using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using ToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

// ───────────────────────────────────────────────────────────────────
// ShellBenchmark — measures RunShellCommandTool throughput and correctness.
//
// Usage:
//   dotnet run                           # run all benchmarks + tests
//   dotnet run -- bench                  # benchmarks only
//   dotnet run -- test                   # comprehensive tests only
//   dotnet run -- bench --iterations 10  # custom iteration count
// ───────────────────────────────────────────────────────────────────

var isWindows = OperatingSystem.IsWindows();
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

var exitCode = 0;

if (options.Mode is "all" or "bench")
{
    RunBenchmarks(options.WarmupRounds, options.IterationCount);
}

if (options.Mode is "all" or "test")
{
    Console.WriteLine();
    var summary = RunComprehensiveTests();
    if (summary.Failed > 0) exitCode = 1;
}

return exitCode;

// ═══════════════════════════════════════════════════════════════════
//  BENCHMARKS
// ═══════════════════════════════════════════════════════════════════

void RunBenchmarks(int warmup, int iterations)
{
    Console.WriteLine("ShellBenchmark");
    Console.WriteLine($"  Warmup:      {warmup}");
    Console.WriteLine($"  Iterations:  {iterations}");

    // Probe shell availability.
    using var probeRoot = new TempDir();
    var probePolicy = new ProcessToolPolicy(probeRoot.Path);
    if (!RunShellCommandTool.TryCreate(probePolicy, out var probeTool, out var reason))
    {
        Console.WriteLine();
        Console.WriteLine($"  SKIP: {reason}");
        return;
    }

    var shellUsed = GetShellUsed(probeTool!);
    Console.WriteLine($"  Shell:       {shellUsed ?? "unknown"}");
    Console.WriteLine();

    // Each scenario exercises a different execution shape.
    var scenarios = new (string Name, Func<string, string> CommandFactory, int? TimeoutSeconds, string? SubDir,
        int MaxOutputBytes, int MaxOutputLines)[]
    {
        // Minimal command — measures process spawn overhead.
        ("spawn overhead (exit 0)",
            _ => isWindows ? "exit /b 0" : "exit 0",
            null, null, 64 * 1024, 2000),

        // Tiny stdout — single line output.
        ("echo one line",
            _ => isWindows ? "echo hello" : "printf 'hello\\n'",
            null, null, 64 * 1024, 2000),

        // Multiple lines of output.
        ("echo 10 lines",
            _ => isWindows
                ? "for /L %i in (1,1,10) do @echo line-%i"
                : "for i in $(seq 1 10); do printf 'line-%d\\n' $i; done",
            null, null, 64 * 1024, 2000),

        // Mixed stdout + stderr.
        ("mixed stdout/stderr",
            _ => isWindows
                ? "echo stdout-1 & echo stderr-1 1>&2 & echo stdout-2 & echo stderr-2 1>&2"
                : "printf 'stdout-1\\n'; printf 'stderr-1\\n' >&2; printf 'stdout-2\\n'; printf 'stderr-2\\n' >&2",
            null, null, 64 * 1024, 2000),

        // Non-zero exit code (should be normal result, not tool error).
        ("non-zero exit",
            _ => isWindows ? "exit /b 42" : "exit 42",
            null, null, 64 * 1024, 2000),

        // Working directory override.
        ("cwd override",
            _ => isWindows ? "cd" : "pwd",
            null, "sub", 64 * 1024, 2000),

        // File system I/O — list directory contents.
        ("ls/dir listing",
            root => isWindows ? "dir /b" : "ls -la",
            null, null, 64 * 1024, 2000),

        // Moderate output — 100 lines.
        ("100 lines output",
            _ => isWindows
                ? "for /L %i in (1,1,100) do @echo line-%i"
                : "seq 1 100",
            null, null, 64 * 1024, 2000),

        // Large output with truncation — 5000 lines into small buffer.
        ("5000 lines, truncated",
            _ => isWindows
                ? "for /L %i in (1,1,5000) do @echo line-%i"
                : "seq 1 5000",
            null, null, 8 * 1024, 20),

        // Environment variable access.
        ("env var read",
            _ => isWindows ? "echo %TERM%" : "echo $TERM",
            null, null, 64 * 1024, 2000),

        // Pipeline command.
        ("pipe (echo | sort)",
            _ => isWindows
                ? "(echo charlie & echo alpha & echo bravo) | sort"
                : "printf 'charlie\\nalpha\\nbravo\\n' | sort",
            null, null, 64 * 1024, 2000),

        // Command that writes then exits — measures drain behavior.
        ("write + immediate exit",
            _ => isWindows
                ? "echo drained & exit /b 0"
                : "printf 'drained\\n'; exit 0",
            null, null, 64 * 1024, 2000),
    };

    PrintBenchHeader();

    foreach (var s in scenarios)
    {
        BenchScenario(s.Name, s.CommandFactory, s.TimeoutSeconds, s.SubDir,
            s.MaxOutputBytes, s.MaxOutputLines, warmup, iterations);
    }
}

