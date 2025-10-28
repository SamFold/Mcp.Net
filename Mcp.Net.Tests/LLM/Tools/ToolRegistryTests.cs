using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Mcp.Net.Tests.LLM.Tools;

/// <summary>
/// Unit tests for the <see cref="ToolRegistry"/> class.
/// </summary>
public class ToolRegistryTests
{
    private readonly Mock<ILogger<ToolRegistry>> _mockLogger;
    private readonly ToolRegistry _registry;

    public ToolRegistryTests()
    {
        _mockLogger = new Mock<ILogger<ToolRegistry>>();
        _registry = new ToolRegistry(_mockLogger.Object);
        _registry.RegisterTools(CreateTestTools());
    }

    [Fact]
    public void ValidateToolIds_ShouldReturnMissingTools()
    {
        var toolIds = new[]
        {
            "finance_budget",
            "non_existent_tool",
            "another_missing_tool",
            "travel_search",
        };

        var missingTools = _registry.ValidateToolIds(toolIds);

        Assert.Equal(2, missingTools.Count);
        Assert.Contains("non_existent_tool", missingTools);
        Assert.Contains("another_missing_tool", missingTools);
    }

    [Fact]
    public void ValidateToolIds_ShouldReturnEmptyListForValidTools()
    {
        var toolIds = new[] { "finance_budget", "travel_search", "general_helper" };

        var missingTools = _registry.ValidateToolIds(toolIds);

        Assert.Empty(missingTools);
    }

    [Fact]
    public void GetToolsByPrefix_ShouldReturnCorrectTools()
    {
        var tools = _registry.GetToolsByPrefix("finance_");

        Assert.Single(tools);
        Assert.All(tools, t => Assert.StartsWith("finance_", t.Name));
    }

    [Fact]
    public void IsToolEnabled_ShouldReturnCorrectState()
    {
        Assert.True(_registry.IsToolEnabled("finance_budget"));

        _registry.SetEnabledTools(new[] { "travel_search" });

        Assert.False(_registry.IsToolEnabled("finance_budget"));
        Assert.True(_registry.IsToolEnabled("travel_search"));
    }

    [Fact]
    public async Task GetToolCategoriesAsync_ShouldReturnAllCategories()
    {
        var categories = await _registry.GetToolCategoriesAsync();

        Assert.Contains("finance", categories);
        Assert.Contains("travel", categories);
        Assert.Contains("research", categories);
        Assert.Contains("general", categories);
    }

    [Fact]
    public async Task GetToolsByCategoryAsync_ShouldReturnCorrectTools()
    {
        var financeTools = await _registry.GetToolsByCategoryAsync("finance");
        var generalTools = await _registry.GetToolsByCategoryAsync("general");

        Assert.Contains("finance_budget", financeTools);
        Assert.DoesNotContain("travel_search", financeTools);
        Assert.Contains("general_helper", generalTools);
    }

    [Fact]
    public void ToolsUpdated_ShouldRaiseEvent_WhenRegistering()
    {
        bool wasRaised = false;

        _registry.ToolsUpdated += (_, tools) =>
        {
            wasRaised = true;
            Assert.Equal(3, tools.Count);
        };

        _registry.RegisterTools(CreateTestTools());

        Assert.True(wasRaised);
    }

    [Fact]
    public void GetCategoriesForTool_ShouldReturnExpectedValues()
    {
        var categories = _registry.GetCategoriesForTool("travel_search");

        Assert.Contains("travel", categories);
        Assert.Contains("research", categories);
    }

    [Fact]
    public void RegisterTools_ShouldPreserveEnabledState()
    {
        _registry.SetEnabledTools(new[] { "finance_budget" });

        _registry.RegisterTools(CreateTestTools());

        Assert.True(_registry.IsToolEnabled("finance_budget"));
        Assert.False(_registry.IsToolEnabled("travel_search"));
    }

    [Fact]
    public void SetEnabledTools_ShouldFallbackToAllTools_WhenNamesDoNotMatch()
    {
        _registry.SetEnabledTools(new[] { "non_existent_tool" });

        Assert.True(_registry.IsToolEnabled("finance_budget"));
        Assert.True(_registry.IsToolEnabled("travel_search"));
    }

    [Fact]
    public void GetCategoryDescriptors_ShouldContainAllRegisteredTools()
    {
        var descriptors = _registry.GetCategoryDescriptors();

        var finance = descriptors.First(d => d.Key == "finance");
        Assert.Contains("finance_budget", finance.ToolNames);

        var general = descriptors.First(d => d.Key == "general");
        Assert.Contains("general_helper", general.ToolNames);
    }

    private static IEnumerable<Tool> CreateTestTools()
    {
        return new[]
        {
            new Tool
            {
                Name = "finance_budget",
                Description = "Budget planner",
                InputSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement,
                Annotations = new Dictionary<string, object?>
                {
                    { "category", "finance" },
                },
            },
            new Tool
            {
                Name = "travel_search",
                Description = "Search flights",
                InputSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement,
                Annotations = new Dictionary<string, object?>
                {
                    { "categories", new object[] { "travel", "research" } },
                },
            },
            new Tool
            {
                Name = "general_helper",
                Description = "Misc helper",
                InputSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement,
            },
        };
    }
}
