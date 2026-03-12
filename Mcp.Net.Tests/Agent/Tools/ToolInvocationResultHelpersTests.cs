using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Tools;

public class ToolInvocationResultHelpersTests
{
    [Fact]
    public void CreateTextResult_ShouldPopulateInvocationIdentityAndText()
    {
        var invocation = new RuntimeToolInvocation(
            "call-1",
            "search",
            new Dictionary<string, object?> { ["query"] = "weather" }
        );

        var result = invocation.CreateTextResult("sunny");

        result.ToolCallId.Should().Be("call-1");
        result.ToolName.Should().Be("search");
        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("sunny");
        result.Structured.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.ResourceLinks.Should().BeEmpty();
    }

    [Fact]
    public void Error_ShouldCreateErrorResult()
    {
        var result = ToolInvocationResults.Error("call-1", "search", "boom");

        result.ToolCallId.Should().Be("call-1");
        result.ToolName.Should().Be("search");
        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Be("boom");
        result.Structured.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.ResourceLinks.Should().BeEmpty();
    }

    [Fact]
    public void CreateResult_ShouldCloneStructuredPayloadsAndMetadata()
    {
        var invocation = new RuntimeToolInvocation(
            "call-1",
            "read_file",
            new Dictionary<string, object?> { ["path"] = "README.md" }
        );
        ToolInvocationResult result;

        using (var structuredDocument = JsonDocument.Parse("""{"path":"README.md"}"""))
        using (var metadataDocument = JsonDocument.Parse("""{"truncated":true}"""))
        {
            result = invocation.CreateResult(
                text: new[] { "partial file contents" },
                structured: structuredDocument.RootElement,
                resourceLinks:
                [
                    new ToolResultResourceLink(
                        "file:///workspace/README.md",
                        "README.md",
                        "Readme",
                        "text/markdown"
                    ),
                ],
                metadata: metadataDocument.RootElement
            );
        }

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("partial file contents");
        result.Structured.Should().NotBeNull();
        result.Structured!.Value.GetProperty("path").GetString().Should().Be("README.md");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.ResourceLinks.Should().ContainSingle();
        result.ResourceLinks[0].Uri.Should().Be("file:///workspace/README.md");
    }
}
