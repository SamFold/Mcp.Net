using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using ToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

// ───────────────────────────────────────────────────────────────────
// EditBenchmark — measures EditFileTool throughput and correctness.
//
// Usage:
//   dotnet run                           # run all benchmarks + tests
//   dotnet run -- bench                  # benchmarks only
//   dotnet run -- test                   # comprehensive tests only
//   dotnet run -- bench --iterations 20  # custom iteration count
// ───────────────────────────────────────────────────────────────────

var mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "all";
var iterationCount = GetIntArg(args, "--iterations", 10);
var warmupRounds = GetIntArg(args, "--warmup", 3);

var tempRoot = Path.Combine(Path.GetTempPath(), "mcp-edit-bench", Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(tempRoot);

try
{
    if (mode is "all" or "bench")
    {
        RunBenchmarks(tempRoot, warmupRounds, iterationCount);
    }

    if (mode is "all" or "test")
    {
        Console.WriteLine();
        RunComprehensiveTests(tempRoot);
    }
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

return 0;

// ═══════════════════════════════════════════════════════════════════
//  BENCHMARKS
// ═══════════════════════════════════════════════════════════════════

void RunBenchmarks(string root, int warmup, int iterations)
{
    Console.WriteLine("EditBenchmark");
    Console.WriteLine($"  Warmup:      {warmup}");
    Console.WriteLine($"  Iterations:  {iterations}");
    Console.WriteLine();

    PrintBenchHeader();

    // ── Small file, single edit ──
    BenchScenario("small file, 1 edit", root, warmup, iterations,
        content: "alpha\nbeta\ngamma\ndelta\n",
        edits: [("beta", "bravo")]);

    // ── Small file, 5 edits ──
    BenchScenario("small file, 5 edits", root, warmup, iterations,
        content: "line-a\nline-b\nline-c\nline-d\nline-e\nline-f\nline-g\n",
        edits: [("line-a", "mod-a"), ("line-b", "mod-b"), ("line-c", "mod-c"),
                ("line-d", "mod-d"), ("line-e", "mod-e")]);

    // ── Small file, 16 edits (max default) ──
    var sb16 = new StringBuilder();
    var edits16 = new List<(string, string)>();
    for (var i = 0; i < 20; i++)
    {
        sb16.AppendLine($"unique-line-{i:D3}");
    }
    for (var i = 0; i < 16; i++)
    {
        edits16.Add(($"unique-line-{i:D3}", $"changed-line-{i:D3}"));
    }
    BenchScenario("small file, 16 edits", root, warmup, iterations,
        content: sb16.ToString(), edits: edits16);

    // ── Medium file (32KB), single edit at middle ──
    var medContent = GenerateFile(32 * 1024, "lf");
    var medLines = medContent.Split('\n');
    var midLine = medLines[medLines.Length / 2];
    BenchScenario("32KB file, 1 edit (mid)", root, warmup, iterations,
        content: medContent, edits: [(midLine, "REPLACED-MID-LINE")]);

    // ── Large file (128KB), single edit at start ──
    var lgContent = GenerateFile(127 * 1024, "lf");
    var firstLine = lgContent.Split('\n')[0];
    BenchScenario("128KB file, 1 edit (start)", root, warmup, iterations,
        content: lgContent, edits: [(firstLine, "REPLACED-FIRST-LINE")]);

    // ── Large file (128KB), single edit at end ──
    var lastLine = lgContent.Split('\n').Where(l => l.Length > 0).Last();
    BenchScenario("128KB file, 1 edit (end)", root, warmup, iterations,
        content: lgContent, edits: [(lastLine, "REPLACED-LAST-LINE")]);

    // ── CRLF file with LF oldText (normalized match) ──
    BenchScenario("CRLF file, normalized match", root, warmup, iterations,
        content: "alpha\r\nbeta\r\ngamma\r\n",
        edits: [("alpha\nbeta\n", "alpha\nbravo\n")]);

    // ── DryRun (no I/O write) ──
    BenchScenario("32KB file, dryRun", root, warmup, iterations,
        content: medContent, edits: [(midLine, "REPLACED-DRY")],
        dryRun: true);

    // ── Hash computation isolation ──
    Console.WriteLine();
    Console.WriteLine("  ── SHA-256 hash computation ──");
    Console.WriteLine();
    PrintBenchHeader();
    foreach (var sizeKb in new[] { 1, 8, 32, 128 })
    {
        BenchHashComputation($"SHA-256 {sizeKb}KB", root, warmup, iterations, sizeKb * 1024);
    }
}

void BenchScenario(
    string name, string root, int warmup, int iterations,
    string content, List<(string Old, string New)> edits,
    bool dryRun = false)
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var filePath = Path.Combine(dir, "target.txt");
    var policy = new FileSystemToolPolicy(dir);
    var readTool = new ReadFileTool(policy);
    var editTool = new EditFileTool(policy);

    // Warmup
    for (var i = 0; i < warmup; i++)
    {
        File.WriteAllText(filePath, content);
        var hash = ReadHash(readTool, "target.txt");
        var inv = BuildEditInvocation(hash, "target.txt", edits, dryRun);
        editTool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
    }

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);

    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Before = GC.CollectionCount(0);
    var timings = new double[iterations];

    for (var i = 0; i < iterations; i++)
    {
        // Reset file to original content each iteration.
        File.WriteAllText(filePath, content);
        var hash = ReadHash(readTool, "target.txt");
        var inv = BuildEditInvocation(hash, "target.txt", edits, dryRun);

        var sw = Stopwatch.StartNew();
        var result = editTool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
        sw.Stop();

        if (result.IsError)
        {
            Console.WriteLine($"  ERROR in '{name}': {result.Text[0]}");
            return;
        }

        timings[i] = sw.Elapsed.TotalMicroseconds;
    }

    var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Delta = GC.CollectionCount(0) - gen0Before;
    var allocPerIter = (allocAfter - allocBefore) / iterations;

    Array.Sort(timings);
    PrintBenchRow(name, timings, allocPerIter, gen0Delta, edits.Count);
}

