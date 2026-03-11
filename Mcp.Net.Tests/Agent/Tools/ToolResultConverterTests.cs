using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Xunit;

namespace Mcp.Net.Tests.Agent.Tools;

public class ToolResultConverterTests
{
    [Fact]
    public void FromMcpResult_ShouldProjectStructuredFields()
    {
        var coreResult = new ToolCallResult
        {
            Content = new ContentBase[]
            {
                new TextContent { Text = "Hello world" },
                new ResourceLinkContent { Uri = "https://example.com/doc", Name = "Spec sheet" },
            },
            Structured = JsonSerializer.SerializeToElement(new { value = 42 }),
            ResourceLinks =
            [
                new ToolCallResourceLink
                {
                    Uri = "https://example.com/doc",
                    Name = "Spec sheet",
                    Description = "Primary reference",
                },
            ],
            Meta = new Dictionary<string, object?>
            {
                ["source"] = "unit-test",
            },
        };

        var result = ToolResultConverter.FromMcpResult("call-1", "tools/test", coreResult);

        result.ToolCallId.Should().Be("call-1");
        result.ToolName.Should().Be("tools/test");
        result.IsError.Should().BeFalse();
        result.Text.Should().Contain(new[] { "Hello world", "Spec sheet" });
        result.Structured.Should().NotBeNull();
        result.Structured!.Value.GetProperty("value").GetInt32().Should().Be(42);
        result.ResourceLinks.Should().HaveCount(1);
        result.ResourceLinks[0].Uri.Should().Be("https://example.com/doc");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("source").GetString().Should().Be("unit-test");
    }

    [Fact]
    public void FromMcpResult_ErrorResult_ShouldPreserveErrorFlag()
    {
        var coreResult = new ToolCallResult
        {
            Content = new ContentBase[] { new TextContent { Text = "payload" } },
            IsError = true,
        };

        var result = ToolResultConverter.FromMcpResult("call-2", "tools/error", coreResult);

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Be("payload");
    }
}
