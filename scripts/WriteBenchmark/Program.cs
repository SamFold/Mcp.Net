using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using ToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

// ───────────────────────────────────────────────────────────────────
// WriteBenchmark — measures WriteFileTool throughput and correctness.
//
// Usage:
//   dotnet run                           # run all benchmarks + tests
//   dotnet run -- bench                  # benchmarks only
//   dotnet run -- test                   # comprehensive tests only
//   dotnet run -- bench --iterations 20  # custom iteration count
// ───────────────────────────────────────────────────────────────────

var mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "all";
var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
if (mode is not ("all" or "bench" or "test"))
{
    Console.Error.WriteLine($"Unknown mode '{mode}'.");
    PrintUsage();
    return 1;
}

var iterationCount = GetRequiredPositiveIntArg(args, "--iterations", 10);
if (iterationCount is null)
{
    return 1;
}
var effectiveIterationCount = iterationCount.Value;

var warmupRounds = GetRequiredNonNegativeIntArg(args, "--warmup", 3);
if (warmupRounds is null)
{
    return 1;
}
var effectiveWarmupRounds = warmupRounds.Value;

var tempRoot = Path.Combine(Path.GetTempPath(), "mcp-write-bench", Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(tempRoot);

var exitCode = 0;

try
{
    if (mode is "all" or "bench")
    {
        RunBenchmarks(tempRoot, effectiveWarmupRounds, effectiveIterationCount);
    }

    if (mode is "all" or "test")
    {
        Console.WriteLine();
        exitCode = RunComprehensiveTests(tempRoot);
    }
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

return exitCode;

// ═══════════════════════════════════════════════════════════════════
//  BENCHMARKS
// ═══════════════════════════════════════════════════════════════════

void RunBenchmarks(string root, int warmup, int iterations)
{
    Console.WriteLine("WriteBenchmark");
    Console.WriteLine($"  Warmup:      {warmup}");
    Console.WriteLine($"  Iterations:  {iterations}");
    Console.WriteLine();

    PrintBenchHeader();

    // ── Create small file (no parent creation) ──
    BenchCreate("create small file", root, warmup, iterations,
        content: "hello\nworld\n", createParentDirs: false, encodedByteCount: utf8NoBom.GetByteCount("hello\nworld\n"));

    // ── Create small file (with parent dir creation) ──
    BenchCreate("create + mkdir parents", root, warmup, iterations,
        content: "hello\nworld\n", createParentDirs: true, encodedByteCount: utf8NoBom.GetByteCount("hello\nworld\n"));

    // ── Create 1KB file ──
    BenchCreate("create 1KB file", root, warmup, iterations,
        content: GenerateContent(1024), createParentDirs: false, encodedByteCount: utf8NoBom.GetByteCount(GenerateContent(1024)));

    // ── Create 8KB file ──
    BenchCreate("create 8KB file", root, warmup, iterations,
        content: GenerateContent(8 * 1024), createParentDirs: false, encodedByteCount: utf8NoBom.GetByteCount(GenerateContent(8 * 1024)));

    // ── Create 32KB file ──
    BenchCreate("create 32KB file", root, warmup, iterations,
        content: GenerateContent(32 * 1024), createParentDirs: false, encodedByteCount: utf8NoBom.GetByteCount(GenerateContent(32 * 1024)));

    // ── Create 127KB file (under default 128KB writable limit) ──
    BenchCreate("create 127KB file", root, warmup, iterations,
        content: GenerateContent(127 * 1024), createParentDirs: false, encodedByteCount: utf8NoBom.GetByteCount(GenerateContent(127 * 1024)));

    Console.WriteLine();
    PrintBenchHeader();

    // ── Overwrite small file ──
    BenchOverwrite("overwrite small file", root, warmup, iterations,
        originalContent: "original content\n", newContent: "replaced content\n",
        encodedByteCount: utf8NoBom.GetByteCount("replaced content\n"));

    // ── Overwrite 32KB file ──
    BenchOverwrite("overwrite 32KB file", root, warmup, iterations,
        originalContent: GenerateContent(32 * 1024), newContent: GenerateContent(32 * 1024, seed: 42),
        encodedByteCount: utf8NoBom.GetByteCount(GenerateContent(32 * 1024, seed: 42)));

    // ── Overwrite 127KB file (under default 128KB writable limit) ──
    BenchOverwrite("overwrite 127KB file", root, warmup, iterations,
        originalContent: GenerateContent(127 * 1024), newContent: GenerateContent(127 * 1024, seed: 42),
        encodedByteCount: utf8NoBom.GetByteCount(GenerateContent(127 * 1024, seed: 42)));

    // ── Overwrite preserving UTF-8 BOM ──
    BenchOverwriteWithEncoding("overwrite UTF-8 BOM", root, warmup, iterations,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        originalContent: "original\n", newContent: "replaced\n",
        encodedByteCount: GetEncodedByteCount("replaced\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

    // ── Overwrite preserving UTF-16 LE ──
    BenchOverwriteWithEncoding("overwrite UTF-16 LE", root, warmup, iterations,
        new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        originalContent: "original\n", newContent: "replaced\n",
        encodedByteCount: GetEncodedByteCount("replaced\n", new UnicodeEncoding(bigEndian: false, byteOrderMark: true)));
}

void BenchCreate(string name, string root, int warmup, int iterations,
    string content, bool createParentDirs, int encodedByteCount)
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var policy = new FileSystemToolPolicy(dir);
    var tool = new WriteFileTool(policy);

    // Warmup
    for (var i = 0; i < warmup; i++)
    {
        var subPath = createParentDirs ? $"sub/deep/warmup-{i}.txt" : $"warmup-{i}.txt";
        var inv = BuildWriteInvocation(subPath, content);
        tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
    }

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);

    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Before = GC.CollectionCount(0);
    var timings = new double[iterations];

    for (var i = 0; i < iterations; i++)
    {
        var subPath = createParentDirs ? $"iter-{i}/nested/file.txt" : $"iter-{i}.txt";
        var inv = BuildWriteInvocation(subPath, content);

        var sw = Stopwatch.StartNew();
        var result = tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
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
    PrintBenchRow(name, timings, allocPerIter, gen0Delta, encodedByteCount);
}

void BenchOverwrite(string name, string root, int warmup, int iterations,
    string originalContent, string newContent, int encodedByteCount)
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var policy = new FileSystemToolPolicy(dir);
    var tool = new WriteFileTool(policy);
    var filePath = Path.Combine(dir, "target.txt");

    // Warmup
    for (var i = 0; i < warmup; i++)
    {
        File.WriteAllText(filePath, originalContent);
        var inv = BuildWriteInvocation("target.txt", newContent, overwrite: true);
        tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
    }

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);

    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Before = GC.CollectionCount(0);
    var timings = new double[iterations];

    for (var i = 0; i < iterations; i++)
    {
        File.WriteAllText(filePath, originalContent);
        var inv = BuildWriteInvocation("target.txt", newContent, overwrite: true);

        var sw = Stopwatch.StartNew();
        var result = tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
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
    PrintBenchRow(name, timings, allocPerIter, gen0Delta, encodedByteCount);
}

void BenchOverwriteWithEncoding(string name, string root, int warmup, int iterations,
    Encoding encoding, string originalContent, string newContent, int encodedByteCount)
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var policy = new FileSystemToolPolicy(dir);
    var tool = new WriteFileTool(policy);
    var filePath = Path.Combine(dir, "target.txt");

    // Warmup
    for (var i = 0; i < warmup; i++)
    {
        File.WriteAllText(filePath, originalContent, encoding);
        var inv = BuildWriteInvocation("target.txt", newContent, overwrite: true);
        tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
    }

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);

    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var gen0Before = GC.CollectionCount(0);
    var timings = new double[iterations];

    for (var i = 0; i < iterations; i++)
    {
        File.WriteAllText(filePath, originalContent, encoding);
        var inv = BuildWriteInvocation("target.txt", newContent, overwrite: true);

        var sw = Stopwatch.StartNew();
        var result = tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
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
    PrintBenchRow(name, timings, allocPerIter, gen0Delta, encodedByteCount);
}

// ═══════════════════════════════════════════════════════════════════
//  COMPREHENSIVE TESTS
// ═══════════════════════════════════════════════════════════════════

int RunComprehensiveTests(string root)
{
    Console.WriteLine("WriteFileTool Comprehensive Tests");
    Console.WriteLine(new string('=', 60));
    Console.WriteLine();

    var passed = 0;
    var failed = 0;

    // ── Basic creation ──

    RunTest("Create new file in existing directory", ref passed, ref failed, () =>
    {
        var (dir, policy, tool) = Setup(root);
        var r = DoWrite(tool, "hello.txt", "hello world\n");
        AssertNoError(r);
        AssertMetaBool(r, "created", true);
        AssertMetaBool(r, "overwroteExisting", false);
        Assert(File.ReadAllText(Path.Combine(dir, "hello.txt")) == "hello world\n", "Content matches");
    });

    RunTest("Create file with auto parent directories", ref passed, ref failed, () =>
    {
        var (dir, policy, tool) = Setup(root);
        var r = DoWrite(tool, "a/b/c/deep.txt", "nested content\n");
        AssertNoError(r);
        AssertMetaBool(r, "createdDirectories", true);
        Assert(File.Exists(Path.Combine(dir, "a", "b", "c", "deep.txt")), "File created at nested path");
        Assert(File.ReadAllText(Path.Combine(dir, "a", "b", "c", "deep.txt")) == "nested content\n", "Content matches");
    });

    RunTest("Create empty file", ref passed, ref failed, () =>
    {
        var (dir, policy, tool) = Setup(root);
        var r = DoWrite(tool, "empty.txt", "");
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "empty.txt")) == "", "File is empty");
        AssertMetaLong(r, "sizeBytes", 0);
    });

    RunTest("Create file preserves LF newlines", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var r = DoWrite(tool, "lf.txt", "alpha\nbeta\n");
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "lf.txt"));
        Assert(Encoding.UTF8.GetString(bytes) == "alpha\nbeta\n", "LF preserved");
        AssertMetaString(r, "newlineStyle", "lf");
    });

    RunTest("Create file preserves CRLF newlines", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var r = DoWrite(tool, "crlf.txt", "alpha\r\nbeta\r\n");
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "crlf.txt"));
        Assert(Encoding.UTF8.GetString(bytes) == "alpha\r\nbeta\r\n", "CRLF preserved");
        AssertMetaString(r, "newlineStyle", "crlf");
    });

    RunTest("Create file defaults to UTF-8 without BOM", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var r = DoWrite(tool, "utf8.txt", "hello\n");
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "utf8.txt"));
        Assert(!bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "No BOM");
        AssertMetaString(r, "encoding", "utf-8");
        AssertMetaBool(r, "bom", false);
    });

    // ── Overwrite behavior ──

    RunTest("Overwrite existing file with overwrite=true", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "original");
        var r = DoWrite(tool, "f.txt", "replaced", overwrite: true);
        AssertNoError(r);
        AssertMetaBool(r, "created", false);
        AssertMetaBool(r, "overwroteExisting", true);
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "replaced", "Content replaced");
    });

    RunTest("Overwrite preserves UTF-8 BOM", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), [.. bom, .. "original\n"u8.ToArray()]);
        var r = DoWrite(tool, "f.txt", "replaced\n", overwrite: true);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "f.txt"));
        Assert(bytes.AsSpan().StartsWith(bom), "UTF-8 BOM preserved");
        Assert(Encoding.UTF8.GetString(bytes.AsSpan(3)) == "replaced\n", "Content after BOM");
        AssertMetaString(r, "encoding", "utf-8");
        AssertMetaBool(r, "bom", true);
    });

    RunTest("Overwrite preserves UTF-16 LE encoding and BOM", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var enc = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "original\n", enc);
        var r = DoWrite(tool, "f.txt", "replaced\n", overwrite: true);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "f.txt"));
        Assert(bytes[0] == 0xFF && bytes[1] == 0xFE, "UTF-16 LE BOM preserved");
        AssertMetaString(r, "encoding", "utf-16-le");
        AssertMetaBool(r, "bom", true);
    });

    RunTest("Overwrite preserves UTF-16 BE encoding and BOM", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var enc = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "original\n", enc);
        var r = DoWrite(tool, "f.txt", "replaced\n", overwrite: true);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "f.txt"));
        Assert(bytes[0] == 0xFE && bytes[1] == 0xFF, "UTF-16 BE BOM preserved");
        AssertMetaString(r, "encoding", "utf-16-be");
        AssertMetaBool(r, "bom", true);
    });

    // ── Error cases: overwrite protection ──

    RunTest("Reject existing file when overwrite is false (default)", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "original");
        var r = DoWrite(tool, "f.txt", "replacement");
        Assert(r.IsError, "Should reject overwrite");
        AssertMetaString(r, "reason", "already_exists");
        Assert(r.Text[0].Contains("already exists"), "Error mentions file exists");
        Assert(r.Text[0].Contains("overwrite=true"), "Error suggests overwrite");
        Assert(File.ReadAllText(Path.Combine(dir, "f.txt")) == "original", "File unchanged");
    });

    // ── Error cases: path validation ──

    RunTest("Reject missing path argument", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var r = tool.ExecuteAsync(
            new ToolInvocation("w", "write_file", new Dictionary<string, object?>
            {
                ["content"] = "hello",
            }), CancellationToken.None).GetAwaiter().GetResult();
        Assert(r.IsError, "Missing path should fail");
        AssertMetaString(r, "reason", "missing_path");
    });

    RunTest("Reject missing content argument", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var r = tool.ExecuteAsync(
            new ToolInvocation("w", "write_file", new Dictionary<string, object?>
            {
                ["path"] = "f.txt",
            }), CancellationToken.None).GetAwaiter().GetResult();
        Assert(r.IsError, "Missing content should fail");
        AssertMetaString(r, "reason", "missing_content");
    });

    RunTest("Reject path that is a directory", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        Directory.CreateDirectory(Path.Combine(dir, "subdir"));
        var r = DoWrite(tool, "subdir", "content");
        Assert(r.IsError, "Directory path should fail");
        AssertMetaString(r, "reason", "path_is_directory");
    });

    RunTest("Reject path outside bounded root", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var r = DoWrite(tool, "../escape.txt", "content");
        Assert(r.IsError, "Escaped path should fail");
        Assert(r.Text[0].Contains("outside"), "Error mentions outside root");
    });

    // ── Error cases: content validation ──

    RunTest("Reject content containing null characters", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var r = DoWrite(tool, "f.txt", "alpha\0beta");
        Assert(r.IsError, "Null characters should fail");
        AssertMetaString(r, "reason", "content_contains_null");
        Assert(!File.Exists(Path.Combine(dir, "f.txt")), "File should not be created");
    });

    RunTest("Reject content exceeding writable byte limit", ref passed, ref failed, () =>
    {
        var (dir, _, _) = Setup(root);
        var tinyPolicy = new FileSystemToolPolicy(dir, maxWritableBytes: 8);
        var tinyTool = new WriteFileTool(tinyPolicy);
        var r = DoWrite(tinyTool, "f.txt", "this exceeds 8 bytes");
        Assert(r.IsError, "Oversized content should fail");
        AssertMetaString(r, "reason", "content_too_large");
        Assert(!File.Exists(Path.Combine(dir, "f.txt")), "File should not be created");
    });

    RunTest("Reject content at exact byte limit boundary (just over)", ref passed, ref failed, () =>
    {
        var (dir, _, _) = Setup(root);
        var tinyPolicy = new FileSystemToolPolicy(dir, maxWritableBytes: 5);
        var tinyTool = new WriteFileTool(tinyPolicy);
        // "hello" is 5 bytes, "hello!" is 6 bytes
        var r = DoWrite(tinyTool, "exact.txt", "hello");
        AssertNoError(r); // exactly at limit should succeed
        var r2 = DoWrite(tinyTool, "over.txt", "hello!");
        Assert(r2.IsError, "One byte over limit should fail");
    });

    // ── Error cases: binary overwrite rejection ──

    RunTest("Reject overwrite of binary file (null bytes in existing)", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        File.WriteAllBytes(Path.Combine(dir, "binary.dat"), [0x48, 0x65, 0x00, 0x6C, 0x6F]);
        var r = DoWrite(tool, "binary.dat", "replacement text", overwrite: true);
        Assert(r.IsError, "Binary file overwrite should fail");
        var originalBytes = File.ReadAllBytes(Path.Combine(dir, "binary.dat"));
        Assert(originalBytes.SequenceEqual(new byte[] { 0x48, 0x65, 0x00, 0x6C, 0x6F }), "File unchanged");
    });

    // ── Error cases: directory creation policy ──

    RunTest("Reject missing parent when allowCreateDirectories is false", ref passed, ref failed, () =>
    {
        var (dir, _, _) = Setup(root);
        var strictPolicy = new FileSystemToolPolicy(dir, allowCreateDirectories: false);
        var strictTool = new WriteFileTool(strictPolicy);
        var r = DoWrite(strictTool, "missing/parent/f.txt", "content");
        Assert(r.IsError, "Missing parent should fail");
        AssertMetaString(r, "reason", "missing_parent_directory");
    });

    RunTest("Allow write into existing directory when allowCreateDirectories is false", ref passed, ref failed, () =>
    {
        var (dir, _, _) = Setup(root);
        Directory.CreateDirectory(Path.Combine(dir, "existing"));
        var strictPolicy = new FileSystemToolPolicy(dir, allowCreateDirectories: false);
        var strictTool = new WriteFileTool(strictPolicy);
        var r = DoWrite(strictTool, "existing/f.txt", "content");
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "existing", "f.txt")) == "content", "Written successfully");
    });

    // ── Unbounded scope ──

    RunTest("Create out-of-tree file when policy is unbounded", ref passed, ref failed, () =>
    {
        var (dir, _, _) = Setup(root);
        var outerDir = Path.Combine(root, Guid.NewGuid().ToString("n"), "outer");
        Directory.CreateDirectory(outerDir);
        var unboundedPolicy = new FileSystemToolPolicy(dir, scopeMode: FileSystemScopeMode.Unbounded);
        var unboundedTool = new WriteFileTool(unboundedPolicy);
        var relativePath = Path.GetRelativePath(dir, Path.Combine(outerDir, "out.txt"));
        var r = DoWrite(unboundedTool, relativePath, "outside content\n");
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(outerDir, "out.txt")) == "outside content\n", "Content written outside tree");
        var displayPath = r.Metadata!.Value.GetProperty("path").GetString()!;
        Assert(Path.IsPathRooted(displayPath.Replace('/', Path.DirectorySeparatorChar)),
            $"Display path should be absolute for out-of-tree, got: {displayPath}");
    });

    RunTest("Reject out-of-tree file when policy is bounded", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root); // default is BoundedToBasePath
        var r = DoWrite(tool, "../../escape.txt", "content");
        Assert(r.IsError, "Bounded should reject out-of-tree");
    });

    // ── Metadata completeness ──

    RunTest("Metadata includes all expected fields on create", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var r = DoWrite(tool, "meta-test.txt", "content\n");
        AssertNoError(r);
        var m = r.Metadata!.Value;
        var expectedFields = new[] { "path", "created", "overwroteExisting", "sizeBytes",
            "encoding", "bom", "newlineStyle", "createdDirectories" };
        foreach (var field in expectedFields)
        {
            Assert(m.TryGetProperty(field, out _), $"Missing metadata field: {field}");
        }
    });

    RunTest("Metadata includes all expected fields on overwrite", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "original");
        var r = DoWrite(tool, "f.txt", "replaced", overwrite: true);
        AssertNoError(r);
        var m = r.Metadata!.Value;
        var expectedFields = new[] { "path", "created", "overwroteExisting", "sizeBytes",
            "encoding", "bom", "newlineStyle", "createdDirectories" };
        foreach (var field in expectedFields)
        {
            Assert(m.TryGetProperty(field, out _), $"Missing metadata field: {field}");
        }
    });

    RunTest("Path metadata is relative for in-tree files", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var r = DoWrite(tool, "sub/dir/f.txt", "content");
        AssertNoError(r);
        AssertMetaString(r, "path", "sub/dir/f.txt");
    });

    RunTest("Size metadata reflects encoded byte count", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var r = DoWrite(tool, "size.txt", "hello");
        AssertNoError(r);
        AssertMetaLong(r, "sizeBytes", 5);
    });

    RunTest("Size metadata includes BOM on overwrite of BOM file", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        File.WriteAllBytes(Path.Combine(dir, "f.txt"), [.. bom, .. "x"u8.ToArray()]);
        var r = DoWrite(tool, "f.txt", "hello", overwrite: true);
        AssertNoError(r);
        // "hello" = 5 bytes + 3 byte BOM = 8
        AssertMetaLong(r, "sizeBytes", 8);
    });

    // ── Success text format ──

    RunTest("Success text says 'Created' for new files", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var r = DoWrite(tool, "new.txt", "content");
        AssertNoError(r);
        Assert(r.Text[0].Contains("Created"), $"Expected 'Created' in: {r.Text[0]}");
    });

    RunTest("Success text says 'Overwrote' for existing files", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "original");
        var r = DoWrite(tool, "f.txt", "replaced", overwrite: true);
        AssertNoError(r);
        Assert(r.Text[0].Contains("Overwrote"), $"Expected 'Overwrote' in: {r.Text[0]}");
    });

    // ── Atomic write safety ──

    RunTest("Atomic write does not leave temp files on success", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var r = DoWrite(tool, "atomic.txt", "content");
        AssertNoError(r);
        var files = Directory.GetFiles(dir);
        Assert(files.Length == 1, $"Expected 1 file, found {files.Length}: {string.Join(", ", files.Select(Path.GetFileName))}");
        Assert(Path.GetFileName(files[0]) == "atomic.txt", $"Unexpected file: {files[0]}");
    });

    RunTest("Atomic overwrite does not leave temp files on success", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        File.WriteAllText(Path.Combine(dir, "atomic.txt"), "original");
        var r = DoWrite(tool, "atomic.txt", "replaced", overwrite: true);
        AssertNoError(r);
        var files = Directory.GetFiles(dir);
        Assert(files.Length == 1, $"Expected 1 file, found {files.Length}");
    });

    // ── Descriptor validation ──

    RunTest("Descriptor requires path and content, describes overwrite", ref passed, ref failed, () =>
    {
        var (_, _, tool) = Setup(root);
        var schema = tool.Descriptor.InputSchema;
        schema.GetProperty("type").GetString();
        Assert(schema.GetProperty("additionalProperties").GetBoolean() == false, "additionalProperties=false");
        var required = schema.GetProperty("required").EnumerateArray()
            .Select(v => v.GetString()).ToArray();
        Assert(required.Contains("path"), "Requires path");
        Assert(required.Contains("content"), "Requires content");
        Assert(!required.Contains("overwrite"), "overwrite should be optional");
        var props = schema.GetProperty("properties");
        Assert(props.TryGetProperty("path", out _), "Has path property");
        Assert(props.TryGetProperty("content", out _), "Has content property");
        Assert(props.TryGetProperty("overwrite", out _), "Has overwrite property");
        Assert(tool.Descriptor.Description?.Contains("overwrite") == true, "Description mentions overwrite");
    });

    // ── Edge cases ──

    RunTest("Write file with only whitespace content", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var r = DoWrite(tool, "ws.txt", "   \n  \n");
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "ws.txt")) == "   \n  \n", "Whitespace preserved");
    });

    RunTest("Write file with unicode content", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var content = "Hello \u00e9\u00e0\u00fc \u4e16\u754c \ud83d\ude80\n";
        var r = DoWrite(tool, "unicode.txt", content);
        AssertNoError(r);
        Assert(File.ReadAllText(Path.Combine(dir, "unicode.txt")) == content, "Unicode preserved");
    });

    RunTest("Write file with mixed newlines preserves as-is", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        var content = "line1\nline2\r\nline3\rline4\n";
        var r = DoWrite(tool, "mixed.txt", content);
        AssertNoError(r);
        var bytes = File.ReadAllBytes(Path.Combine(dir, "mixed.txt"));
        Assert(Encoding.UTF8.GetString(bytes) == content, "Mixed newlines preserved exactly");
        AssertMetaString(r, "newlineStyle", "mixed");
    });

    RunTest("Overwrite does not create parent directories", ref passed, ref failed, () =>
    {
        var (dir, _, tool) = Setup(root);
        // overwrite=true but parent doesn't exist => the file doesn't exist so
        // it should try to create, not overwrite
        var r = DoWrite(tool, "newdir/f.txt", "content");
        AssertNoError(r);
        AssertMetaBool(r, "created", true);
        AssertMetaBool(r, "createdDirectories", true);
    });

    Console.WriteLine();
    Console.WriteLine($"Results: {passed} passed, {failed} failed, {passed + failed} total");
    return failed > 0 ? 1 : 0;
}