void BenchHashComputation(string name, string root, int warmup, int iterations, int sizeBytes)
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var filePath = Path.Combine(dir, "hashtest.bin");
    var data = new byte[sizeBytes];
    Random.Shared.NextBytes(data);
    File.WriteAllBytes(filePath, data);

    for (var i = 0; i < warmup; i++)
    {
        using var s = File.OpenRead(filePath);
        SHA256.HashData(s);
    }

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var timings = new double[iterations];

    for (var i = 0; i < iterations; i++)
    {
        var sw = Stopwatch.StartNew();
        using var s = File.OpenRead(filePath);
        SHA256.HashData(s);
        sw.Stop();
        timings[i] = sw.Elapsed.TotalMicroseconds;
    }

    var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
    Array.Sort(timings);
    PrintBenchRow(name, timings, (allocAfter - allocBefore) / iterations, 0, 0);
}

// ═══════════════════════════════════════════════════════════════════
//  COMPREHENSIVE TESTS
// ═══════════════════════════════════════════════════════════════════

void RunComprehensiveTests(string root)
{
    Console.WriteLine("EditFileTool Comprehensive Tests");
    Console.WriteLine(new string('=', 60));
    Console.WriteLine();

    var passed = 0;
    var failed = 0;

    // ── Encoding round-trips ──
    RunTest("UTF-8 no BOM round-trip", ref passed, ref failed, () =>
    {
        var (dir, policy, read, edit) = Setup(root);
        var content = "hello\nworld\n";
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), Encoding.UTF8.GetBytes(content));
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("hello", "hola")]);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "f.txt"));
        Assert(!bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Should not have BOM");
        Assert(Encoding.UTF8.GetString(bytes) == "hola\nworld\n", $"Content mismatch: {Encoding.UTF8.GetString(bytes)}");
    });

    RunTest("UTF-8 BOM preserved", ref passed, ref failed, () =>
    {
        var (dir, policy, read, edit) = Setup(root);
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var textBytes = Encoding.UTF8.GetBytes("hello\nworld\n");
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), [.. bom, .. textBytes]);
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("hello", "hola")]);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "f.txt"));
        Assert(bytes.AsSpan().StartsWith(bom), "BOM should be preserved");
        Assert(Encoding.UTF8.GetString(bytes.AsSpan(3)) == "hola\nworld\n", "Content after BOM");
    });

    RunTest("UTF-16 LE BOM preserved", ref passed, ref failed, () =>
    {
        var (dir, policy, read, edit) = Setup(root);
        var enc = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "hello\nworld\n", enc);
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("hello", "hola")]);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "f.txt"));
        Assert(bytes[0] == 0xFF && bytes[1] == 0xFE, "UTF-16 LE BOM preserved");
    });

    RunTest("UTF-16 BE BOM preserved", ref passed, ref failed, () =>
    {
        var (dir, policy, read, edit) = Setup(root);
        var enc = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "hello\nworld\n", enc);
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("hello", "hola")]);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "f.txt"));
        Assert(bytes[0] == 0xFE && bytes[1] == 0xFF, "UTF-16 BE BOM preserved");
    });

    // ── Line ending preservation ──
    RunTest("LF preserved", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), "aaa\nbbb\nccc\n"u8.ToArray());
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("bbb", "BBB")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "aaa\nBBB\nccc\n", "LF preserved");
    });

    RunTest("CRLF preserved", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), "aaa\r\nbbb\r\nccc\r\n"u8.ToArray());
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("bbb", "BBB")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "aaa\r\nBBB\r\nccc\r\n", "CRLF preserved");
    });

    RunTest("CR preserved", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), "aaa\rbbb\rccc\r"u8.ToArray());
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("bbb", "BBB")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "aaa\rBBB\rccc\r", "CR preserved");
    });

    RunTest("CRLF file with LF oldText (normalized match)", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), "aaa\r\nbbb\r\nccc\r\n"u8.ToArray());
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("aaa\nbbb\n", "aaa\nBBB\n")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "aaa\r\nBBB\r\nccc\r\n", "Normalized + CRLF restored");
        AssertMeta(r, "usedNormalizedLineEndingMatch", true);
    });

    RunTest("CRLF file with LF oldText, exact mode rejects", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), "aaa\r\nbbb\r\n"u8.ToArray());
        var hash = ReadHash(read, "f.txt");
        var r = DoEditWithMode(edit, "f.txt", hash, "aaa\nbbb\n", "XXX", "exact");
        Assert(r.IsError, "Exact mode should reject LF against CRLF");
    });

    // ── Batch edits ──
    RunTest("Multi-edit batch applies all edits", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "aaa\nbbb\nccc\nddd\n");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("aaa", "AAA"), ("ccc", "CCC")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "AAA\nbbb\nCCC\nddd\n", "Both edits applied");
        AssertMetaInt(r, "appliedEditCount", 2);
    });

    RunTest("Overlapping edits rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "aaa bbb ccc");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("aaa bbb", "XXX"), ("bbb ccc", "YYY")]);
        Assert(r.IsError, "Overlapping edits should fail");
        Assert(r.Text[0].Contains("overlap", StringComparison.OrdinalIgnoreCase), "Error mentions overlap");
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "aaa bbb ccc", "File unchanged");
    });

    // ── Deletion (empty newText) ──
    RunTest("Empty newText deletes matched text", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "keep\ndelete-me\nkeep\n");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("delete-me\n", "")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "keep\nkeep\n", "Line deleted");
    });

    // ── Boundary edits ──
    RunTest("Edit first line", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "first\nsecond\nthird\n");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("first", "FIRST")]);
        AssertNoError(r);
        AssertMetaInt(r, "firstChangedLine", 1);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "FIRST\nsecond\nthird\n", "First line edited");
    });

    RunTest("Edit last line", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "first\nsecond\nthird");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("third", "THIRD")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "first\nsecond\nTHIRD", "Last line edited");
    });

    RunTest("Replace entire file content", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "entire content");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("entire content", "brand new content")]);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "brand new content", "Entire replacement");
    });

    // ── Error cases ──
    RunTest("Stale content hash rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "original");
        var r = DoEdit(edit, "f.txt", "sha256:0000000000", [("original", "changed")]);
        Assert(r.IsError, "Stale hash should fail");
        AssertMetaString(r, "reason", "content_hash_mismatch");
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "original", "File unchanged");
    });

    RunTest("Ambiguous match rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "dup\ndup\n");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("dup", "unique")]);
        Assert(r.IsError, "Ambiguous should fail");
        Assert(r.Text[0].Contains("multiple", StringComparison.OrdinalIgnoreCase), "Error mentions multiple");
    });

    RunTest("oldText not found rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "actual content");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("nonexistent", "replacement")]);
        Assert(r.IsError, "Not found should fail");
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "actual content", "File unchanged");
    });

    RunTest("oldText == newText rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "no change");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("no change", "no change")]);
        Assert(r.IsError, "Identical old/new should fail");
    });

    RunTest("Empty edits array rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "content");
        var r = edit.ExecuteAsync(
            new ToolInvocation("t", "edit_file", new Dictionary<string, object?>
            {
                ["path"] = "f.txt",
                ["expectedContentHash"] = "sha256:abc",
                ["edits"] = Array.Empty<object>(),
            }), CancellationToken.None).GetAwaiter().GetResult();
        Assert(r.IsError, "Empty edits should fail");
    });

    RunTest("Missing file rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        var r = DoEdit(edit, "nonexistent.txt", "sha256:abc", [("x", "y")]);
        Assert(r.IsError, "Missing file should fail");
        AssertMetaString(r, "reason", "missing_file");
    });

    RunTest("Directory path rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        Directory.CreateDirectory(Path.Combine(dir, "subdir"));
        var r = DoEdit(edit, "subdir", "sha256:abc", [("x", "y")]);
        Assert(r.IsError, "Directory path should fail");
        AssertMetaString(r, "reason", "path_is_directory");
    });

    RunTest("Path outside root rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        var r = DoEdit(edit, "../escape.txt", "sha256:abc", [("x", "y")]);
        Assert(r.IsError, "Escaped path should fail");
    });

    RunTest("Binary file (null bytes) rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllBytes(Path.Combine(dir, "f.bin"), [0x48, 0x65, 0x00, 0x6C, 0x6F]);
        var hash = ReadHash(read, "f.bin");
        // read_file might succeed (returns text up to null) — but edit should reject
        var hashBytes = SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, "f.bin")));
        var realHash = $"sha256:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
        var r = DoEdit(edit, "f.bin", realHash, [("He", "XX")]);
        Assert(r.IsError, "Binary file should be rejected");
    });

    RunTest("File exceeding maxEditableBytes rejected", ref passed, ref failed, () =>
    {
        var (dir, _, _, _) = Setup(root);
        var tinyPolicy = new FileSystemToolPolicy(dir, maxEditableBytes: 64);
        var tinyRead = new ReadFileTool(tinyPolicy);
        var tinyEdit = new EditFileTool(tinyPolicy);
        File.WriteAllText(Path.Combine(dir, "f.txt"), new string('x', 128));
        var hashBytes = SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, "f.txt")));
        var realHash = $"sha256:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
        var r = DoEdit(tinyEdit, "f.txt", realHash, [("xxx", "yyy")]);
        Assert(r.IsError, "Oversized file should be rejected");
    });

    RunTest("Too many edits rejected", ref passed, ref failed, () =>
    {
        var (dir, _, _, _) = Setup(root);
        var tinyPolicy = new FileSystemToolPolicy(dir, maxEditsPerRequest: 2);
        var tinyEdit = new EditFileTool(tinyPolicy);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "a\nb\nc\n");
        var r = DoEdit(tinyEdit, "f.txt", "sha256:abc",
            [("a", "A"), ("b", "B"), ("c", "C")]);
        Assert(r.IsError, "Too many edits should fail");
        AssertMetaString(r, "reason", "too_many_edits");
    });

    // ── DryRun ──
    RunTest("DryRun does not modify file", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "hello\nworld\n");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("hello", "goodbye")], dryRun: true);
        AssertNoError(r);
        AssertMeta(r, "dryRun", true);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "hello\nworld\n", "File should be unchanged");
    });

    RunTest("DryRun still validates content hash", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "content");
        var r = DoEdit(edit, "f.txt", "sha256:stale", [("content", "new")], dryRun: true);
        Assert(r.IsError, "DryRun should still reject stale hash");
    });

    // ── Metadata completeness ──
    RunTest("Metadata includes all expected fields", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "hello\nworld\n");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("hello", "goodbye")]);
        AssertNoError(r);
        var m = r.Metadata!.Value;
        var expectedFields = new[] { "path", "dryRun", "appliedEditCount", "contentHashBefore",
            "contentHashAfter", "encoding", "bom", "newlineStyle", "firstChangedLine",
            "usedNormalizedLineEndingMatch", "diffPreview", "diffTruncated" };
        foreach (var field in expectedFields)
        {
            Assert(m.TryGetProperty(field, out _), $"Missing metadata field: {field}");
        }
    });

    // ── Diff preview ──
    RunTest("Diff preview contains changed lines", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "aaa\nbbb\nccc\n");
        var hash = ReadHash(read, "f.txt");
        var r = DoEdit(edit, "f.txt", hash, [("bbb", "BBB")]);
        AssertNoError(r);
        var diff = r.Metadata!.Value.GetProperty("diffPreview").GetString()!;
        Assert(diff.Contains("-bbb"), $"Diff should contain removed line, got: {diff}");
        Assert(diff.Contains("+BBB"), $"Diff should contain added line, got: {diff}");
    });

    // ── Concurrent modification detection ──
    RunTest("File modified between read and edit is rejected", ref passed, ref failed, () =>
    {
        var (dir, _, read, edit) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "version-1");
        var hash = ReadHash(read, "f.txt");
        // Simulate external modification after read
        File.WriteAllText(Path.Combine(dir, "f.txt"), "version-2");
        var r = DoEdit(edit, "f.txt", hash, [("version-1", "version-3")]);
        Assert(r.IsError, "Modified file should be rejected");
        AssertMetaString(r, "reason", "content_hash_mismatch");
    });

    // ── read_file metadata for edit workflow ──
    RunTest("read_file returns contentHash for edit workflow", ref passed, ref failed, () =>
    {
        var (dir, _, read, _) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "test content");
        var result = read.ExecuteAsync(
            new ToolInvocation("t", "read_file", new Dictionary<string, object?> { ["path"] = "f.txt" }),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert(!result.IsError, "read_file should succeed");
        var m = result.Metadata!.Value;
        Assert(m.TryGetProperty("contentHash", out var h) && h.GetString()!.StartsWith("sha256:"), "Has contentHash");
        Assert(m.TryGetProperty("encoding", out _), "Has encoding");
        Assert(m.TryGetProperty("bom", out _), "Has bom");
        Assert(m.TryGetProperty("newlineStyle", out _), "Has newlineStyle");
    });

    Console.WriteLine();
    Console.WriteLine($"Results: {passed} passed, {failed} failed, {passed + failed} total");
    if (failed > 0)
    {
        Environment.ExitCode = 1;
    }
}

