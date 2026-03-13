using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using ToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

// ───────────────────────────────────────────────────────────────────
// GrepBenchmark — measures GrepTool throughput and correctness.
//
// Usage:
//   dotnet run                           # run all benchmarks + tests
//   dotnet run -- bench                  # benchmarks only
//   dotnet run -- test                   # comprehensive tests only
//   dotnet run -- bench --iterations 10  # custom iteration count
//   dotnet run -- /path/to/repo          # run all modes against another tree
//   dotnet run -- bench /path/to/repo    # benchmark another tree
// ───────────────────────────────────────────────────────────────────

var options = ParseArgs(args);
if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

if (!string.IsNullOrWhiteSpace(options.Error))
{
    Console.Error.WriteLine(options.Error);
    Console.Error.WriteLine();
    PrintUsage();
    return 1;
}

// Frozen snapshot for repeatable benchmarks + realistic tests.
var snapshotRoot = ResolveSnapshotRoot();

// Allow override for benchmarks against a different tree.
var benchRoot = options.BenchRoot ?? snapshotRoot;
var exitCode = 0;

// Resolve rg once up front — if unavailable, skip benchmarks and note it.
var probePolicy = new FileSystemToolPolicy(benchRoot ?? FindRepoRoot());
var ripgrepAvailable = GrepTool.TryCreate(
    probePolicy, out _, out var rgUnavailableReason);

if (options.Mode is "all" or "bench")
{
    if (benchRoot is null)
    {
        Console.WriteLine("Skipping benchmarks: testdata/ snapshot not found.");
        Console.WriteLine("  Run ./snapshot-testdata.sh to create it.");
    }
    else if (!ripgrepAvailable)
    {
        Console.WriteLine($"Skipping benchmarks: {rgUnavailableReason}");
    }
    else
    {
        RunBenchmarks(benchRoot, options.WarmupRounds, options.IterationCount);
    }
}

if (options.Mode is "all" or "test")
{
    Console.WriteLine();
    var summary = RunComprehensiveTests(snapshotRoot);
    if (summary.Failed > 0)
    {
        exitCode = 1;
    }
}

return exitCode;

// ═══════════════════════════════════════════════════════════════════
//  BENCHMARKS
// ═══════════════════════════════════════════════════════════════════

void RunBenchmarks(string root, int warmup, int iterations)
{
    var treeStats = GetTreeStats(root);
    var ripgrepVersion = GetRipgrepVersion() ?? "unknown";

    Console.WriteLine("GrepBenchmark");
    Console.WriteLine($"  Root:        {root}");
    Console.WriteLine($"  Ripgrep:     {ripgrepVersion}");
    Console.WriteLine($"  Files:       {treeStats.FileCount:N0}");
    Console.WriteLine($"  Directories: {treeStats.DirectoryCount:N0}");
    Console.WriteLine($"  Bytes:       {FormatBytes(treeStats.TotalBytes)}");
    Console.WriteLine($"  Warmup:      {warmup}");
    Console.WriteLine($"  Iterations:  {iterations}");
    Console.WriteLine("  Notes:       timings include process spawn, ripgrep search, JSON parsing, and formatting");
    Console.WriteLine();

    var policy = new FileSystemToolPolicy(root, maxGrepMatches: 200);

    if (!GrepTool.TryCreate(policy, out var tool, out var reason))
    {
        Console.WriteLine($"  SKIP: {reason}");
        return;
    }

    // ── Scenarios ────────────────────────────────────────────────
    // Each scenario exercises a different search shape an agent uses.

    var scenarios = new (string Name, string Pattern, string? Glob, string? Path,
        bool Literal, bool IgnoreCase, bool Word, int ContextLines, int? Limit)[]
    {
        // Literal search across all files — common "find usages" query.
        ("literal, all files",
            "FileSystemToolPolicy", null, null,
            true, false, false, 0, 100),

        // Literal search scoped to *.cs — typical agent grep.
        ("literal, *.cs only",
            "FileSystemToolPolicy", "*.cs", null,
            true, false, false, 0, 100),

        // Case-insensitive literal.
        ("literal, case-insensitive",
            "filesystemtoolpolicy", "*.cs", null,
            true, true, false, 0, 100),

        // Word boundary search — "find exact method name".
        ("word boundary, *.cs",
            "Resolve", "*.cs", null,
            true, false, true, 0, 100),

        // Regex search — find method definitions.
        ("regex, method defs",
            "public\\s+(static\\s+)?\\w+\\s+\\w+\\(", "*.cs", null,
            false, false, false, 0, 100),

        // Context lines — agent wants surrounding code.
        ("literal + 3 context lines",
            "FileSystemToolPolicy", "*.cs", null,
            true, false, false, 3, 100),

        // Scoped to subdirectory — narrow search root.
        ("literal, scoped to Agent/",
            "ExecuteAsync", "*.cs", "Mcp.Net.Agent",
            true, false, false, 0, 100),

        // Very common pattern — many hits, tests truncation.
        ("high-frequency literal",
            "return", "*.cs", null,
            true, false, false, 0, 200),

        // Tight limit — measures early termination.
        ("literal, limit 5",
            "return", "*.cs", null,
            true, false, false, 0, 5),

        // Single file search.
        ("literal, single file",
            "MaxGrepMatches", null, "Mcp.Net.Agent/Tools/FileSystemToolPolicy.cs",
            true, false, false, 0, 100),

        // No matches — measures overhead of full traversal with no output.
        ("no matches (unique string)",
            "xyzzy_9f8a2b_never_matches", null, null,
            true, false, false, 0, 100),
    };

    PrintBenchHeader();

    foreach (var s in scenarios)
    {
        BenchScenario(s.Name, tool!, s.Pattern, s.Glob, s.Path,
            s.Literal, s.IgnoreCase, s.Word, s.ContextLines, s.Limit,
            warmup, iterations);
    }
}

