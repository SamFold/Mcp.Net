using FluentAssertions;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using System.Text.Json.Serialization;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Tools;

public class LocalToolBaseTests
{
    [Fact]
    public void BindArguments_ShouldDeserializeTypedArguments()
    {
        var invocation = new RuntimeToolInvocation(
            "call-1",
            "read_file",
            new Dictionary<string, object?>
            {
                ["path"] = "README.md",
                ["maxLines"] = 25,
            }
        );

        var args = invocation.BindArguments<ReadFileArgs>();

        args.Path.Should().Be("README.md");
        args.MaxLines.Should().Be(25);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBindArgumentsAndInvokeTypedImplementation()
    {
        var tool = new ReadFileTool();

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "read_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "README.md",
                    ["maxLines"] = 25,
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("README.md:25");
    }

    [Fact]
    public async Task ExecuteAsync_WhenBindingFails_ShouldReturnErrorResult()
    {
        var tool = new ReadFileTool();

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "read_file",
                new Dictionary<string, object?>
                {
                    ["path"] = 42,
                    ["maxLines"] = "many",
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.ToolCallId.Should().Be("call-1");
        result.ToolName.Should().Be("read_file");
        result.Text.Should().ContainSingle();
        result.Text[0].Should().StartWith("Invalid tool arguments:");
    }

    [Fact]
    public void Descriptor_ShouldUseGeneratedSchemaFromArgumentType()
    {
        var tool = new ReadFileTool();

        tool.Descriptor.Name.Should().Be("read_file");
        tool.Descriptor.Description.Should().Be("Reads a file");
        tool.Descriptor.InputSchema.GetProperty("type").GetString().Should().Be("object");

        var properties = tool.Descriptor.InputSchema.GetProperty("properties");
        properties.GetProperty("path").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("maxLines").GetProperty("type").GetString().Should().Be("integer");

        var required = tool.Descriptor.InputSchema.GetProperty("required").EnumerateArray()
            .Select(entry => entry.GetString())
            .ToArray();
        required.Should().Contain("path");
        required.Should().NotContain("maxLines");
    }

    [Fact]
    public void Descriptor_ShouldHonorJsonPropertyNames()
    {
        var tool = new CustomNamedTool();

        var properties = tool.Descriptor.InputSchema.GetProperty("properties");
        properties.TryGetProperty("custom_path", out var customPathProperty).Should().BeTrue();
        customPathProperty.GetProperty("type").GetString().Should().Be("string");

        var required = tool.Descriptor.InputSchema.GetProperty("required").EnumerateArray()
            .Select(entry => entry.GetString())
            .ToArray();
        required.Should().Contain("custom_path");
    }

    private sealed record ReadFileArgs(string Path, int? MaxLines);

    private sealed class ReadFileTool() : LocalToolBase<ReadFileArgs>("read_file", "Reads a file")
    {
        protected override Task<ToolInvocationResult> ExecuteAsync(
            RuntimeToolInvocation invocation,
            ReadFileArgs arguments,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(invocation.CreateTextResult($"{arguments.Path}:{arguments.MaxLines}"));
    }

    private sealed record CustomNamedArgs(
        [property: JsonPropertyName("custom_path")]
        string Path
    );

    private sealed class CustomNamedTool() : LocalToolBase<CustomNamedArgs>("custom_named", "Has a custom property name")
    {
        protected override Task<ToolInvocationResult> ExecuteAsync(
            RuntimeToolInvocation invocation,
            CustomNamedArgs arguments,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(invocation.CreateTextResult(arguments.Path));
    }
}
