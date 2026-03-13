using FluentAssertions;
using Mcp.Net.Agent.Tools;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Tools;

public class GlobToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnDeterministicRootRelativeMatches()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "src", "b"));
        Directory.CreateDirectory(Path.Combine(root.Path, "src", "a"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "root.cs"), "// root");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "a", "alpha.cs"), "// a");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "b", "beta.cs"), "// b");

        var tool = new GlobTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "glob_files",
                new Dictionary<string, object?> { ["pattern"] = "src/**/*.cs" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0]
            .Split('\n')
            .Should()
            .Equal("src/root.cs", "src/a/alpha.cs", "src/b/beta.cs");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("searchRoot").GetString().Should().Be("src");
        result.Metadata!.Value.GetProperty("returnedCount").GetInt32().Should().Be(3);
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRespectSingleSegmentWildcardDepth()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "src", "one"));
        Directory.CreateDirectory(Path.Combine(root.Path, "src", "one", "two"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "one", "match.cs"), "// yes");
        await File.WriteAllTextAsync(
            Path.Combine(root.Path, "src", "one", "two", "skip.cs"),
            "// no"
        );

        var tool = new GlobTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "glob_files",
                new Dictionary<string, object?> { ["pattern"] = "src/*/*.cs" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("src/one/match.cs");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldClampRequestedLimitToPolicyAndReportTruncation()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "alpha.cs"), "// a");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "beta.cs"), "// b");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "gamma.cs"), "// c");

        var tool = new GlobTool(
            new FileSystemToolPolicy(root.Path, maxGlobMatches: 2)
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "glob_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "src/*.cs",
                    ["limit"] = 50,
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Split('\n').Should().Equal("src/alpha.cs", "src/beta.cs");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("limit").GetInt32().Should().Be(2);
        result.Metadata!.Value.GetProperty("returnedCount").GetInt32().Should().Be(2);
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBypassDefaultSkippedDirectoriesWhenPathExplicitlyTargetsOne()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git", "hooks"));
        await File.WriteAllTextAsync(
            Path.Combine(root.Path, ".git", "hooks", "pre-commit"),
            "#!/bin/sh"
        );
        await File.WriteAllTextAsync(Path.Combine(root.Path, "visible.txt"), "visible");

        var tool = new GlobTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "glob_files",
                new Dictionary<string, object?>
                {
                    ["path"] = ".git",
                    ["pattern"] = "**/*",
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be(".git/hooks/pre-commit");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be(".git");
        result.Metadata!.Value.GetProperty("searchRoot").GetString().Should().Be(".git");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectPathOutsideConfiguredRoot()
    {
        using var root = new TemporaryDirectory();
        var tool = new GlobTool(new FileSystemToolPolicy(root.Path));

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "glob_files",
                new Dictionary<string, object?>
                {
                    ["path"] = "../outside",
                    ["pattern"] = "**/*.cs",
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("outside the configured root");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnAbsoluteMatchesWhenSearchingOutsideBasePathInUnboundedMode()
    {
        using var workspace = new TemporaryDirectory();
        var basePath = Path.Combine(workspace.Path, "repo");
        var externalPath = Path.Combine(workspace.Path, "external");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(externalPath);
        await File.WriteAllTextAsync(Path.Combine(externalPath, "alpha.cs"), "// external");

        var tool = new GlobTool(
            new FileSystemToolPolicy(
                basePath,
                scopeMode: FileSystemScopeMode.Unbounded
            )
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1u",
                "glob_files",
                new Dictionary<string, object?>
                {
                    ["path"] = "../external",
                    ["pattern"] = "*.cs",
                }
            )
        );

        var expectedPath = Path.GetFullPath(Path.Combine(externalPath, "alpha.cs")).Replace('\\', '/');
        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be(expectedPath);
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value
            .GetProperty("path")
            .GetString()
            .Should()
            .Be(Path.GetFullPath(externalPath).Replace('\\', '/'));
        result.Metadata!.Value
            .GetProperty("searchRoot")
            .GetString()
            .Should()
            .Be(Path.GetFullPath(externalPath).Replace('\\', '/'));
    }

    [Fact]
    public void Descriptor_ShouldRequirePatternAndDescribeOptionalPathAndLimit()
    {
        using var root = new TemporaryDirectory();
        var tool = new GlobTool(new FileSystemToolPolicy(root.Path));

        var schema = tool.Descriptor.InputSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var required = schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToArray();
        required.Should().Contain("pattern");

        var properties = schema.GetProperty("properties");
        properties.GetProperty("pattern").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("path").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("limit").GetProperty("type").GetString().Should().Be("integer");
        tool.Descriptor.Description.Should().Contain("glob pattern");
    }
}
