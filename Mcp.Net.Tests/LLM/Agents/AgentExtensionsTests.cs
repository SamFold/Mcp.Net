using Mcp.Net.LLM.Agents;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Tools;
using Moq;
using Xunit;

namespace Mcp.Net.Tests.LLM.Agents;

/// <summary>
/// Unit tests for AgentExtensions class
/// </summary>
public class AgentExtensionsTests
{
    private readonly AgentDefinition _testAgent;
    private readonly Mock<IAgentFactory> _mockFactory;
    private readonly Mock<IToolRegistry> _mockToolRegistry;

    public AgentExtensionsTests()
    {
        _testAgent = new AgentDefinition
        {
            Name = "Test Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            SystemPrompt = "Test system prompt",
            ToolIds = new List<string>(),
            Parameters = new Dictionary<string, object>(),
        };

        _mockFactory = new Mock<IAgentFactory>();
        _mockToolRegistry = new Mock<IToolRegistry>();
    }

    [Fact]
    public async Task WithToolsFromCategoryAsync_WithAgentFactory_ShouldAddCategoryTools()
    {
        // Arrange
        var category = "math";
        var categoryTools = new[] { "calculator_add", "calculator_subtract" };
        _mockFactory.Setup(f => f.GetToolsByCategoryAsync(category)).ReturnsAsync(categoryTools);

        // Act
        var result = await _testAgent.WithToolsFromCategoryAsync(category, _mockFactory.Object);

        // Assert
        Assert.Equal(_testAgent, result); // Should return the same instance
        Assert.Equal(categoryTools.Length, result.ToolIds.Count);
        Assert.All(categoryTools, id => Assert.Contains(id, result.ToolIds));
    }

    [Fact]
    public async Task WithToolsFromCategoryAsync_WithToolRegistry_ShouldAddCategoryTools()
    {
        // Arrange
        var category = "math";
        var categoryTools = new[] { "calculator_add", "calculator_subtract" };
        _mockToolRegistry
            .Setup(tr => tr.GetToolsByCategoryAsync(category))
            .ReturnsAsync(categoryTools);

        // Act
        var result = await _testAgent.WithToolsFromCategoryAsync(
            category,
            _mockToolRegistry.Object
        );

        // Assert
        Assert.Equal(_testAgent, result); // Should return the same instance
        Assert.Equal(categoryTools.Length, result.ToolIds.Count);
        Assert.All(categoryTools, id => Assert.Contains(id, result.ToolIds));
    }

    [Fact]
    public void WithTemperature_ShouldSetTemperatureParameter()
    {
        // Arrange
        var temperature = 0.85f;

        // Act
        var result = _testAgent.WithTemperature(temperature);

        // Assert
        Assert.Equal(_testAgent, result); // Should return the same instance
        Assert.True(result.Parameters.ContainsKey("temperature"));
        Assert.Equal(temperature, result.Parameters["temperature"]);
    }

    [Fact]
    public void WithTemperature_ShouldClampInvalidValues()
    {
        // Arrange - value too high
        var tooHigh = 2.0f;

        // Act
        var result1 = _testAgent.WithTemperature(tooHigh);

        // Assert
        Assert.Equal(1.0f, result1.Parameters["temperature"]);

        // Arrange - value too low
        var tooLow = -0.5f;

        // Act
        var result2 = _testAgent.WithTemperature(tooLow);

        // Assert
        Assert.Equal(0.0f, result2.Parameters["temperature"]);
    }

    [Fact]
    public void WithMaxTokens_ShouldSetMaxTokensParameter()
    {
        // Arrange
        var maxTokens = 4096;

        // Act
        var result = _testAgent.WithMaxTokens(maxTokens);

        // Assert
        Assert.Equal(_testAgent, result); // Should return the same instance
        Assert.True(result.Parameters.ContainsKey("max_tokens"));
        Assert.Equal(maxTokens, result.Parameters["max_tokens"]);
    }

    [Fact]
    public void WithMaxTokens_ShouldThrowForInvalidValues()
    {
        // Arrange
        var invalidTokens = -100;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _testAgent.WithMaxTokens(invalidTokens));
    }

    [Fact]
    public void WithDefaultParameters_ShouldSetProviderSpecificDefaults()
    {
        // Arrange - OpenAI agent
        var openAiAgent = new AgentDefinition
        {
            Provider = LlmProvider.OpenAI,
            Parameters = new Dictionary<string, object>(),
        };

        // Act
        var result1 = openAiAgent.WithDefaultParameters();

        // Assert
        Assert.True(result1.Parameters.ContainsKey("temperature"));
        Assert.True(result1.Parameters.ContainsKey("max_tokens"));
        Assert.True(result1.Parameters.ContainsKey("top_p"));

        // Arrange - Anthropic agent
        var anthropicAgent = new AgentDefinition
        {
            Provider = LlmProvider.Anthropic,
            Parameters = new Dictionary<string, object>(),
        };

        // Act
        var result2 = anthropicAgent.WithDefaultParameters();

        // Assert
        Assert.Equal(1.0f, result2.Parameters["temperature"]); // Anthropic uses different default
    }

    [Fact]
    public void Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var original = new AgentDefinition
        {
            Name = "Original Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            SystemPrompt = "Test system prompt",
            ToolIds = new List<string> { "tool1", "tool2" },
            Parameters = new Dictionary<string, object>
            {
                { "temperature", 0.7f },
                { "max_tokens", 2048 },
            },
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotEqual(original.Id, clone.Id); // Should have a new ID
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.Provider, clone.Provider);
        Assert.Equal(original.ModelName, clone.ModelName);
        Assert.Equal(original.SystemPrompt, clone.SystemPrompt);
        Assert.Equal(original.ToolIds.Count, clone.ToolIds.Count);
        Assert.Equal(original.Parameters.Count, clone.Parameters.Count);

        // Modify the clone and verify it doesn't affect original
        clone.Name = "Modified Clone";
        clone.ToolIds.Add("tool3");

        Assert.Equal("Original Agent", original.Name);
        Assert.Equal(2, original.ToolIds.Count);
    }

    [Fact]
    public void WithCategory_ShouldSetCategory()
    {
        // Arrange
        var category = AgentCategory.Math;

        // Act
        var result = _testAgent.WithCategory(category);

        // Assert
        Assert.Equal(_testAgent, result); // Should return the same instance
        Assert.Equal(category, result.Category);
    }

    [Fact]
    public void WithNameAndDescription_ShouldSetCustomValues()
    {
        // Arrange
        var name = "Custom Name";
        var description = "Custom Description";

        // Act
        var result = _testAgent.WithNameAndDescription(name, description);

        // Assert
        Assert.Equal(name, result.Name);
        Assert.Equal(description, result.Description);
    }

    [Fact]
    public void WithNameAndDescription_ShouldGenerateDefaultsWhenNotProvided()
    {
        // Arrange
        _testAgent.Category = AgentCategory.Math;
        _testAgent.ToolIds = new List<string> { "tool1", "tool2" };

        // Act
        var result = _testAgent.WithNameAndDescription();

        // Assert
        Assert.Contains("Math", result.Name);
        Assert.Contains("2 specialized tools", result.Description);
    }
}
