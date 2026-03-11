using System.Text.Json;
using FluentAssertions;
using Mcp.Net.LLM.Models;
using Xunit;

namespace Mcp.Net.Tests.LLM.Models;

public class ToolInvocationResultTests
{
    [Fact]
    public void ToWireJson_ShouldIncludeCoreFields()
    {
        var result = new ToolInvocationResult(
            "call-2",
            "tools/error",
            true,
            new[] { "payload" },
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );

        var json = JsonDocument.Parse(result.ToWireJson()).RootElement;

        json.GetProperty("toolName").GetString().Should().Be("tools/error");
        json.GetProperty("isError").GetBoolean().Should().BeTrue();
        json.GetProperty("text")[0].GetString().Should().Be("payload");
    }
}