void BenchScenario(
    string name, Func<string, string> commandFactory, int? timeoutSeconds, string? subDir,
    int maxOutputBytes, int maxOutputLines,
    int warmup, int iterations)
{
    using var root = new TempDir();
    var subPath = subDir is not null ? Path.Combine(root.Path, subDir) : null;
    if (subPath is not null) Directory.CreateDirectory(subPath);

    // Seed a few files so listing commands have something to show.
    for (var i = 0; i < 5; i++)
        File.WriteAllText(Path.Combine(root.Path, $"file-{i}.txt"), $"content-{i}\n");

    var policy = new ProcessToolPolicy(root.Path,
        maxOutputBytes: maxOutputBytes, maxOutputLines: maxOutputLines);

    if (!RunShellCommandTool.TryCreate(policy, out var tool, out var reason))
    {
        Console.WriteLine($"  SKIP  {name}: {reason}");
        return;
    }

    var command = commandFactory(root.Path);
    var args = new Dictionary<string, object?> { ["command"] = command };
    if (subDir is not null) args["workingDirectory"] = subDir;
    if (timeoutSeconds.HasValue) args["timeoutSeconds"] = timeoutSeconds.Value;

    var invocation = new ToolInvocation("bench-0", "run_shell_command", args);

    // Warmup
    for (var i = 0; i < warmup; i++)
    {
        tool!.ExecuteAsync(invocation, CancellationToken.None).GetAwaiter().GetResult();
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
        lastResult = tool!.ExecuteAsync(invocation, CancellationToken.None).GetAwaiter().GetResult();
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

    int? exitCode = null;
    var timedOut = false;
    var truncated = false;
    if (lastResult?.Metadata is { } meta)
    {
        if (meta.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number)
            exitCode = ec.GetInt32();
        if (meta.TryGetProperty("timedOut", out var to)) timedOut = to.GetBoolean();
        if (meta.TryGetProperty("truncated", out var tr)) truncated = tr.GetBoolean();
    }

    var isError = lastResult?.IsError == true;
    if (isError)
    {
        Console.WriteLine($"  ERROR in '{name}': {lastResult!.Text[0]}");
        return;
    }

    Console.Write($"  {name,-35} | {(exitCode?.ToString() ?? "?"),4} | ");
    Console.Write($"{(timedOut ? "yes" : "no"),5} | {(truncated ? "yes" : "no"),5} | ");
    Console.Write($"{FormatMicros(mean),7} | {FormatMicros(p50),7} | {FormatMicros(p95),7} | ");
    Console.Write($"{FormatMicros(min),7} | {FormatMicros(max),7} | ");
    Console.WriteLine($"{FormatBytes(allocPerIteration),8}  gc:{gen0Delta}");
}

// ═══════════════════════════════════════════════════════════════════
//  COMPREHENSIVE TESTS
// ═══════════════════════════════════════════════════════════════════

TestSummary RunComprehensiveTests()
{
    Console.WriteLine("RunShellCommandTool Comprehensive Tests");
    Console.WriteLine(new string('=', 60));
    Console.WriteLine();

    var passed = 0;
    var failed = 0;
    var skipped = 0;

    // ── TryCreate / availability ──

    RunTest("TryCreate returns false for missing shell", ref passed, ref failed, ref skipped, () =>
    {
        using var root = new TempDir();
        var policy = new ProcessToolPolicy(root.Path);
        var ok = RunShellCommandTool.TryCreate(
            policy, out var t, out var reason,
            new RunShellCommandToolOptions(Path.Combine(root.Path, "no-such-shell"), ShellKind.Bash));
        Assert(!ok, "Should return false");
        Assert(t is null, "Tool should be null");
        Assert(!string.IsNullOrWhiteSpace(reason), "Should provide reason");
    });

    RunTest("TryCreate succeeds with default shell", ref passed, ref failed, ref skipped, () =>
    {
        using var root = new TempDir();
        var policy = new ProcessToolPolicy(root.Path);
        var ok = RunShellCommandTool.TryCreate(policy, out var t, out _);
        Assert(ok, "Should find a default shell");
        Assert(t is not null, "Tool should be created");
        Assert(t!.Descriptor.Name == "run_shell_command", "Tool name");
    });

    // ── Basic execution ──

    RunTest("Simple command returns output", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo hello" : "printf 'hello\\n'");
        AssertNoError(r);
        AssertText(r, "hello");
        AssertMetaInt(r, "exitCode", 0);
        AssertMetaBool(r, "timedOut", false);
    });

    RunTest("Non-zero exit code is normal result (not error)", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "exit /b 42" : "exit 42");
        Assert(!r.IsError, "Non-zero exit should not be isError");
        AssertMetaInt(r, "exitCode", 42);
    });

    RunTest("Exit code 1 is normal result", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "exit /b 1" : "exit 1");
        Assert(!r.IsError, "Exit 1 should not be isError");
        AssertMetaInt(r, "exitCode", 1);
    });

    RunTest("Combined stdout and stderr captured", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows
            ? "echo out-line & echo err-line 1>&2"
            : "printf 'out-line\\n'; printf 'err-line\\n' >&2";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertText(r, "out-line");
        AssertText(r, "err-line");
    });

    RunTest("Empty command returns error", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, "");
        Assert(r.IsError, "Empty command should error");
        AssertText(r, "command");
    });

    RunTest("Whitespace-only command returns error", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, "   ");
        Assert(r.IsError, "Whitespace command should error");
    });

    // ── Working directory ──

    RunTest("Default working directory is root", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "cd" : "pwd");
        AssertNoError(r);
        AssertText(r, env.Dir);
        AssertMetaString(r, "workingDirectory", ".");
    });

    RunTest("Working directory override within root", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        Directory.CreateDirectory(Path.Combine(env.Dir, "child"));
        var r = Shell(env.Tool, isWindows ? "cd" : "pwd", workingDirectory: "child");
        AssertNoError(r);
        AssertText(r, Path.Combine(env.Dir, "child"));
        AssertMetaString(r, "workingDirectory", "child");
    });

    RunTest("Nested working directory", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        Directory.CreateDirectory(Path.Combine(env.Dir, "a", "b"));
        var r = Shell(env.Tool, isWindows ? "cd" : "pwd", workingDirectory: "a/b");
        AssertNoError(r);
        AssertText(r, Path.Combine(env.Dir, "a", "b"));
    });

    RunTest("Working directory outside root is rejected", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, "echo test", workingDirectory: "../outside");
        Assert(r.IsError, "Escaped path should error");
        AssertText(r, "outside the configured root");
    });

    RunTest("Working directory absolute path outside root is rejected", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, "echo test", workingDirectory: "/tmp");
        Assert(r.IsError, "Absolute path outside root should error");
    });

    RunTest("Non-existent working directory is rejected", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, "echo test", workingDirectory: "no-such-dir");
        Assert(r.IsError, "Missing directory should error");
        AssertText(r, "does not exist");
    });

    RunTest("File path as working directory is rejected", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        File.WriteAllText(Path.Combine(env.Dir, "not-a-dir.txt"), "content");
        var r = Shell(env.Tool, "echo test", workingDirectory: "not-a-dir.txt");
        Assert(r.IsError, "File path should error");
        AssertText(r, "not a directory");
    });

    // ── Timeout ──

    RunTest("Timeout produces timed-out result", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(defaultTimeoutSeconds: 1, maxTimeoutSeconds: 1);
        var command = isWindows
            ? "echo start & ping -n 4 127.0.0.1 > nul"
            : "printf 'start\\n'; sleep 5";
        var r = Shell(env.Tool, command);
        Assert(r.IsError, "Timed-out command should be isError");
        AssertText(r, "timed out");
        AssertText(r, "start");
        AssertMetaBool(r, "timedOut", true);
    });

    RunTest("Timeout argument clamped to policy max", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(defaultTimeoutSeconds: 5, maxTimeoutSeconds: 10);
        // Request timeout of 999 — should be clamped to 10.
        var r = Shell(env.Tool, isWindows ? "echo ok" : "printf 'ok\\n'", timeoutSeconds: 999);
        AssertNoError(r);
        AssertMetaInt(r, "timeoutSeconds", 10);
    });

    RunTest("Zero timeout is rejected", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, "echo test", timeoutSeconds: 0);
        Assert(r.IsError, "Zero timeout should error");
        AssertText(r, "timeoutSeconds");
    });

    RunTest("Negative timeout is rejected", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, "echo test", timeoutSeconds: -1);
        Assert(r.IsError, "Negative timeout should error");
    });

    // ── Output truncation (head + tail) ──

    RunTest("Output within limits is not truncated", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(maxOutputLines: 100);
        var command = isWindows
            ? "for /L %i in (1,1,5) do @echo line-%i"
            : "for i in 1 2 3 4 5; do printf 'line-%d\\n' $i; done";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertMetaBool(r, "truncated", false);
        for (var i = 1; i <= 5; i++)
            AssertText(r, $"line-{i}");
    });

    RunTest("Head+tail truncation preserves first and last lines", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(maxOutputLines: 6, maxOutputBytes: 64 * 1024);
        var command = isWindows
            ? "@echo head-1 & @echo head-2 & @echo head-3 & @echo mid-4 & @echo mid-5 & @echo mid-6 & @echo mid-7 & @echo mid-8 & @echo tail-9 & @echo tail-10"
            : "printf '%s\\n' head-1 head-2 head-3 mid-4 mid-5 mid-6 mid-7 mid-8 tail-9 tail-10";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertMetaBool(r, "truncated", true);
        // Head (first ~3 lines with 50/50 split on 6 line limit)
        AssertText(r, "head-1");
        AssertText(r, "head-2");
        AssertText(r, "head-3");
        // Tail (last ~3 lines)
        AssertText(r, "tail-9");
        AssertText(r, "tail-10");
        // Truncation marker
        AssertText(r, "truncated");
    });

    RunTest("Byte-limit truncation", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(maxOutputBytes: 128, maxOutputLines: 10000);
        // Generate enough output to exceed 128 bytes.
        var command = isWindows
            ? "for /L %i in (1,1,50) do @echo line-number-%i-padding-data"
            : "for i in $(seq 1 50); do printf 'line-number-%d-padding-data\\n' $i; done";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertMetaBool(r, "truncatedByBytes", true);
    });

    RunTest("Line-limit truncation", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(maxOutputLines: 4, maxOutputBytes: 64 * 1024);
        var command = isWindows
            ? "@echo L1 & @echo L2 & @echo L3 & @echo L4 & @echo L5 & @echo L6 & @echo L7 & @echo L8"
            : "printf '%s\\n' L1 L2 L3 L4 L5 L6 L7 L8";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertMetaBool(r, "truncatedByLines", true);
    });

    // ── Environment defaults ──

    RunTest("TERM is set to dumb", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo %TERM%" : "echo $TERM");
        AssertNoError(r);
        AssertText(r, "dumb");
    });

    RunTest("NO_COLOR is set", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo %NO_COLOR%" : "echo $NO_COLOR");
        AssertNoError(r);
        AssertText(r, "1");
    });

    RunTest("GIT_PAGER is set to cat", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo %GIT_PAGER%" : "echo $GIT_PAGER");
        AssertNoError(r);
        AssertText(r, "cat");
    });

    // ── Stdin is closed ──

    RunTest("Stdin-reading command does not hang", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(defaultTimeoutSeconds: 5, maxTimeoutSeconds: 5);
        // `cat` with no arguments reads stdin — should get EOF immediately.
        var command = isWindows
            ? "echo pre & (set /p dummy=) 2>nul & echo post"
            : "printf 'pre\\n'; cat; printf 'post\\n'";
        var r = Shell(env.Tool, command);
        // Should complete without timing out because stdin is closed.
        AssertMetaBool(r, "timedOut", false);
        AssertText(r, "pre");
    });

    // ── File I/O through shell ──

    RunTest("Can create and read files through shell", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows
            ? "echo test-content > out.txt & type out.txt"
            : "printf 'test-content\\n' > out.txt; cat out.txt";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertText(r, "test-content");
        Assert(File.Exists(Path.Combine(env.Dir, "out.txt")), "File should be created");
    });

    RunTest("Shell can see files in root directory", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        File.WriteAllText(Path.Combine(env.Dir, "existing.txt"), "preexisting\n");
        var command = isWindows ? "type existing.txt" : "cat existing.txt";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertText(r, "preexisting");
    });

    // ── Pipe and compound commands ──

    RunTest("Pipe command works", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows
            ? "(echo charlie & echo alpha & echo bravo) | sort"
            : "printf 'charlie\\nalpha\\nbravo\\n' | sort";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        var text = r.Text[0];
        var alphaIdx = text.IndexOf("alpha", StringComparison.Ordinal);
        var bravoIdx = text.IndexOf("bravo", StringComparison.Ordinal);
        var charlieIdx = text.IndexOf("charlie", StringComparison.Ordinal);
        Assert(alphaIdx >= 0 && bravoIdx >= 0 && charlieIdx >= 0, "All lines present");
        Assert(alphaIdx < bravoIdx && bravoIdx < charlieIdx, "Should be sorted");
    });

    RunTest("Chained commands with &&", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows
            ? "echo first & echo second"
            : "printf 'first\\n' && printf 'second\\n'";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertText(r, "first");
        AssertText(r, "second");
    });

    RunTest("Failed command in chain stops execution (&&)", ref passed, ref failed, ref skipped, () =>
    {
        if (isWindows)
        {
            Skip("cmd.exe & is unconditional");
            return;
        }

        using var env = CreateEnv();
        var r = Shell(env.Tool, "printf 'before\\n' && false && printf 'after\\n'");
        Assert(!r.IsError, "Should return normally");
        AssertText(r, "before");
        AssertTextAbsent(r, "after");
        AssertMetaInt(r, "exitCode", 1);
    });

    // ── Metadata completeness ──

    RunTest("Metadata includes all expected fields", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo meta-test" : "printf 'meta-test\\n'");
        AssertNoError(r);
        var meta = r.Metadata!.Value;
        var expectedFields = new[]
        {
            "command", "workingDirectory", "timeoutSeconds", "exitCode",
            "durationMs", "timedOut", "truncated", "truncatedByLines",
            "truncatedByBytes", "outputLineCount", "stdoutBytes",
            "stderrBytes", "shellUsed"
        };
        foreach (var field in expectedFields)
        {
            Assert(meta.TryGetProperty(field, out _), $"Missing metadata field: {field}");
        }
    });

    RunTest("durationMs is positive", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo timing" : "printf 'timing\\n'");
        AssertNoError(r);
        var duration = r.Metadata!.Value.GetProperty("durationMs").GetInt64();
        Assert(duration >= 1, $"durationMs should be >= 1, got {duration}");
    });

    RunTest("shellUsed is an absolute path", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo shell" : "printf 'shell\\n'");
        AssertNoError(r);
        var shell = r.Metadata!.Value.GetProperty("shellUsed").GetString()!;
        Assert(Path.IsPathRooted(shell), $"shellUsed should be absolute, got '{shell}'");
    });

    RunTest("stdoutBytes and stderrBytes are tracked", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows
            ? "echo stdout-data & echo stderr-data 1>&2"
            : "printf 'stdout-data\\n'; printf 'stderr-data\\n' >&2";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        var stdoutBytes = r.Metadata!.Value.GetProperty("stdoutBytes").GetInt64();
        var stderrBytes = r.Metadata!.Value.GetProperty("stderrBytes").GetInt64();
        Assert(stdoutBytes > 0, $"stdoutBytes should be > 0, got {stdoutBytes}");
        Assert(stderrBytes > 0, $"stderrBytes should be > 0, got {stderrBytes}");
    });

    // ── Descriptor ──

    RunTest("Descriptor has correct schema", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var desc = env.Tool.Descriptor;
        Assert(desc.Name == "run_shell_command", $"Name should be run_shell_command, got {desc.Name}");
        Assert(!string.IsNullOrWhiteSpace(desc.Description), "Should have description");

        var schema = desc.InputSchema;
        Assert(schema.TryGetProperty("properties", out var props), "Should have properties");
        Assert(props.TryGetProperty("command", out _), "Should have command property");
        Assert(props.TryGetProperty("workingDirectory", out _), "Should have workingDirectory property");
        Assert(props.TryGetProperty("timeoutSeconds", out _), "Should have timeoutSeconds property");

        Assert(schema.TryGetProperty("required", out var req), "Should have required");
        var requiredFields = req.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert(requiredFields.Contains("command"), "command should be required");
    });

    // ── ProcessToolPolicy validation ──

    RunTest("ProcessToolPolicy rejects empty root", ref passed, ref failed, ref skipped, () =>
    {
        try
        {
            _ = new ProcessToolPolicy("");
            Assert(false, "Should throw");
        }
        catch (ArgumentException)
        {
            // expected
        }
    });

    RunTest("ProcessToolPolicy rejects default > max timeout", ref passed, ref failed, ref skipped, () =>
    {
        using var root = new TempDir();
        try
        {
            _ = new ProcessToolPolicy(root.Path, defaultTimeoutSeconds: 300, maxTimeoutSeconds: 60);
            Assert(false, "Should throw");
        }
        catch (ArgumentOutOfRangeException)
        {
            // expected
        }
    });

    RunTest("ProcessToolPolicy rejects zero maxOutputLines", ref passed, ref failed, ref skipped, () =>
    {
        using var root = new TempDir();
        try
        {
            _ = new ProcessToolPolicy(root.Path, maxOutputLines: 0);
            Assert(false, "Should throw");
        }
        catch (ArgumentOutOfRangeException)
        {
            // expected
        }
    });

    RunTest("ProcessToolPolicy rejects zero maxConcurrentProcesses", ref passed, ref failed, ref skipped, () =>
    {
        using var root = new TempDir();
        try
        {
            _ = new ProcessToolPolicy(root.Path, maxConcurrentProcesses: 0);
            Assert(false, "Should throw");
        }
        catch (ArgumentOutOfRangeException)
        {
            // expected
        }
    });

    // ── Concurrency ──

    RunTest("Multiple commands can run concurrently up to policy limit", ref passed, ref failed, ref skipped, () =>
    {
        const double expectedConcurrencyGainMilliseconds = 120;
        var command = isWindows
            ? "echo concurrent & ping -n 2 127.0.0.1 > nul"
            : "printf 'concurrent\\n'; sleep 0.25";

        using var serializedEnv = CreateEnv(maxConcurrentProcesses: 1, defaultTimeoutSeconds: 10, maxTimeoutSeconds: 10);
        var serializedElapsed = MeasureConcurrentRun(serializedEnv.Tool, command, 2, out var serializedResults);
        foreach (var r in serializedResults)
        {
            AssertNoError(r);
            AssertText(r, "concurrent");
        }

        using var concurrentEnv = CreateEnv(maxConcurrentProcesses: 2, defaultTimeoutSeconds: 10, maxTimeoutSeconds: 10);
        var concurrentElapsed = MeasureConcurrentRun(concurrentEnv.Tool, command, 2, out var concurrentResults);
        foreach (var r in concurrentResults)
        {
            AssertNoError(r);
            AssertText(r, "concurrent");
        }

        Assert(
            serializedElapsed - concurrentElapsed >= expectedConcurrencyGainMilliseconds,
            $"Expected maxConcurrentProcesses=2 to beat serialized execution by at least {expectedConcurrencyGainMilliseconds}ms, but serialized={serializedElapsed:F1}ms concurrent={concurrentElapsed:F1}ms");
    });

    // ── Text formatting ──

    RunTest("Output includes exitCode in text", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "exit /b 5" : "exit 5");
        Assert(!r.IsError, "Non-zero exit is not an error");
        AssertText(r, "exitCode: 5");
    });

    RunTest("Output includes workingDirectory in text", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var r = Shell(env.Tool, isWindows ? "echo ok" : "printf 'ok\\n'");
        AssertNoError(r);
        AssertText(r, "workingDirectory:");
    });

    RunTest("Timed-out result includes 'Command timed out' in text", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv(defaultTimeoutSeconds: 1, maxTimeoutSeconds: 1);
        var command = isWindows
            ? "ping -n 4 127.0.0.1 > nul"
            : "sleep 5";
        var r = Shell(env.Tool, command);
        Assert(r.IsError, "Timed-out should be isError");
        AssertText(r, "Command timed out");
    });

    RunTest("No output command shows (no output)", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows ? "exit /b 0" : "exit 0";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertText(r, "(no output)");
    });

    // ── Special characters ──

    RunTest("Command with single quotes", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows
            ? "echo it's alive"
            : "printf \"it's alive\\n\"";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertText(r, "it's alive");
    });

    RunTest("Command with spaces in output", ref passed, ref failed, ref skipped, () =>
    {
        using var env = CreateEnv();
        var command = isWindows
            ? "echo hello   world   spaces"
            : "printf 'hello   world   spaces\\n'";
        var r = Shell(env.Tool, command);
        AssertNoError(r);
        AssertText(r, "hello   world   spaces");
    });

    // ── Summary ──

    Console.WriteLine();
    Console.WriteLine($"Results: {passed} passed, {failed} failed, {skipped} skipped, {passed + failed + skipped} total");
    return new TestSummary(passed, failed, skipped);
}