// ═══════════════════════════════════════════════════════════════════
//  SHARED HELPERS
// ═══════════════════════════════════════════════════════════════════

(string Dir, FileSystemToolPolicy Policy, ReadFileTool Read, EditFileTool Edit) Setup(string root)
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var policy = new FileSystemToolPolicy(dir);
    return (dir, policy, new ReadFileTool(policy), new EditFileTool(policy));
}

string ReadHash(ReadFileTool tool, string path)
{
    var r = tool.ExecuteAsync(
        new ToolInvocation("r", "read_file", new Dictionary<string, object?> { ["path"] = path }),
        CancellationToken.None).GetAwaiter().GetResult();
    if (r.IsError)
    {
        throw new Exception($"read_file failed: {r.Text[0]}");
    }
    return r.Metadata!.Value.GetProperty("contentHash").GetString()!;
}

ToolInvocationResult DoEdit(EditFileTool tool, string path, string hash,
    List<(string Old, string New)> edits, bool dryRun = false)
{
    var inv = BuildEditInvocation(hash, path, edits, dryRun);
    return tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
}

ToolInvocationResult DoEditWithMode(EditFileTool tool, string path, string hash,
    string oldText, string newText, string matchMode)
{
    var inv = new ToolInvocation("e", "edit_file", new Dictionary<string, object?>
    {
        ["path"] = path,
        ["expectedContentHash"] = hash,
        ["edits"] = new object?[]
        {
            new Dictionary<string, object?>
            {
                ["oldText"] = oldText,
                ["newText"] = newText,
                ["matchMode"] = matchMode,
            },
        },
    });
    return tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
}

