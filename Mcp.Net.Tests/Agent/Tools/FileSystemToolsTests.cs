using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Tools;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Tools;

public class FileSystemToolPolicyTests
{
    [Fact]
    public void Resolve_ShouldReturnCanonicalFullAndDisplayPath()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "docs"));

        var policy = new FileSystemToolPolicy(root.Path);

        var resolved = policy.Resolve("docs/../docs");

        resolved.FullPath.Should().Be(Path.GetFullPath(Path.Combine(root.Path, "docs")));
        resolved.DisplayPath.Should().Be("docs");
    }

    [Fact]
    public void Resolve_ShouldRejectPathOutsideConfiguredRoot()
    {
        using var root = new TemporaryDirectory();
        var policy = new FileSystemToolPolicy(root.Path);

        var act = () => policy.Resolve("../outside.txt");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*outside the configured root*");
    }

    [Fact]
    public void Resolve_ShouldAllowPathOutsideBasePathInUnboundedMode()
    {
        using var workspace = new TemporaryDirectory();
        var basePath = Path.Combine(workspace.Path, "repo");
        Directory.CreateDirectory(basePath);
        var outsidePath = Path.Combine(workspace.Path, "outside.txt");
        File.WriteAllText(outsidePath, "outside");

        var policy = new FileSystemToolPolicy(
            basePath,
            scopeMode: FileSystemScopeMode.Unbounded
        );

        var resolved = policy.Resolve("../outside.txt");

        resolved.FullPath.Should().Be(Path.GetFullPath(outsidePath));
        resolved.DisplayPath.Should().Be(Path.GetFullPath(outsidePath).Replace('\\', '/'));
    }

    [Fact]
    public void ResolveOrBase_ShouldReturnBasePathWhenPathIsMissing()
    {
        using var root = new TemporaryDirectory();
        var policy = new FileSystemToolPolicy(
            root.Path,
            scopeMode: FileSystemScopeMode.Unbounded
        );

        var resolved = policy.ResolveOrBase(null);

        resolved.FullPath.Should().Be(Path.GetFullPath(root.Path));
        resolved.DisplayPath.Should().Be(".");
    }
}