void BenchScenario(
    string name, GrepTool tool,
    string pattern, string? glob, string? path,
    bool literal, bool ignoreCase, bool word,
    int contextLines, int? limit,
    int warmup, int iterations)
{
    var args = new Dictionary<string, object?> { ["pattern"] = pattern };
    if (glob is not null) args["glob"] = glob;
    if (path is not null) args["path"] = path;
    if (!literal) args["literal"] = false;
    if (ignoreCase) args["ignoreCase"] = true;
    if (word) args["word"] = true;
    if (contextLines > 0) args["contextLines"] = contextLines;
    if (limit.HasValue) args["limit"] = (double)limit.Value;

    var invocation = new ToolInvocation("bench-0", "grep_files", args);

    // Warmup
    for (var i = 0; i < warmup; i++)
    {
        tool.ExecuteAsync(invocation, CancellationToken.None).GetAwaiter().GetResult();
    }

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);

    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Before = GC.CollectionCount(0);
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
    var allocPerIteration = (allocAfter - allocBefore) / iterations;

    Array.Sort(timings);
    var mean = timings.Average();
    var p50 = Percentile(timings, 0.50);
    var p95 = Percentile(timings, 0.95);
    var min = timings[0];
    var max = timings[^1];

    var matchCount = 0;
    var filesMatched = 0;
    var truncByMatches = false;
    if (lastResult?.Metadata is { } meta)
    {
        if (meta.TryGetProperty("matchCount", out var mc)) matchCount = mc.GetInt32();
        if (meta.TryGetProperty("filesMatched", out var fm)) filesMatched = fm.GetInt32();
        if (meta.TryGetProperty("truncatedByMatches", out var tb)) truncByMatches = tb.GetBoolean();
    }

    var isError = lastResult?.IsError == true;
    if (isError)
    {
        Console.WriteLine($"  ERROR in '{name}': {lastResult!.Text[0]}");
        return;
    }

    Console.Write($"  {name,-35} | {matchCount,5:N0} | {filesMatched,5:N0} | ");
    Console.Write($"{(truncByMatches ? "yes" : "no"),5} | ");
    Console.Write($"{FormatMicros(mean),7} | {FormatMicros(p50),7} | {FormatMicros(p95),7} | ");
    Console.Write($"{FormatMicros(min),7} | {FormatMicros(max),7} | ");
    Console.WriteLine($"{FormatBytes(allocPerIteration),8}  gc:{gen0Delta}");
}

// ═══════════════════════════════════════════════════════════════════
//  COMPREHENSIVE TESTS
// ═══════════════════════════════════════════════════════════════════