static ToolInvocation BuildEditInvocation(string hash, string path,
    List<(string Old, string New)> edits, bool dryRun = false)
{
    var editArgs = edits.Select(e => (object?)new Dictionary<string, object?>
    {
        ["oldText"] = e.Old,
        ["newText"] = e.New,
    }).ToArray();

    var args = new Dictionary<string, object?>
    {
        ["path"] = path,
        ["expectedContentHash"] = hash,
        ["edits"] = editArgs,
    };
    if (dryRun)
    {
        args["dryRun"] = true;
    }

    return new ToolInvocation("e", "edit_file", args);
}

static string GenerateFile(int approxBytes, string newline)
{
    var sb = new StringBuilder(approxBytes + 256);
    var lineNum = 0;
    var nl = newline == "crlf" ? "\r\n" : "\n";
    while (sb.Length < approxBytes)
    {
        sb.Append($"line-{lineNum:D6}: The quick brown fox jumps over the lazy dog.{nl}");
        lineNum++;
    }
    return sb.ToString();
}

void RunTest(string name, ref int passed, ref int failed, Action test)
{
    try
    {
        test();
        Console.WriteLine($"  PASS  {name}");
        passed++;
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
    if (!condition)
    {
        throw new Exception(message);
    }
}

static void AssertNoError(ToolInvocationResult r)
{
    if (r.IsError)
    {
        throw new Exception($"Expected success but got error: {r.Text[0]}");
    }
}

static void AssertMeta(ToolInvocationResult r, string key, bool expected)
{
    var actual = r.Metadata!.Value.GetProperty(key).GetBoolean();
    Assert(actual == expected, $"Metadata '{key}': expected {expected}, got {actual}");
}

static void AssertMetaInt(ToolInvocationResult r, string key, int expected)
{
    var actual = r.Metadata!.Value.GetProperty(key).GetInt32();
    Assert(actual == expected, $"Metadata '{key}': expected {expected}, got {actual}");
}

static void AssertMetaString(ToolInvocationResult r, string key, string expected)
{
    if (r.Metadata is null)
    {
        throw new Exception($"No metadata on result to check '{key}'");
    }
    if (!r.Metadata.Value.TryGetProperty(key, out var val))
    {
        throw new Exception($"Metadata missing key '{key}'");
    }
    var actual = val.GetString();
    Assert(actual == expected, $"Metadata '{key}': expected '{expected}', got '{actual}'");
}

static void PrintBenchHeader()
{
    Console.WriteLine($"  {"Scenario",-35} | {"Edits",5} | {"Mean",7} | {"Min",7} | {"Max",7} | {"Alloc/iter",8}");
    Console.WriteLine($"  {new string('-', 35)}-+-{new string('-', 5)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 14)}");
}

static void PrintBenchRow(string name, double[] timings, long allocPerIter, int gen0Delta, int editCount)
{
    var mean = timings.Average();
    var min = timings[0];
    var max = timings[^1];
    Console.Write($"  {name,-35} | {editCount,5} | ");
    Console.Write($"{FormatMicros(mean),7} | {FormatMicros(min),7} | {FormatMicros(max),7} | ");
    Console.WriteLine($"{FormatBytes(allocPerIter),8}  gc:{gen0Delta}");
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

static int GetIntArg(string[] args, string name, int defaultValue)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name && int.TryParse(args[i + 1], out var value))
            return value;
    }
    return defaultValue;
}