public class ReadFileToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReadFileWithinRoot()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(filePath, "alpha\nbeta");

        var tool = new ReadFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "read_file",
                new Dictionary<string, object?> { ["path"] = "notes.txt" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("alpha\nbeta");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be("notes.txt");
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectPathOutsideConfiguredRoot()
    {
        using var root = new TemporaryDirectory();
        var tool = new ReadFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "read_file",
                new Dictionary<string, object?> { ["path"] = "../outside.txt" }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("outside the configured root");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReadOutOfTreeFileWhenPolicyIsUnbounded()
    {
        using var workspace = new TemporaryDirectory();
        var basePath = Path.Combine(workspace.Path, "repo");
        Directory.CreateDirectory(basePath);
        var outsidePath = Path.Combine(workspace.Path, "outside.txt");
        await File.WriteAllTextAsync(outsidePath, "external");

        var tool = new ReadFileTool(
            new FileSystemToolPolicy(
                basePath,
                scopeMode: FileSystemScopeMode.Unbounded
            )
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1b",
                "read_file",
                new Dictionary<string, object?> { ["path"] = "../outside.txt" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("external");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value
            .GetProperty("path")
            .GetString()
            .Should()
            .Be(Path.GetFullPath(outsidePath).Replace('\\', '/'));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnErrorWhenPathIsMissing()
    {
        using var root = new TemporaryDirectory();
        var tool = new ReadFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "read_file",
                new Dictionary<string, object?>()
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("The 'path' argument is required.");
        result.Text[0].Should().Contain("README.md");
    }

    [Fact]
    public void Descriptor_ShouldRequirePathAndDescribeHowToUseTheTool()
    {
        using var root = new TemporaryDirectory();
        var tool = new ReadFileTool(new FileSystemToolPolicy(root.Path));

        var schema = tool.Descriptor.InputSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var required = schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToArray();
        required.Should().Contain("path");

        var pathProperty = schema.GetProperty("properties").GetProperty("path");
        pathProperty.GetProperty("type").GetString().Should().Be("string");
        pathProperty.GetProperty("minLength").GetInt32().Should().Be(1);
        pathProperty.GetProperty("description").GetString().Should().Contain("Required.");
        tool.Descriptor.Description.Should().Contain("Use list_files first");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSetTruncationMetadataWhenReadExceedsPolicyLimits()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(filePath, "line-1\nline-2\nline-3\nline-4");

        var tool = new ReadFileTool(
            new FileSystemToolPolicy(root.Path, maxReadBytes: 1024, maxReadLines: 2)
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "read_file",
                new Dictionary<string, object?> { ["path"] = "notes.txt" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("line-1\nline-2");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.Metadata!.Value.GetProperty("truncatedByLines").GetBoolean().Should().BeTrue();
        result.Metadata!.Value.GetProperty("truncatedByBytes").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnMutationMetadataForOptimisticConcurrency()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(filePath, "alpha\r\nbeta\r\n", encoding);

        var tool = new ReadFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "read_file",
                new Dictionary<string, object?> { ["path"] = "notes.txt" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("contentHash").GetString().Should().Be(
            await FileSystemToolTestHelpers.GetContentHashAsync(filePath)
        );
        result.Metadata!.Value.GetProperty("encoding").GetString().Should().Be("utf-8");
        result.Metadata!.Value.GetProperty("bom").GetBoolean().Should().BeTrue();
        result.Metadata!.Value.GetProperty("newlineStyle").GetString().Should().Be("crlf");
    }
}

public class EditFileToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldApplyExactEditAndReturnUpdatedHashes()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(filePath, "alpha\r\nbeta\r\n");

        var policy = new FileSystemToolPolicy(root.Path);
        var readTool = new ReadFileTool(policy);
        var editTool = new EditFileTool(policy);
        var contentHash = await FileSystemToolTestHelpers.ReadContentHashAsync(
            readTool,
            "notes.txt"
        );

        var result = await editTool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-2",
                "edit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["expectedContentHash"] = contentHash,
                    ["edits"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["oldText"] = "beta",
                            ["newText"] = "gamma",
                        },
                    },
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("appliedEditCount").GetInt32().Should().Be(1);
        result.Metadata!.Value.GetProperty("contentHashBefore").GetString().Should().Be(contentHash);
        result.Metadata!.Value.GetProperty("contentHashAfter").GetString().Should().NotBe(contentHash);
        result.Metadata!.Value.GetProperty("firstChangedLine").GetInt32().Should().Be(2);
        result.Metadata!.Value.GetProperty("usedNormalizedLineEndingMatch").GetBoolean().Should().BeFalse();
        (await File.ReadAllTextAsync(filePath)).Should().Be("alpha\r\ngamma\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseNormalizedLineEndingMatchAndPreserveFileLineEndings()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(filePath, "alpha\r\nbeta\r\n");

        var policy = new FileSystemToolPolicy(root.Path);
        var readTool = new ReadFileTool(policy);
        var editTool = new EditFileTool(policy);
        var contentHash = await FileSystemToolTestHelpers.ReadContentHashAsync(
            readTool,
            "notes.txt"
        );

        var result = await editTool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-2",
                "edit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["expectedContentHash"] = contentHash,
                    ["edits"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["oldText"] = "alpha\nbeta\n",
                            ["newText"] = "alpha\ngamma\n",
                        },
                    },
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("usedNormalizedLineEndingMatch").GetBoolean().Should().BeTrue();
        (await File.ReadAllTextAsync(filePath)).Should().Be("alpha\r\ngamma\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectStaleContentHash()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(filePath, "alpha\nbeta\n");

        var tool = new EditFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-2",
                "edit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["expectedContentHash"] = "sha256:deadbeef",
                    ["edits"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["oldText"] = "beta",
                            ["newText"] = "gamma",
                        },
                    },
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("content hash");
        (await File.ReadAllTextAsync(filePath)).Should().Be("alpha\nbeta\n");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectAmbiguousMatches()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(filePath, "repeat\nrepeat\n");

        var policy = new FileSystemToolPolicy(root.Path);
        var readTool = new ReadFileTool(policy);
        var editTool = new EditFileTool(policy);
        var contentHash = await FileSystemToolTestHelpers.ReadContentHashAsync(
            readTool,
            "notes.txt"
        );

        var result = await editTool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-2",
                "edit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["expectedContentHash"] = contentHash,
                    ["edits"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["oldText"] = "repeat",
                            ["newText"] = "once",
                        },
                    },
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("multiple");
        (await File.ReadAllTextAsync(filePath)).Should().Be("repeat\nrepeat\n");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldEditOutOfTreeFileWhenPolicyIsUnbounded()
    {
        using var workspace = new TemporaryDirectory();
        var basePath = Path.Combine(workspace.Path, "repo");
        Directory.CreateDirectory(basePath);
        var outsidePath = Path.Combine(workspace.Path, "outside.txt");
        await File.WriteAllTextAsync(outsidePath, "alpha\nbeta\n");

        var policy = new FileSystemToolPolicy(
            basePath,
            scopeMode: FileSystemScopeMode.Unbounded
        );
        var readTool = new ReadFileTool(policy);
        var editTool = new EditFileTool(policy);
        var contentHash = await FileSystemToolTestHelpers.ReadContentHashAsync(
            readTool,
            "../outside.txt"
        );

        var result = await editTool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-2u",
                "edit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "../outside.txt",
                    ["expectedContentHash"] = contentHash,
                    ["edits"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["oldText"] = "beta",
                            ["newText"] = "gamma",
                        },
                    },
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value
            .GetProperty("path")
            .GetString()
            .Should()
            .Be(Path.GetFullPath(outsidePath).Replace('\\', '/'));
        (await File.ReadAllTextAsync(outsidePath)).Should().Be("alpha\ngamma\n");
    }

    [Fact]
    public void Descriptor_ShouldRequirePathHashAndEdits()
    {
        using var root = new TemporaryDirectory();
        var tool = new EditFileTool(new FileSystemToolPolicy(root.Path));

        var schema = tool.Descriptor.InputSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var required = schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToArray();
        required.Should().Contain(new[] { "path", "expectedContentHash", "edits" });

        var editsProperty = schema.GetProperty("properties").GetProperty("edits");
        editsProperty.GetProperty("type").GetString().Should().Be("array");
        editsProperty.GetProperty("items").GetProperty("type").GetString().Should().Be("object");
        tool.Descriptor.Description.Should().Contain("optimistic concurrency");
    }
}

public class ListFilesToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldListEntriesInDeterministicRootRelativeOrder()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "zeta.txt"), "z");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "alpha.txt"), "a");

        var tool = new ListFilesTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "list_files",
                new Dictionary<string, object?> { ["path"] = "." }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Split('\n').Should().Equal("alpha.txt", "src/", "zeta.txt");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be(".");
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectPathOutsideConfiguredRoot()
    {
        using var root = new TemporaryDirectory();
        var tool = new ListFilesTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "list_files",
                new Dictionary<string, object?> { ["path"] = "../outside" }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("outside the configured root");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDefaultToRootWhenPathIsMissing()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "alpha.txt"), "a");
        var tool = new ListFilesTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "list_files",
                new Dictionary<string, object?>()
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Be("alpha.txt");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be(".");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldListOutOfTreeEntriesWithAbsolutePathsWhenPolicyIsUnbounded()
    {
        using var workspace = new TemporaryDirectory();
        var basePath = Path.Combine(workspace.Path, "repo");
        var outsideDirectory = Path.Combine(workspace.Path, "external");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(outsideDirectory);
        await File.WriteAllTextAsync(Path.Combine(outsideDirectory, "alpha.txt"), "a");

        var tool = new ListFilesTool(
            new FileSystemToolPolicy(
                basePath,
                scopeMode: FileSystemScopeMode.Unbounded
            )
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1u",
                "list_files",
                new Dictionary<string, object?> { ["path"] = "../external" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be(
            $"{Path.GetFullPath(Path.Combine(outsideDirectory, "alpha.txt")).Replace('\\', '/')}"
        );
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value
            .GetProperty("path")
            .GetString()
            .Should()
            .Be(Path.GetFullPath(outsideDirectory).Replace('\\', '/'));
    }

    [Fact]
    public void Descriptor_ShouldDescribeOptionalPathAndRootDefault()
    {
        using var root = new TemporaryDirectory();
        var tool = new ListFilesTool(new FileSystemToolPolicy(root.Path));

        var schema = tool.Descriptor.InputSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var required = schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToArray();
        required.Should().BeEmpty();

        var pathProperty = schema.GetProperty("properties").GetProperty("path");
        pathProperty.GetProperty("description").GetString().Should().Contain("Defaults to '.'.");
        tool.Descriptor.Description.Should().Contain("defaults to the current directory");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSetTruncationMetadataWhenDirectoryExceedsEntryLimit()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "alpha.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "beta.txt"), "b");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "gamma.txt"), "c");

        var tool = new ListFilesTool(
            new FileSystemToolPolicy(root.Path, maxDirectoryEntries: 2)
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "list_files",
                new Dictionary<string, object?> { ["path"] = "." }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Split('\n').Should().Equal("alpha.txt", "beta.txt");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.Metadata!.Value.GetProperty("totalEntryCount").GetInt32().Should().Be(3);
        result.Metadata!.Value.GetProperty("entryLimit").GetInt32().Should().Be(2);
    }
}

public class WriteFileToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldCreateNewFileAndParentDirectories()
    {
        using var root = new TemporaryDirectory();
        var tool = new WriteFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-1",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "src/new/notes.txt",
                    ["content"] = "alpha\nbeta\n",
                }
            )
        );

        var fullPath = Path.Combine(root.Path, "src", "new", "notes.txt");
        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Contain("Created");
        (await File.ReadAllTextAsync(fullPath)).Should().Be("alpha\nbeta\n");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be("src/new/notes.txt");
        result.Metadata!.Value.GetProperty("created").GetBoolean().Should().BeTrue();
        result.Metadata!.Value.GetProperty("overwroteExisting").GetBoolean().Should().BeFalse();
        result.Metadata!.Value.GetProperty("encoding").GetString().Should().Be("utf-8");
        result.Metadata!.Value.GetProperty("bom").GetBoolean().Should().BeFalse();
        result.Metadata!.Value.GetProperty("newlineStyle").GetString().Should().Be("lf");
        result.Metadata!.Value.GetProperty("createdDirectories").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectExistingFileWhenOverwriteIsFalse()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(filePath, "original");

        var tool = new WriteFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-2",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["content"] = "replacement",
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("already exists");
        (await File.ReadAllTextAsync(filePath)).Should().Be("original");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldOverwriteExistingTextFileAndPreserveEncodingAndBom()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "notes.txt");
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        await File.WriteAllTextAsync(filePath, "alpha\r\nbeta\r\n", encoding);

        var tool = new WriteFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-3",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["content"] = "gamma\n",
                    ["overwrite"] = true,
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Contain("Overwrote");
        var bytes = await File.ReadAllBytesAsync(filePath);
        bytes.Take(2).Should().Equal(0xFF, 0xFE);
        encoding.GetString(bytes[2..]).Should().Be("gamma\n");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("created").GetBoolean().Should().BeFalse();
        result.Metadata!.Value.GetProperty("overwroteExisting").GetBoolean().Should().BeTrue();
        result.Metadata!.Value.GetProperty("encoding").GetString().Should().Be("utf-16-le");
        result.Metadata!.Value.GetProperty("bom").GetBoolean().Should().BeTrue();
        result.Metadata!.Value.GetProperty("newlineStyle").GetString().Should().Be("lf");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectBinaryExistingFileWhenOverwriteIsTrue()
    {
        using var root = new TemporaryDirectory();
        var filePath = Path.Combine(root.Path, "binary.dat");
        await File.WriteAllBytesAsync(filePath, [0x00, 0x01, 0x02, 0x03]);

        var tool = new WriteFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-4",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "binary.dat",
                    ["content"] = "replacement",
                    ["overwrite"] = true,
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("supported text file");
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(0x00, 0x01, 0x02, 0x03);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCreateOutOfTreeFileWhenPolicyIsUnbounded()
    {
        using var workspace = new TemporaryDirectory();
        var basePath = Path.Combine(workspace.Path, "repo");
        Directory.CreateDirectory(basePath);

        var tool = new WriteFileTool(
            new FileSystemToolPolicy(
                basePath,
                scopeMode: FileSystemScopeMode.Unbounded
            )
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-5",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "../external/notes.txt",
                    ["content"] = "outside\n",
                }
            )
        );

        var fullPath = Path.GetFullPath(Path.Combine(workspace.Path, "external", "notes.txt"));
        result.IsError.Should().BeFalse();
        (await File.ReadAllTextAsync(fullPath)).Should().Be("outside\n");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be(fullPath.Replace('\\', '/'));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRespectWritableByteLimit()
    {
        using var root = new TemporaryDirectory();
        var tool = new WriteFileTool(
            new FileSystemToolPolicy(root.Path, maxWritableBytes: 4)
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-6",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["content"] = "alpha",
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("exceeds");
        File.Exists(Path.Combine(root.Path, "notes.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectMissingParentWhenDirectoryCreationIsDisabled()
    {
        using var root = new TemporaryDirectory();
        var tool = new WriteFileTool(
            new FileSystemToolPolicy(root.Path, allowCreateDirectories: false)
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-7",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "src/new/notes.txt",
                    ["content"] = "alpha",
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("parent directory");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectContentContainingNullCharacters()
    {
        using var root = new TemporaryDirectory();
        var tool = new WriteFileTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-write-8",
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "notes.txt",
                    ["content"] = "alpha\0beta",
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("text content");
    }

    [Fact]
    public void Descriptor_ShouldRequirePathAndContentAndDescribeOverwrite()
    {
        using var root = new TemporaryDirectory();
        var tool = new WriteFileTool(new FileSystemToolPolicy(root.Path));

        var schema = tool.Descriptor.InputSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var required = schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToArray();
        required.Should().Contain(new[] { "path", "content" });

        var properties = schema.GetProperty("properties");
        properties.GetProperty("path").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("content").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("overwrite").GetProperty("type").GetString().Should().Be("boolean");
        tool.Descriptor.Description.Should().Contain("overwrite");
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mcp-net-tests",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal static class FileSystemToolTestHelpers
{
    public static async Task<string> ReadContentHashAsync(ReadFileTool tool, string path)
    {
        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-read",
                "read_file",
                new Dictionary<string, object?> { ["path"] = path }
            )
        );

        result.IsError.Should().BeFalse();
        result.Metadata.Should().NotBeNull();
        return result.Metadata!.Value.GetProperty("contentHash").GetString()!;
    }

    public static async Task<string> GetContentHashAsync(string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }
}