TestSummary RunComprehensiveTests(string? snapshotRoot)
{
    Console.WriteLine("GrepTool Comprehensive Tests");
    Console.WriteLine(new string('=', 60));
    Console.WriteLine();

    var passed = 0;
    var failed = 0;
    var skipped = 0;

    // ── TryCreate / availability ──

    RunTest("TryCreate returns false for missing rg path", ref passed, ref failed, ref skipped, () =>
    {
        using var tmp = new TempDir();
        var policy = new FileSystemToolPolicy(tmp.Path);
        var ok = GrepTool.TryCreate(policy, out var t, out var reason,
            new GrepToolOptions(Path.Combine(tmp.Path, "no-such-rg")));
        Assert(!ok, "Should return false");
        Assert(t is null, "Tool should be null");
        Assert(reason!.Contains("ripgrep", StringComparison.OrdinalIgnoreCase), "Reason mentions ripgrep");
    });

    RunTest("TryCreate returns false for non-executable file", ref passed, ref failed, ref skipped, () =>
    {
        using var tmp = new TempDir();
        var fakePath = Path.Combine(tmp.Path, "rg");
        File.WriteAllText(fakePath, "not an executable");
        var policy = new FileSystemToolPolicy(tmp.Path);
        var ok = GrepTool.TryCreate(policy, out _, out var reason,
            new GrepToolOptions(fakePath));
        Assert(!ok, "Should return false for non-executable");
        Assert(!string.IsNullOrWhiteSpace(reason), "Should provide reason");
    });

    RunTest("TryCreate succeeds with real rg on PATH", ref passed, ref failed, ref skipped, () =>
    {
        using var tmp = new TempDir();
        var policy = new FileSystemToolPolicy(tmp.Path);
        var ok = GrepTool.TryCreate(policy, out var t, out _);
        if (!ok)
        {
            Skip("ripgrep not available on PATH");
            return;
        }
        Assert(t is not null, "Tool should be created");
        Assert(t!.Descriptor.Name == "grep_files", "Tool name should be grep_files");
    });

    // ── Basic search ──

    RunTest("Literal search returns matching lines", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "hello world\ngoodbye world\nhello again\n");

        var r = Grep(env.Tool, "hello");
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("a.txt:1:"), "Should contain line 1 match");
        Assert(text.Contains("a.txt:3:"), "Should contain line 3 match");
        Assert(!text.Contains("a.txt:2:"), "Should not contain non-matching line 2");
        AssertMetaInt(r, "matchCount", 2);
    });

    RunTest("No matches returns 'No matches found'", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "hello world\n");

        var r = Grep(env.Tool, "zzz_no_match");
        AssertNoError(r);
        Assert(r.Text[0] == "No matches found", "Should say no matches");
        AssertMetaInt(r, "matchCount", 0);
    });

    RunTest("Search is literal by default", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        // "foo.bar" should NOT match "fooXbar" when literal
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "foo.bar\nfooXbar\n");

        var r = Grep(env.Tool, "foo.bar");
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("a.txt:1:"), "Literal match on line 1");
        Assert(!text.Contains("a.txt:2:"), "Should not regex-match line 2");
        AssertMetaInt(r, "matchCount", 1);
    });

    RunTest("Regex mode matches with regex semantics", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "foo.bar\nfooXbar\n");

        var r = Grep(env.Tool, "foo.bar", literal: false);
        AssertNoError(r);
        var text = r.Text[0];
        // "foo.bar" as regex matches both lines (. matches any char)
        Assert(text.Contains("a.txt:1:"), "Regex match line 1");
        Assert(text.Contains("a.txt:2:"), "Regex match line 2");
        AssertMetaInt(r, "matchCount", 2);
    });

    // ── Case sensitivity ──

    RunTest("Case-sensitive by default", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "Hello\nhello\nHELLO\n");

        var r = Grep(env.Tool, "Hello");
        AssertNoError(r);
        AssertMetaInt(r, "matchCount", 1);
    });

    RunTest("Case-insensitive when requested", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "Hello\nhello\nHELLO\n");

        var r = Grep(env.Tool, "Hello", ignoreCase: true);
        AssertNoError(r);
        AssertMetaInt(r, "matchCount", 3);
    });

    // ── Word boundary ──

    RunTest("Word boundary restricts matches", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "cat\ncatch\nthe cat sat\ncatalog\n");

        var r = Grep(env.Tool, "cat", word: true);
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("a.txt:1:"), "Standalone 'cat'");
        Assert(text.Contains("a.txt:3:"), "'cat' as word in sentence");
        Assert(!text.Contains("a.txt:2:"), "'catch' should not match word boundary");
        Assert(!text.Contains("a.txt:4:"), "'catalog' should not match word boundary");
        AssertMetaInt(r, "matchCount", 2);
    });

    // ── Glob file filter ──

    RunTest("Glob filter restricts to matching files", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.cs"), "target\n");
        File.WriteAllText(Path.Combine(env.Dir, "b.txt"), "target\n");
        File.WriteAllText(Path.Combine(env.Dir, "c.cs"), "target\n");

        var r = Grep(env.Tool, "target", glob: "*.cs");
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("a.cs:1:"), "a.cs should match");
        Assert(text.Contains("c.cs:1:"), "c.cs should match");
        Assert(!text.Contains("b.txt"), "b.txt should be excluded by glob");
        AssertMetaInt(r, "matchCount", 2);
    });

    // ── Path scoping ──

    RunTest("Path scopes search to subdirectory", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        Directory.CreateDirectory(Path.Combine(env.Dir, "sub"));
        File.WriteAllText(Path.Combine(env.Dir, "root.txt"), "needle\n");
        File.WriteAllText(Path.Combine(env.Dir, "sub", "deep.txt"), "needle\n");

        var r = Grep(env.Tool, "needle", path: "sub");
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("sub/deep.txt:1:") || text.Contains("deep.txt:1:"),
            "Should find match in subdirectory");
        Assert(!text.Contains("root.txt"), "Should not search outside scoped path");
    });

    RunTest("Path can target a single file", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "alpha\nbeta\n");
        File.WriteAllText(Path.Combine(env.Dir, "b.txt"), "alpha\ngamma\n");

        var r = Grep(env.Tool, "alpha", path: "a.txt");
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("a.txt:1:"), "Should match in targeted file");
        Assert(!text.Contains("b.txt"), "Should not search other files");
        AssertMetaInt(r, "matchCount", 1);
    });

    // ── Context lines ──

    RunTest("Context lines show surrounding code", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv(maxGrepContextLines: 2);
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"),
            "line1\nline2\nTARGET\nline4\nline5\n");

        var r = Grep(env.Tool, "TARGET", contextLines: 1);
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("a.txt:3:") || text.Contains("a.txt:3: TARGET"),
            "Should have match line");
        // Context lines use dash separator
        Assert(text.Contains("a.txt-2-") || text.Contains("a.txt-4-"),
            "Should have at least one context line");
    });

    RunTest("Context lines clamped to policy max", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv(maxGrepContextLines: 1);
        var r = Grep(env.Tool, "TARGET", contextLines: 99);
        // Should not error — just clamps
        AssertNoError(r);
        var meta = r.Metadata!.Value;
        var actualContext = meta.GetProperty("contextLines").GetInt32();
        Assert(actualContext <= 1, $"Context should be clamped to 1, got {actualContext}");
    });

    // ── Match limit ──

    RunTest("Limit caps returned matches", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv(maxGrepMatches: 5);
        var sb = new StringBuilder();
        for (var i = 0; i < 20; i++) sb.AppendLine("needle");
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), sb.ToString());

        var r = Grep(env.Tool, "needle", limit: 3);
        AssertNoError(r);
        AssertMetaInt(r, "matchCount", 3);
        AssertMetaBool(r, "truncatedByMatches", true);
    });

    RunTest("Limit clamped to policy maximum", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv(maxGrepMatches: 5);
        var sb = new StringBuilder();
        for (var i = 0; i < 20; i++) sb.AppendLine("needle");
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), sb.ToString());

        // Request limit=50 but policy max is 5
        var r = Grep(env.Tool, "needle", limit: 50);
        AssertNoError(r);
        var meta = r.Metadata!.Value;
        Assert(meta.GetProperty("limit").GetInt32() == 5, "Limit should be clamped to policy max");
        AssertMetaInt(r, "matchCount", 5);
        AssertMetaBool(r, "truncatedByMatches", true);
    });

    // ── Output truncation ──

    RunTest("Long lines are truncated", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv(maxGrepLineLength: 20);
        var longLine = "needle " + new string('x', 500) + "\n";
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), longLine);

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        Assert(r.Text[0].Contains("... [truncated]"), "Long line should be truncated");
        AssertMetaBool(r, "linesTruncated", true);
    });

    RunTest("Output byte limit triggers truncation", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv(maxGrepOutputBytes: 256);
        var sb = new StringBuilder();
        for (var i = 0; i < 100; i++)
            sb.AppendLine($"needle-{i:D4} " + new string('a', 50));
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), sb.ToString());

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        AssertMetaBool(r, "truncatedByBytes", true);
    });

    // ── Separator between files / non-contiguous blocks ──

    RunTest("Separator emitted between files", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "needle\n");
        File.WriteAllText(Path.Combine(env.Dir, "b.txt"), "needle\n");

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        Assert(r.Text[0].Contains("--"), "Should have separator between file groups");
        AssertMetaInt(r, "matchCount", 2);
    });

    // ── Skip directories ──

    RunTest("Skipped directories excluded by default", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        Directory.CreateDirectory(Path.Combine(env.Dir, "src"));
        Directory.CreateDirectory(Path.Combine(env.Dir, "node_modules", "pkg"));
        File.WriteAllText(Path.Combine(env.Dir, "src", "a.txt"), "needle\n");
        File.WriteAllText(Path.Combine(env.Dir, "node_modules", "pkg", "b.txt"), "needle\n");

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("src/a.txt") || text.Contains("src\\a.txt"),
            "Should find match in src/");
        Assert(!text.Contains("node_modules"), "Should skip node_modules/");
    });

    // ── Error handling ──

    RunTest("Empty pattern returns error", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        var r = Grep(env.Tool, "");
        Assert(r.IsError, "Empty pattern should error");
        Assert(r.Text[0].Contains("pattern", StringComparison.OrdinalIgnoreCase),
            "Error should mention pattern");
    });

    RunTest("Negative limit returns error", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        var r = Grep(env.Tool, "needle", limit: -1);
        Assert(r.IsError, "Negative limit should error");
    });

    RunTest("Negative contextLines returns error", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        var r = Grep(env.Tool, "needle", contextLines: -1);
        Assert(r.IsError, "Negative contextLines should error");
    });

    RunTest("Non-existent path returns error", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        var r = Grep(env.Tool, "needle", path: "no-such-dir");
        Assert(r.IsError, "Non-existent path should error");
    });

    RunTest("Path outside root returns error", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        var r = Grep(env.Tool, "needle", path: "../../etc/passwd");
        Assert(r.IsError, "Path traversal should error");
    });

    RunTest("Invalid regex returns error when literal=false", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "hello\n");
        var r = Grep(env.Tool, "(unclosed", literal: false);
        Assert(r.IsError, "Invalid regex should error");
    });

    // ── Metadata completeness ──

    RunTest("Metadata includes all expected fields", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "needle\nhaystack\nneedle again\n");

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        var meta = r.Metadata!.Value;

        Assert(meta.TryGetProperty("path", out _), "metadata.path");
        Assert(meta.TryGetProperty("pattern", out _), "metadata.pattern");
        Assert(meta.TryGetProperty("literal", out _), "metadata.literal");
        Assert(meta.TryGetProperty("ignoreCase", out _), "metadata.ignoreCase");
        Assert(meta.TryGetProperty("word", out _), "metadata.word");
        Assert(meta.TryGetProperty("contextLines", out _), "metadata.contextLines");
        Assert(meta.TryGetProperty("limit", out _), "metadata.limit");
        Assert(meta.TryGetProperty("filesSearched", out _), "metadata.filesSearched");
        Assert(meta.TryGetProperty("filesMatched", out _), "metadata.filesMatched");
        Assert(meta.TryGetProperty("matchCount", out _), "metadata.matchCount");
        Assert(meta.TryGetProperty("truncatedByMatches", out _), "metadata.truncatedByMatches");
        Assert(meta.TryGetProperty("truncatedByBytes", out _), "metadata.truncatedByBytes");
        Assert(meta.TryGetProperty("linesTruncated", out _), "metadata.linesTruncated");
        Assert(meta.TryGetProperty("engine", out _), "metadata.engine");
        Assert(meta.GetProperty("engine").GetString() == "ripgrep", "engine should be ripgrep");
    });

    // ── Output format ──

    RunTest("Output uses displayPath:lineNumber: format", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        Directory.CreateDirectory(Path.Combine(env.Dir, "sub"));
        File.WriteAllText(Path.Combine(env.Dir, "sub", "file.cs"), "aaa\nneedle\nccc\n");

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        // Should be relative path with forward slashes
        Assert(r.Text[0].Contains("sub/file.cs:2: needle") || r.Text[0].Contains("sub/file.cs:2:"),
            $"Expected 'sub/file.cs:2:' format, got: {r.Text[0]}");
    });

    // ── Multi-file deterministic ordering ──

    RunTest("Results are sorted by path", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        // Create files in reverse alphabetical order
        File.WriteAllText(Path.Combine(env.Dir, "z.txt"), "needle\n");
        File.WriteAllText(Path.Combine(env.Dir, "m.txt"), "needle\n");
        File.WriteAllText(Path.Combine(env.Dir, "a.txt"), "needle\n");

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        var text = r.Text[0];
        var aIdx = text.IndexOf("a.txt", StringComparison.Ordinal);
        var mIdx = text.IndexOf("m.txt", StringComparison.Ordinal);
        var zIdx = text.IndexOf("z.txt", StringComparison.Ordinal);
        Assert(aIdx >= 0 && mIdx >= 0 && zIdx >= 0, "All files should appear");
        Assert(aIdx < mIdx && mIdx < zIdx, "Results should be sorted alphabetically");
    });

    // ── Binary file handling ──

    RunTest("Binary files are skipped", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        File.WriteAllText(Path.Combine(env.Dir, "text.txt"), "needle\n");
        // Write a file with null bytes (binary)
        File.WriteAllBytes(Path.Combine(env.Dir, "binary.bin"),
            Encoding.UTF8.GetBytes("needle\0binary content"));

        var r = Grep(env.Tool, "needle");
        AssertNoError(r);
        var text = r.Text[0];
        Assert(text.Contains("text.txt"), "Text file should match");
        // rg skips binary files by default
        Assert(!text.Contains("binary.bin"), "Binary file should be skipped");
    });

    // ── Descriptor ──

    RunTest("Descriptor has correct schema", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateTestEnv();
        var desc = env.Tool.Descriptor;
        Assert(desc.Name == "grep_files", $"Name should be grep_files, got {desc.Name}");
        Assert(!string.IsNullOrWhiteSpace(desc.Description), "Should have description");

        var schema = desc.InputSchema;
        Assert(schema.TryGetProperty("properties", out var props), "Should have properties");
        Assert(props.TryGetProperty("pattern", out _), "Should have pattern property");
        Assert(props.TryGetProperty("path", out _), "Should have path property");
        Assert(props.TryGetProperty("glob", out _), "Should have glob property");
        Assert(props.TryGetProperty("literal", out _), "Should have literal property");
        Assert(props.TryGetProperty("ignoreCase", out _), "Should have ignoreCase property");
        Assert(props.TryGetProperty("word", out _), "Should have word property");
        Assert(props.TryGetProperty("contextLines", out _), "Should have contextLines property");
        Assert(props.TryGetProperty("limit", out _), "Should have limit property");

        Assert(schema.TryGetProperty("required", out var req), "Should have required");
        var requiredFields = req.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert(requiredFields.Contains("pattern"), "pattern should be required");
    });

    // ── Realistic volume tests against frozen snapshot ──

    if (snapshotRoot is not null)
    {
        Console.WriteLine();
        Console.WriteLine("  ── Snapshot-based realistic tests ──");
        Console.WriteLine();

        RunTest("Snapshot: literal search finds matches across many files", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var r = Grep(env.Tool, "namespace");
            AssertNoError(r);
            var meta = r.Metadata!.Value;
            var matchCount = meta.GetProperty("matchCount").GetInt32();
            var filesMatched = meta.GetProperty("filesMatched").GetInt32();
            Assert(matchCount > 10, $"Expected many matches, got {matchCount}");
            Assert(filesMatched > 5, $"Expected matches across multiple files, got {filesMatched}");
        });

        RunTest("Snapshot: glob filter narrows search to *.cs files only", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            // "sealed class" is common in .cs but rare/absent in .md
            var rCs = Grep(env.Tool, "sealed class", glob: "*.cs");
            var rMd = Grep(env.Tool, "sealed class", glob: "*.md");
            AssertNoError(rCs);
            AssertNoError(rMd);

            var csMatches = rCs.Metadata!.Value.GetProperty("matchCount").GetInt32();
            var mdMatches = rMd.Metadata!.Value.GetProperty("matchCount").GetInt32();
            Assert(csMatches > 0, $"*.cs should have 'sealed class' hits, got {csMatches}");
            Assert(csMatches > mdMatches, $"*.cs ({csMatches}) should have more 'sealed class' hits than *.md ({mdMatches})");
        });

        RunTest("Snapshot: scoped path searches only subdirectory", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var rScoped = Grep(env.Tool, "using", glob: "*.cs", path: "Mcp.Net.Agent");
            var rFull = Grep(env.Tool, "using", glob: "*.cs");
            AssertNoError(rScoped);
            AssertNoError(rFull);

            var scopedMatches = rScoped.Metadata!.Value.GetProperty("matchCount").GetInt32();
            var fullMatches = rFull.Metadata!.Value.GetProperty("matchCount").GetInt32();
            Assert(scopedMatches > 0, "Scoped search should find matches");
            Assert(fullMatches >= scopedMatches, $"Full search ({fullMatches}) should find >= scoped ({scopedMatches})");
        });

        RunTest("Snapshot: regex finds method signatures across codebase", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var r = Grep(env.Tool, @"public\s+(static\s+)?\w+\s+\w+\(", glob: "*.cs", literal: false);
            AssertNoError(r);
            var matchCount = r.Metadata!.Value.GetProperty("matchCount").GetInt32();
            Assert(matchCount > 5, $"Expected multiple method signatures, got {matchCount}");
        });

        RunTest("Snapshot: word boundary avoids substring matches", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var rWord = Grep(env.Tool, "Path", glob: "*.cs", word: true);
            var rLiteral = Grep(env.Tool, "Path", glob: "*.cs");
            AssertNoError(rWord);
            AssertNoError(rLiteral);

            var wordMatches = rWord.Metadata!.Value.GetProperty("matchCount").GetInt32();
            var literalMatches = rLiteral.Metadata!.Value.GetProperty("matchCount").GetInt32();
            // Literal matches "FilePath", "RootPath", etc. — word boundary should find fewer
            Assert(wordMatches <= literalMatches,
                $"Word ({wordMatches}) should find <= literal ({literalMatches})");
        });

        RunTest("Snapshot: case-insensitive finds more than case-sensitive", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var rSensitive = Grep(env.Tool, "public", glob: "*.cs");
            var rInsensitive = Grep(env.Tool, "public", glob: "*.cs", ignoreCase: true);
            AssertNoError(rSensitive);
            AssertNoError(rInsensitive);

            var sensitiveCount = rSensitive.Metadata!.Value.GetProperty("matchCount").GetInt32();
            var insensitiveCount = rInsensitive.Metadata!.Value.GetProperty("matchCount").GetInt32();
            Assert(insensitiveCount >= sensitiveCount,
                $"Case-insensitive ({insensitiveCount}) should find >= case-sensitive ({sensitiveCount})");
        });

        RunTest("Snapshot: context lines produce larger output", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var rNoCtx = Grep(env.Tool, "FileSystemToolPolicy", glob: "*.cs");
            var rWithCtx = Grep(env.Tool, "FileSystemToolPolicy", glob: "*.cs", contextLines: 2);
            AssertNoError(rNoCtx);
            AssertNoError(rWithCtx);

            var noCtxLen = rNoCtx.Text[0].Length;
            var withCtxLen = rWithCtx.Text[0].Length;
            Assert(withCtxLen > noCtxLen,
                $"Context output ({withCtxLen}) should be longer than no-context ({noCtxLen})");
        });

        RunTest("Snapshot: match limit truncates high-frequency pattern", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot, maxGrepMatches: 10);
            var r = Grep(env.Tool, "return", glob: "*.cs", limit: 10);
            AssertNoError(r);
            AssertMetaInt(r, "matchCount", 10);
            AssertMetaBool(r, "truncatedByMatches", true);
        });

        RunTest("Snapshot: output byte limit truncates large result", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot, maxGrepOutputBytes: 512);
            var r = Grep(env.Tool, "return", glob: "*.cs");
            AssertNoError(r);
            AssertMetaBool(r, "truncatedByBytes", true);
        });

        RunTest("Snapshot: no-match string returns zero across entire tree", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var r = Grep(env.Tool, "xyzzy_9f8a2b_never_matches");
            AssertNoError(r);
            Assert(r.Text[0] == "No matches found", "Should say no matches");
            AssertMetaInt(r, "matchCount", 0);
        });

        RunTest("Snapshot: skip directories excluded from results", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var r = Grep(env.Tool, "namespace", glob: "*.cs");
            AssertNoError(r);
            var text = r.Text[0];
            Assert(!text.Contains("/bin/"), "bin/ should be excluded");
            Assert(!text.Contains("/obj/"), "obj/ should be excluded");
            Assert(!text.Contains("/node_modules/"), "node_modules/ should be excluded");
        });

        RunTest("Snapshot: deterministic output order across runs", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var r1 = Grep(env.Tool, "sealed class", glob: "*.cs");
            var r2 = Grep(env.Tool, "sealed class", glob: "*.cs");
            AssertNoError(r1);
            AssertNoError(r2);
            Assert(r1.Text[0] == r2.Text[0], "Two identical searches should produce identical output");
        });

        RunTest("Snapshot: filesSearched > 0 in metadata", ref passed, ref failed, ref skipped, () =>
        {
            using var env = CreateSnapshotEnv(snapshotRoot);
            var r = Grep(env.Tool, "using", glob: "*.cs");
            AssertNoError(r);
            var filesSearched = r.Metadata!.Value.GetProperty("filesSearched").GetInt32();
            Assert(filesSearched > 10, $"Expected many files searched, got {filesSearched}");
        });
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("  SKIP  Snapshot-based tests (testdata/ not found — run ./snapshot-testdata.sh)");
    }

    // ── Summary ──

    Console.WriteLine();
    Console.WriteLine($"Results: {passed} passed, {failed} failed, {skipped} skipped, {passed + failed + skipped} total");
    return new TestSummary(passed, failed, skipped);
}