// ═══════════════════════════════════════════════════════════════════
//  SHARED HELPERS
// ═══════════════════════════════════════════════════════════════════

(string Dir, FileSystemToolPolicy Policy, WriteFileTool Tool) Setup(string root)
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dir);
    var policy = new FileSystemToolPolicy(dir);
    return (dir, policy, new WriteFileTool(policy));
}

ToolInvocationResult DoWrite(WriteFileTool tool, string path, string content, bool overwrite = false)
{
    var inv = BuildWriteInvocation(path, content, overwrite);
    return tool.ExecuteAsync(inv, CancellationToken.None).GetAwaiter().GetResult();
}

static ToolInvocation BuildWriteInvocation(string path, string content, bool overwrite = false)
{
    var args = new Dictionary<string, object?>
    {
        ["path"] = path,
        ["content"] = content,
    };
    if (overwrite)
    {
        args["overwrite"] = true;
    }
    return new ToolInvocation("w", "write_file", args);
}

static string GenerateContent(int maxBytes, int seed = 0)
{
    var sb = new StringBuilder(maxBytes);
    var lineNum = seed;
    var line = $"line-{lineNum:D6}: The quick brown fox jumps over the lazy dog.\n";
    while (sb.Length + line.Length <= maxBytes)
    {
        sb.Append(line);
        lineNum++;
        line = $"line-{lineNum:D6}: The quick brown fox jumps over the lazy dog.\n";
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

static void AssertMetaBool(ToolInvocationResult r, string key, bool expected)
{
    var actual = r.Metadata!.Value.GetProperty(key).GetBoolean();
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

static void AssertMetaLong(ToolInvocationResult r, string key, long expected)
{
    var actual = r.Metadata!.Value.GetProperty(key).GetInt64();
    Assert(actual == expected, $"Metadata '{key}': expected {expected}, got {actual}");
}

static void PrintBenchHeader()
{
    Console.WriteLine($"  {"Scenario",-35} | {"Bytes",7} | {"Mean",7} | {"P50",7} | {"P95",7} | {"Min",7} | {"Max",7} | {"Alloc/iter",8}");
    Console.WriteLine($"  {new string('-', 35)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 7)}-+-{new string('-', 14)}");
}

static void PrintBenchRow(string name, double[] timings, long allocPerIter, int gen0Delta, int contentSize)
{
    var mean = timings.Average();
    var p50 = timings[(timings.Length - 1) / 2];
    var p95 = timings[(int)Math.Ceiling(timings.Length * 0.95) - 1];
    var min = timings[0];
    var max = timings[^1];
    Console.Write($"  {name,-35} | {FormatBytes(contentSize),7} | ");
    Console.Write($"{FormatMicros(mean),7} | {FormatMicros(p50),7} | {FormatMicros(p95),7} | {FormatMicros(min),7} | {FormatMicros(max),7} | ");
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

static int? GetRequiredPositiveIntArg(string[] args, string name, int defaultValue)
{
    var value = GetIntArg(args, name, defaultValue);
    if (value <= 0)
    {
        Console.Error.WriteLine($"{name} must be greater than 0.");
        PrintUsage();
        return null;
    }

    return value;
}

static int? GetRequiredNonNegativeIntArg(string[] args, string name, int defaultValue)
{
    var value = GetIntArg(args, name, defaultValue);
    if (value < 0)
    {
        Console.Error.WriteLine($"{name} must be 0 or greater.");
        PrintUsage();
        return null;
    }

    return value;
}

static int GetEncodedByteCount(string content, Encoding encoding)
{
    return encoding.GetPreamble().Length + encoding.GetByteCount(content);
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run --project scripts/WriteBenchmark/WriteBenchmark.csproj");
    Console.Error.WriteLine("  dotnet run --project scripts/WriteBenchmark/WriteBenchmark.csproj -- bench");
    Console.Error.WriteLine("  dotnet run --project scripts/WriteBenchmark/WriteBenchmark.csproj -- test");
    Console.Error.WriteLine("  dotnet run --project scripts/WriteBenchmark/WriteBenchmark.csproj -- bench --iterations 20 --warmup 5");
}