// ═══════════════════════════════════════════════════════════════════
//  HELPERS
// ═══════════════════════════════════════════════════════════════════

TestEnv CreateEnv(
    int defaultTimeoutSeconds = 120,
    int maxTimeoutSeconds = 300,
    int maxOutputBytes = 64 * 1024,
    int maxOutputLines = 2000,
    int maxConcurrentProcesses = 4)
{
    var dir = Path.Combine(Path.GetTempPath(), "mcp-shell-bench", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);

    var policy = new ProcessToolPolicy(dir,
        defaultTimeoutSeconds: defaultTimeoutSeconds,
        maxTimeoutSeconds: maxTimeoutSeconds,
        maxOutputBytes: maxOutputBytes,
        maxOutputLines: maxOutputLines,
        maxConcurrentProcesses: maxConcurrentProcesses);

    if (!RunShellCommandTool.TryCreate(policy, out var tool, out var reason))
    {
        throw new InvalidOperationException($"Cannot create RunShellCommandTool for tests: {reason}");
    }

    return new TestEnv(dir, policy, tool!);
}

ToolInvocationResult Shell(
    RunShellCommandTool tool, string command,
    string? workingDirectory = null, int? timeoutSeconds = null)
{
    var args = new Dictionary<string, object?> { ["command"] = command };
    if (workingDirectory is not null) args["workingDirectory"] = workingDirectory;
    if (timeoutSeconds.HasValue) args["timeoutSeconds"] = timeoutSeconds.Value;

    return tool.ExecuteAsync(
        new ToolInvocation("test", "run_shell_command", args),
        CancellationToken.None
    ).GetAwaiter().GetResult();
}