// ═══════════════════════════════════════════════════════════════════
//  HELPERS
// ═══════════════════════════════════════════════════════════════════

TestEnv CreateTestEnv(
    int maxGrepMatches = 100,
    int maxGrepOutputBytes = 64 * 1024,
    int maxGrepLineLength = 500,
    int maxGrepContextLines = 3)
{
    var dir = Path.Combine(Path.GetTempPath(), "mcp-grep-test", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);

    var policy = new FileSystemToolPolicy(dir,
        maxGrepMatches: maxGrepMatches,
        maxGrepOutputBytes: maxGrepOutputBytes,
        maxGrepLineLength: maxGrepLineLength,
        maxGrepContextLines: maxGrepContextLines);

    if (!GrepTool.TryCreate(policy, out var tool, out var reason))
    {
        throw new InvalidOperationException($"Cannot create GrepTool for tests: {reason}");
    }

    return new TestEnv(dir, policy, tool!);
}

SnapshotEnv CreateSnapshotEnv(
    string snapshotRoot,
    int maxGrepMatches = 100,
    int maxGrepOutputBytes = 64 * 1024,
    int maxGrepLineLength = 500,
    int maxGrepContextLines = 3)
{
    var policy = new FileSystemToolPolicy(snapshotRoot,
        maxGrepMatches: maxGrepMatches,
        maxGrepOutputBytes: maxGrepOutputBytes,
        maxGrepLineLength: maxGrepLineLength,
        maxGrepContextLines: maxGrepContextLines);

    if (!GrepTool.TryCreate(policy, out var tool, out var reason))
    {
        throw new InvalidOperationException($"Cannot create GrepTool for snapshot tests: {reason}");
    }

    return new SnapshotEnv(snapshotRoot, policy, tool!);
}

