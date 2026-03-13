using System.Diagnostics;
using System.Text.Json;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using ToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

// ───────────────────────────────────────────────────────────────────
// GlobBenchmark — measures GlobTool throughput against a real tree.
//
// Usage:
//   dotnet run                                  # benchmarks this repo
//   dotnet run -- /path/to/large/repo           # benchmarks another tree
//   dotnet run -- /path/to/large/repo --warmup 3 --iterations 10
// ───────────────────────────────────────────────────────────────────

var rootPath = args.Length > 0 && !args[0].StartsWith("--")
    ? Path.GetFullPath(args[0])
    : FindRepoRoot();
var warmupRounds = GetIntArg(args, "--warmup", 3);
var iterationCount = GetIntArg(args, "--iterations", 10);

if (!Directory.Exists(rootPath))
{
    Console.Error.WriteLine($"Directory not found: {rootPath}");
    return 1;
}

Console.WriteLine("GlobBenchmark");
Console.WriteLine($"  Root:        {rootPath}");
Console.WriteLine($"  Warmup:      {warmupRounds}");
Console.WriteLine($"  Iterations:  {iterationCount}");
Console.WriteLine();

// Count total filesystem entries for context.
var entryOptions = new EnumerationOptions
{
    RecurseSubdirectories = true,
    IgnoreInaccessible = true,
    AttributesToSkip = FileAttributes.ReparsePoint,
};
var totalFiles = Directory.EnumerateFiles(rootPath, "*", entryOptions).Count();
var totalDirs = Directory.EnumerateDirectories(rootPath, "*", entryOptions).Count();
Console.WriteLine($"  Tree size:   {totalFiles:N0} files, {totalDirs:N0} directories");
Console.WriteLine();

// ── Scenarios ────────────────────────────────────────────────────
// Each scenario is a (name, pattern, path, limit) tuple exercising
// a different traversal shape the agent will use in practice.

var scenarios = new (string Name, string Pattern, string? Path, int? Limit)[]
{
    // Broad recursive — touches every directory, early-terminates at limit.
    ("**/*  (limit 200)", "**/*", null, 200),

    // Extension filter — common agent query, span-based EndsWith fast path.
    ("**/*.cs  (limit 200)", "**/*.cs", null, 200),

    // Extension filter, high limit — measures full-tree scan cost.
    ("**/*.cs  (limit 5000)", "**/*.cs", null, 5000),

    // Literal prefix narrowing — should skip most of the tree.
    ("Agent/**/*.cs  (limit 200)", "**/*.cs", "Mcp.Net.Agent", 200),

    // Single-level, no recursion.
    ("*.sln  (limit 200)", "*.sln", null, 200),

    // Wildcard directory segment — bounded depth.
    ("*/*.csproj  (limit 200)", "*/*.csproj", null, 200),

    // DoubleStar + intermediate literal — exercises double-star traversal.
    ("**/Tools/*.cs  (limit 200)", "**/Tools/*.cs", null, 200),

    // Wildcard in filename — common agent discovery query.
    ("**/*Test*.cs  (limit 200)", "**/*Test*.cs", null, 200),

    // Very tight limit — measures early termination overhead.
    ("**/*  (limit 5)", "**/*", null, 5),

    // No limit override — uses policy default (200).
    ("**/*.cs  (policy default)", "**/*.cs", null, null),
};

// ── Run ──────────────────────────────────────────────────────────

var policyDefault = new FileSystemToolPolicy(rootPath, maxGlobMatches: 5000);
var toolDefault = new GlobTool(policyDefault);

var policyNoSkip = new FileSystemToolPolicy(
    rootPath,
    maxGlobMatches: 5000,
    skippedDirectoryNames: Array.Empty<string>()
);
var toolNoSkip = new GlobTool(policyNoSkip);

PrintHeader();

foreach (var (name, pattern, path, limit) in scenarios)
{
    RunScenario(name, pattern, path, limit, toolDefault, warmupRounds, iterationCount);
}

// Re-run the broad scan without skip list to show the impact.
Console.WriteLine();
Console.WriteLine("── Without skip list (.git, node_modules, etc. included) ──");
Console.WriteLine();
PrintHeader();

