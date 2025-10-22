using System.Linq;
using System.Text.Json;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Server.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Server;

public class ToolDiscoveryServiceTests
{
    private readonly ToolDiscoveryService _service;

    public ToolDiscoveryServiceTests()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<ToolDiscoveryService>();
        _service = new ToolDiscoveryService(logger);
    }

    [Fact]
    public void DiscoverTools_ProducesSchemaWithOriginalParameterCasing()
    {
        var descriptors = _service.DiscoverTools(new[] { typeof(SchemaTool).Assembly });
        var descriptor = Assert.Single(descriptors, d => d.Name == "schema.sample");

        var schema = descriptor.InputSchema;
        Assert.True(schema.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("userId", out var _));
        Assert.True(properties.TryGetProperty("retryCount", out var _));

        Assert.True(schema.TryGetProperty("required", out var required));
        Assert.Contains("userId", required.EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(
            "retryCount",
            required.EnumerateArray().Select(e => e.GetString())
        );
    }

    private class SchemaTool
    {
        [McpTool("schema.sample", "Tool used to validate discovery schema output")]
        public string Execute(
            [McpParameter(required: true, description: "User identifier")]
            string userId,
            [McpParameter(required: false, description: "Retry attempts")]
            int retryCount = 3
        )
        {
            return JsonSerializer.Serialize(new { userId, retryCount });
        }
    }
}