double MeasureConcurrentRun(
    RunShellCommandTool tool,
    string command,
    int taskCount,
    out ToolInvocationResult[] results)
{
    var stopwatch = Stopwatch.StartNew();
    var tasks = Enumerable.Range(0, taskCount).Select(i =>
    {
        var invocation = new ToolInvocation(
            $"conc-{i}",
            "run_shell_command",
            new Dictionary<string, object?> { ["command"] = command }
        );
        return tool.ExecuteAsync(invocation, CancellationToken.None);
    }).ToArray();

    results = Task.WhenAll(tasks).GetAwaiter().GetResult();
    stopwatch.Stop();
    return stopwatch.Elapsed.TotalMilliseconds;
}

string? GetShellUsed(RunShellCommandTool tool)
{
    try
    {
        using var tmp = new TempDir();
        var policy = new ProcessToolPolicy(tmp.Path);
        var r = Shell(tool, isWindows ? "exit /b 0" : "exit 0");
        return r.Metadata?.GetProperty("shellUsed").GetString();
    }
    catch
    {
        return null;
    }
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

static void AssertText(ToolInvocationResult r, string expected)
{
    var text = string.Join('\n', r.Text);
    Assert(text.Contains(expected, StringComparison.Ordinal),
        $"Expected text to contain '{expected}', got:\n{Truncate(text, 500)}");
}

static void AssertTextAbsent(ToolInvocationResult r, string unexpected)
{
    var text = string.Join('\n', r.Text);
    Assert(!text.Contains(unexpected, StringComparison.Ordinal),
        $"Expected text NOT to contain '{unexpected}'");
}

static void AssertMetaInt(ToolInvocationResult r, string key, int expected)
{
    var meta = r.Metadata ?? throw new Exception($"No metadata to check '{key}'");
    if (!meta.TryGetProperty(key, out var val))
        throw new Exception($"Metadata missing key '{key}'");
    var actual = val.GetInt32();
    Assert(actual == expected, $"Metadata '{key}': expected {expected}, got {actual}");
}

static void AssertMetaBool(ToolInvocationResult r, string key, bool expected)
{
    var meta = r.Metadata ?? throw new Exception($"No metadata to check '{key}'");
    if (!meta.TryGetProperty(key, out var val))
        throw new Exception($"Metadata missing key '{key}'");
    var actual = val.GetBoolean();
    Assert(actual == expected, $"Metadata '{key}': expected {expected}, got {actual}");
}

static void AssertMetaString(ToolInvocationResult r, string key, string expected)
{
    var meta = r.Metadata ?? throw new Exception($"No metadata to check '{key}'");
    if (!meta.TryGetProperty(key, out var val))
        throw new Exception($"Metadata missing key '{key}'");
    var actual = val.GetString();
    Assert(actual == expected, $"Metadata '{key}': expected '{expected}', got '{actual}'");
}

static void Skip(string message) => throw new SkipException(message);

static string Truncate(string text, int maxLength) =>
    text.Length <= maxLength ? text : text[..maxLength] + "...";

static void PrintBenchHeader()
{
    Console.WriteLine($"  {"Scenario",-35} | {"Exit",4} | {"T/Out",5} | {"Trunc",5} | {"Mean",7} | {"P50",7} | {"P95",7} | {"Min",7} | {"Max",7} | {"Alloc/iter",8}");
    Console.WriteLine($"  {new string('-', 35)}-+-{new string('-', 4)}-+-{new string('-', 5)}-+-{new string('-', 5)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 14)}");
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
        throw new ArgumentException("Percentile requires at least one value.", nameof(sortedTimings));
    if (sortedTimings.Length == 1)
        return sortedTimings[0];

    var position = (sortedTimings.Length - 1) * percentile;
    var lowerIndex = (int)Math.Floor(position);
    var upperIndex = (int)Math.Ceiling(position);

    if (lowerIndex == upperIndex)
        return sortedTimings[lowerIndex];

    var weight = position - lowerIndex;
    return sortedTimings[lowerIndex] + ((sortedTimings[upperIndex] - sortedTimings[lowerIndex]) * weight);
}

static CliOptions ParseArgs(string[] args)
{
    var mode = "all";
    var iterationCount = 10;
    var warmupRounds = 3;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        switch (arg)
        {
            case "--help":
            case "-h":
                return new CliOptions(mode, iterationCount, warmupRounds, true, null);
            case "--iterations":
                if (i == args.Length - 1 || !int.TryParse(args[i + 1], out iterationCount))
                    return new CliOptions(mode, iterationCount, warmupRounds, false,
                        "The '--iterations' option requires an integer value.");
                i++;
                continue;
            case "--warmup":
                if (i == args.Length - 1 || !int.TryParse(args[i + 1], out warmupRounds))
                    return new CliOptions(mode, iterationCount, warmupRounds, false,
                        "The '--warmup' option requires an integer value.");
                i++;
                continue;
        }

        if (arg.StartsWith("--", StringComparison.Ordinal))
            return new CliOptions(mode, iterationCount, warmupRounds, false, $"Unknown option '{arg}'.");

        if (arg is "all" or "bench" or "test")
        {
            mode = arg;
            continue;
        }

        return new CliOptions(mode, iterationCount, warmupRounds, false, $"Unrecognized argument '{arg}'.");
    }

    if (iterationCount <= 0)
        return new CliOptions(mode, iterationCount, warmupRounds, false,
            "The '--iterations' value must be greater than zero.");

    if (warmupRounds < 0)
        return new CliOptions(mode, iterationCount, warmupRounds, false,
            "The '--warmup' value must be zero or greater.");

    return new CliOptions(mode, iterationCount, warmupRounds, false, null);
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project scripts/ShellBenchmark/ShellBenchmark.csproj -- [mode] [options]");
    Console.WriteLine();
    Console.WriteLine("Modes:");
    Console.WriteLine("  all     Run benchmarks and comprehensive tests (default)");
    Console.WriteLine("  bench   Run benchmarks only");
    Console.WriteLine("  test    Run comprehensive tests only");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --iterations <n>   Number of measured iterations per scenario (must be > 0)");
    Console.WriteLine("  --warmup <n>       Number of warmup iterations per scenario (must be >= 0)");
    Console.WriteLine("  --help             Show this help text");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run --project scripts/ShellBenchmark/ShellBenchmark.csproj");
    Console.WriteLine("  dotnet run --project scripts/ShellBenchmark/ShellBenchmark.csproj -- bench --iterations 20");
    Console.WriteLine("  dotnet run --project scripts/ShellBenchmark/ShellBenchmark.csproj -- test");
}

sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "mcp-shell-bench", Guid.NewGuid().ToString("n"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

sealed record TestEnv(string Dir, ProcessToolPolicy Policy, RunShellCommandTool Tool) : IDisposable
{
    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { }
    }
}

sealed class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}

sealed record CliOptions(
    string Mode,
    int IterationCount,
    int WarmupRounds,
    bool ShowHelp,
    string? Error);

sealed record TestSummary(int Passed, int Failed, int Skipped);