RunScenario("**/*.cs  no-skip (200)", "**/*.cs", null, 200, toolNoSkip, warmupRounds, iterationCount);
RunScenario("**/*  no-skip (200)", "**/*", null, 200, toolNoSkip, warmupRounds, iterationCount);

return 0;

// ── Helpers ──────────────────────────────────────────────────────

void RunScenario(
    string name,
    string pattern,
    string? path,
    int? limit,
    GlobTool tool,
    int warmup,
    int iterations
)
{
    var invocationArgs = new Dictionary<string, object?> { ["pattern"] = pattern };
    if (path != null)
    {
        invocationArgs["path"] = path;
    }

    if (limit.HasValue)
    {
        invocationArgs["limit"] = (double)limit.Value;
    }

    var invocation = new ToolInvocation("bench-0", "glob_files", invocationArgs);

    // Warmup — prime filesystem caches.
    for (var i = 0; i < warmup; i++)
    {
        tool.ExecuteAsync(invocation, CancellationToken.None).GetAwaiter().GetResult();
    }

    // Force GC before measured runs.
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);

    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Before = GC.CollectionCount(0);
    var gen1Before = GC.CollectionCount(1);
    var gen2Before = GC.CollectionCount(2);

    var timings = new double[iterations];
    ToolInvocationResult? lastResult = null;

    for (var i = 0; i < iterations; i++)
    {
        var sw = Stopwatch.StartNew();
        lastResult = tool.ExecuteAsync(invocation, CancellationToken.None).GetAwaiter().GetResult();
        sw.Stop();
        timings[i] = sw.Elapsed.TotalMicroseconds;
    }

    var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Delta = GC.CollectionCount(0) - gen0Before;
    var gen1Delta = GC.CollectionCount(1) - gen1Before;
    var gen2Delta = GC.CollectionCount(2) - gen2Before;
    var allocPerIteration = (allocAfter - allocBefore) / iterations;

    Array.Sort(timings);
    var mean = timings.Average();
    var min = timings[0];
    var max = timings[^1];

    // Extract metadata from result.
    var matchCount = 0;
    var dirsVisited = 0;
    var truncated = false;
    if (lastResult?.Metadata is { } meta)
    {
        if (meta.TryGetProperty("returnedCount", out var rc))
        {
            matchCount = rc.GetInt32();
        }

        if (meta.TryGetProperty("directoriesVisited", out var dv))
        {
            dirsVisited = dv.GetInt32();
        }

        if (meta.TryGetProperty("truncated", out var tr))
        {
            truncated = tr.GetBoolean();
        }
    }

    Console.Write($"  {name,-43} | {matchCount,6:N0} | {dirsVisited,5:N0} | ");
    Console.Write($"{(truncated ? "yes" : "no"),5} | ");
    Console.Write($"{FormatMicros(mean),7} | {FormatMicros(min),7} | {FormatMicros(max),7} | ");
    Console.WriteLine($"{FormatBytes(allocPerIteration),8}  gc:{gen0Delta}/{gen1Delta}/{gen2Delta}");
}

static void PrintHeader()
{
    Console.WriteLine($"  {"Scenario",-43} | {"Hits",6} | {"Dirs",5} | {"Trunc",5} | {"Mean",7} | {"Min",7} | {"Max",7} | {"Alloc/iter",8}");
    Console.WriteLine($"  {new string('-', 43)}-+-{new string('-', 6)}-+-{new string('-', 5)}-+-{new string('-', 5)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 14)}");
}

static string FormatMicros(double us)
{
    if (us < 1000)
    {
        return $"{us:F0}us";
    }

    return us < 1_000_000
        ? $"{us / 1000:F1}ms"
        : $"{us / 1_000_000:F2}s";
}

static string FormatBytes(long bytes)
{
    if (bytes < 1024)
    {
        return $"{bytes}B";
    }

    return bytes < 1024 * 1024
        ? $"{bytes / 1024.0:F1}KB"
        : $"{bytes / (1024.0 * 1024):F1}MB";
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git")))
        {
            return dir;
        }

        dir = Path.GetDirectoryName(dir);
    }

    return Directory.GetCurrentDirectory();
}

static int GetIntArg(string[] args, string name, int defaultValue)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name && int.TryParse(args[i + 1], out var value))
        {
            return value;
        }
    }

    return defaultValue;
}