ToolInvocationResult Grep(
    GrepTool tool, string pattern,
    string? glob = null, string? path = null,
    bool? literal = null, bool? ignoreCase = null,
    bool? word = null, int? contextLines = null, int? limit = null)
{
    var args = new Dictionary<string, object?> { ["pattern"] = pattern };
    if (glob is not null) args["glob"] = glob;
    if (path is not null) args["path"] = path;
    if (literal.HasValue) args["literal"] = literal.Value;
    if (ignoreCase.HasValue) args["ignoreCase"] = ignoreCase.Value;
    if (word.HasValue) args["word"] = word.Value;
    if (contextLines.HasValue) args["contextLines"] = contextLines.Value;
    if (limit.HasValue) args["limit"] = (double)limit.Value;

    return tool.ExecuteAsync(
        new ToolInvocation("test", "grep_files", args),
        CancellationToken.None
    ).GetAwaiter().GetResult();
}

void RunTest(string name, ref int passed, ref int failed, ref int skipped, Action test)
{
    try
    {
        test();
        Console.WriteLine($"  PASS  {name}");
        passed++;
    }
    catch (SkipException ex)
    {
        Console.WriteLine($"  SKIP  {name}: {ex.Message}");
        skipped++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL  {name}");
        Console.WriteLine($"        {ex.Message}");
        failed++;
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception($"Assertion failed: {message}");
}

static void AssertNoError(ToolInvocationResult r)
{
    if (r.IsError) throw new Exception($"Unexpected error: {r.Text[0]}");
}

static void AssertMetaInt(ToolInvocationResult r, string key, int expected)
{
    var actual = r.Metadata!.Value.GetProperty(key).GetInt32();
    Assert(actual == expected, $"Metadata '{key}': expected {expected}, got {actual}");
}

static void AssertMetaBool(ToolInvocationResult r, string key, bool expected)
{
    var actual = r.Metadata!.Value.GetProperty(key).GetBoolean();
    Assert(actual == expected, $"Metadata '{key}': expected {expected}, got {actual}");
}

static void Skip(string message) => throw new SkipException(message);

static void PrintBenchHeader()
{
    Console.WriteLine($"  {"Scenario",-35} | {"Hits",5} | {"Files",5} | {"Trunc",5} | {"Mean",7} | {"P50",7} | {"P95",7} | {"Min",7} | {"Max",7} | {"Alloc/iter",8}");
    Console.WriteLine($"  {new string('-', 35)}-+-{new string('-', 5)}-+-{new string('-', 5)}-+-{new string('-', 5)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 14)}");
}

static string FormatMicros(double us)
{
    if (us < 1000) return $"{us:F0}us";
    return us < 1_000_000 ? $"{us / 1000:F1}ms" : $"{us / 1_000_000:F2}s";
}

static string FormatBytes(long bytes)
{
    if (bytes < 1024) return $"{bytes}B";
    return bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1}KB" : $"{bytes / (1024.0 * 1024):F1}MB";
}

