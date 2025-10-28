using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
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

    [Fact]
    public void DiscoverTools_WhenCategoryMetadataProvided_EmitsAnnotations()
    {
        var descriptors = _service.DiscoverTools(new[] { typeof(CategoryTool).Assembly });
        var descriptor = Assert.Single(descriptors, d => d.Name == "category.sample");

        Assert.NotNull(descriptor.Annotations);
        Assert.True(descriptor.Annotations!.TryGetValue("category", out var value));

        var categoryObject = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal("utilities", categoryObject["id"]);
        Assert.Equal("Utilities", categoryObject["displayName"]);
        Assert.Equal(5d, categoryObject["order"]);
    }

    [Fact]
    public void DiscoverTools_WhenMultipleCategoriesProvided_EmitsArrayAnnotation()
    {
        var descriptors = _service.DiscoverTools(new[] { typeof(MultiCategoryTool).Assembly });
        var descriptor = Assert.Single(descriptors, d => d.Name == "category.multi" );

        Assert.NotNull(descriptor.Annotations);
        Assert.True(descriptor.Annotations!.TryGetValue("categories", out var value));

        var categories = Assert.IsType<List<object?>>(value);
        categories.Should().HaveCount(3);

        var primary = Assert.IsType<Dictionary<string, object?>>(categories[0]);
        Assert.Equal("primary", primary["id"]);
        Assert.Equal("Primary", primary["displayName"]);
        Assert.False(primary.ContainsKey("order"));

        categories[1].Should().BeOfType<string>().Subject.Should().Be("secondary");
        categories[2].Should().BeOfType<string>().Subject.Should().Be("tertiary");
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

    private class CategoryTool
    {
        [McpTool(
            "category.sample",
            "Tool used to validate category annotations",
            Category = "utilities",
            CategoryDisplayName = "Utilities",
            CategoryOrder = 5
        )]
        public int Execute(
            [McpParameter(required: true, description: "Sample value")] int value
        ) => value;
    }

    private class MultiCategoryTool
    {
        [McpTool(
            "category.multi",
            "Tool used to validate multi-category annotations",
            Category = "primary",
            CategoryDisplayName = "Primary",
            Categories = new[] { "secondary", "tertiary" }
        )]
        public int Execute([McpParameter(required: true)] int value) => value;
    }
}
