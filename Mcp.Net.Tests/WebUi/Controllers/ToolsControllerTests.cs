using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.WebUi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.WebUi.Controllers;

public class ToolsControllerTests
{
    [Fact]
    public async Task GetToolCategories_ShouldReturnCategoriesFromToolRegistry()
    {
        using var financeSchema = JsonDocument.Parse("{}");
        using var researchSchema = JsonDocument.Parse("{}");

        var registry = new ToolRegistry();
        registry.RegisterTools(
            new[]
            {
                new Tool
                {
                    Name = "finance_budget",
                    Description = "Budget planner",
                    InputSchema = financeSchema.RootElement.Clone(),
                    Annotations = new Dictionary<string, object?> { ["category"] = "finance" },
                },
                new Tool
                {
                    Name = "travel_search",
                    Description = "Search trips",
                    InputSchema = researchSchema.RootElement.Clone(),
                    Annotations = new Dictionary<string, object?>
                    {
                        ["categories"] = new object[] { "travel", "research" },
                    },
                },
            }
        );

        var controller = new ToolsController(NullLogger<ToolsController>.Instance, registry);

        var result = await controller.GetToolCategories();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var categories = ok.Value.Should().BeAssignableTo<IEnumerable<string>>().Subject.ToArray();
        categories.Should().Contain(new[] { "finance", "travel", "research" });
    }

    [Fact]
    public async Task GetToolsByCategory_ShouldReturnMatchingToolsFromToolRegistry()
    {
        using var financeSchema = JsonDocument.Parse("{}");
        using var researchSchema = JsonDocument.Parse("{}");

        var registry = new ToolRegistry();
        registry.RegisterTools(
            new[]
            {
                new Tool
                {
                    Name = "finance_budget",
                    Description = "Budget planner",
                    InputSchema = financeSchema.RootElement.Clone(),
                    Annotations = new Dictionary<string, object?> { ["category"] = "finance" },
                },
                new Tool
                {
                    Name = "travel_search",
                    Description = "Search trips",
                    InputSchema = researchSchema.RootElement.Clone(),
                    Annotations = new Dictionary<string, object?>
                    {
                        ["categories"] = new object[] { "travel", "research" },
                    },
                },
            }
        );

        var controller = new ToolsController(NullLogger<ToolsController>.Instance, registry);

        var result = await controller.GetToolsByCategory("finance");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var tools = ok.Value.Should().BeAssignableTo<IEnumerable<Tool>>().Subject.ToArray();
        tools.Select(tool => tool.Name).Should().Equal("finance_budget");
    }
}