static double Percentile(double[] sortedTimings, double percentile)
{
    if (sortedTimings.Length == 0)
    {
        throw new ArgumentException("Percentile requires at least one value.", nameof(sortedTimings));
    }

    if (sortedTimings.Length == 1)
    {
        return sortedTimings[0];
    }

    var position = (sortedTimings.Length - 1) * percentile;
    var lowerIndex = (int)Math.Floor(position);
    var upperIndex = (int)Math.Ceiling(position);

    if (lowerIndex == upperIndex)
    {
        return sortedTimings[lowerIndex];
    }

    var weight = position - lowerIndex;
    return sortedTimings[lowerIndex] + ((sortedTimings[upperIndex] - sortedTimings[lowerIndex]) * weight);
}

static TreeStats GetTreeStats(string root)
{
    var fileCount = 0;
    var directoryCount = 1;
    long totalBytes = 0;

    foreach (var _ in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
    {
        directoryCount++;
    }

    foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        fileCount++;
        totalBytes += new FileInfo(filePath).Length;
    }

    return new TreeStats(fileCount, directoryCount, totalBytes);
}

static string? GetRipgrepVersion()
{
    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "rg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--version");

        if (!process.Start())
        {
            return null;
        }

        var firstLine = process.StandardOutput.ReadLine();
        if (!process.WaitForExit(2000) || process.ExitCode != 0)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine.Trim();
    }
    catch
    {
        return null;
    }
}

static string? ResolveSnapshotRoot()
{
    // Look for testdata/ relative to the script source location.
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        var candidate = Path.Combine(dir, "scripts", "GrepBenchmark", "testdata");
        if (Directory.Exists(candidate)) return candidate;
        // Also check if we're already inside scripts/GrepBenchmark/
        candidate = Path.Combine(dir, "testdata");
        if (Directory.Exists(candidate)) return candidate;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return Directory.GetCurrentDirectory();
}

static CliOptions ParseArgs(string[] args)
{
    var mode = "all";
    string? benchRoot = null;
    var iterationCount = 10;
    var warmupRounds = 3;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        switch (arg)
        {
            case "--help":
            case "-h":
                return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, true, null);
            case "--iterations":
                if (i == args.Length - 1 || !int.TryParse(args[i + 1], out iterationCount))
                {
                    return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false,
                        "The '--iterations' option requires an integer value.");
                }
                i++;
                continue;
            case "--warmup":
                if (i == args.Length - 1 || !int.TryParse(args[i + 1], out warmupRounds))
                {
                    return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false,
                        "The '--warmup' option requires an integer value.");
                }
                i++;
                continue;
        }

        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false,
                $"Unknown option '{arg}'.");
        }

        if (arg is "all" or "bench" or "test")
        {
            mode = arg;
            continue;
        }

        if (Directory.Exists(arg))
        {
            if (benchRoot is not null)
            {
                return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false,
                    "Only one benchmark root directory can be provided.");
            }

            benchRoot = Path.GetFullPath(arg);
            continue;
        }

        return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false,
            $"Unrecognized argument '{arg}'.");
    }

    if (iterationCount <= 0)
    {
        return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false,
            "The '--iterations' value must be greater than zero.");
    }

    if (warmupRounds < 0)
    {
        return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false,
            "The '--warmup' value must be zero or greater.");
    }

    return new CliOptions(mode, benchRoot, iterationCount, warmupRounds, false, null);
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project scripts/GrepBenchmark/GrepBenchmark.csproj -- [mode] [path] [options]");
    Console.WriteLine();
    Console.WriteLine("Modes:");
    Console.WriteLine("  all     Run benchmarks and comprehensive smoke tests (default)");
    Console.WriteLine("  bench   Run benchmarks only");
    Console.WriteLine("  test    Run comprehensive smoke tests only");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --iterations <n>   Number of measured iterations per scenario (must be > 0)");
    Console.WriteLine("  --warmup <n>       Number of warmup iterations per scenario (must be >= 0)");
    Console.WriteLine("  --help             Show this help text");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run --project scripts/GrepBenchmark/GrepBenchmark.csproj");
    Console.WriteLine("  dotnet run --project scripts/GrepBenchmark/GrepBenchmark.csproj -- bench --iterations 20");
    Console.WriteLine("  dotnet run --project scripts/GrepBenchmark/GrepBenchmark.csproj -- bench /path/to/repo");
    Console.WriteLine("  dotnet run --project scripts/GrepBenchmark/GrepBenchmark.csproj -- /path/to/repo");
}

sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "mcp-grep-test", Guid.NewGuid().ToString("n"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

sealed record TestEnv(string Dir, FileSystemToolPolicy Policy, GrepTool Tool) : IDisposable
{
    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { }
    }
}

// Snapshot env does NOT dispose — the snapshot is persistent.
sealed record SnapshotEnv(string Dir, FileSystemToolPolicy Policy, GrepTool Tool) : IDisposable
{
    public void Dispose() { }
}

sealed class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}

sealed record CliOptions(
    string Mode,
    string? BenchRoot,
    int IterationCount,
    int WarmupRounds,
    bool ShowHelp,
    string? Error);

sealed record TestSummary(int Passed, int Failed, int Skipped);

sealed record TreeStats(int FileCount, int DirectoryCount, long TotalBytes);
