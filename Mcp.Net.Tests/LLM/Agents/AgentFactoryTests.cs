using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Agents;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Models.Exceptions;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Net.Tests.LLM.Agents;

/// <summary>
/// Unit tests for the AgentFactory class
/// </summary>
public class AgentFactoryTests
{
    private readonly Mock<IAgentRegistry> _mockRegistry;
    private readonly Mock<IChatClientFactory> _mockChatClientFactory;
    private readonly Mock<IToolRegistry> _mockToolRegistry;
    private readonly Mock<IApiKeyProvider> _mockApiKeyProvider;
    private readonly Mock<ILogger<AgentFactory>> _mockLogger;
    private readonly AgentFactory _factory;

    // Test tools
    private readonly List<Tool> _testTools = new()
    {
        new Tool
        {
            Name = "calculator_add",
            Description = "Adds two numbers",
            InputSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement,
        },
        new Tool
        {
            Name = "calculator_subtract",
            Description = "Subtracts two numbers",
            InputSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement,
        },
        new Tool
        {
            Name = "google_search",
            Description = "Searches the web",
            InputSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement,
        },
    };

    public AgentFactoryTests()
    {
        _mockRegistry = new Mock<IAgentRegistry>();
        _mockChatClientFactory = new Mock<IChatClientFactory>();

        _mockToolRegistry = new Mock<IToolRegistry>();

        _mockApiKeyProvider = new Mock<IApiKeyProvider>();
        _mockLogger = new Mock<ILogger<AgentFactory>>();

        // Set up the tool registry mock
        _mockToolRegistry.Setup(tr => tr.AllTools).Returns(_testTools);

        // Setup tool categories
        var mathToolIds = new[] { "calculator_add", "calculator_subtract" };
        var searchToolIds = new[] { "google_search" };
        var allCategories = new[] { "math", "search", "utility", "code" };

        _mockToolRegistry.Setup(tr => tr.GetToolCategoriesAsync()).ReturnsAsync(allCategories);

        _mockToolRegistry.Setup(tr => tr.GetToolsByCategoryAsync("math")).ReturnsAsync(mathToolIds);

        _mockToolRegistry
            .Setup(tr => tr.GetToolsByCategoryAsync("search"))
            .ReturnsAsync(searchToolIds);

        // Set up the API key provider mock
        _mockApiKeyProvider
            .Setup(p => p.GetApiKeyAsync(It.IsAny<LlmProvider>()))
            .ReturnsAsync("test-api-key");

        _factory = new AgentFactory(
            _mockRegistry.Object,
            _mockChatClientFactory.Object,
            _mockToolRegistry.Object,
            _mockApiKeyProvider.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task CreateAgentAsync_WithProviderAndModel_ShouldCreateWithDefaults()
    {
        // Arrange
        var provider = LlmProvider.OpenAI;
        var modelName = "gpt-5";
        var userId = "test-user-123";

        // Act
        var result = await _factory.CreateAgentAsync(provider, modelName, userId);

        // Assert
        Assert.Equal(provider, result.Provider);
        Assert.Equal(modelName, result.ModelName);
        Assert.NotEmpty(result.SystemPrompt);
        Assert.Empty(result.ToolIds);
        Assert.Equal(AgentCategory.Uncategorized, result.Category);
        Assert.NotNull(result.Parameters);
        Assert.True(result.Parameters.ContainsKey("temperature"));
        Assert.Equal(userId, result.CreatedBy);
        Assert.Equal(userId, result.ModifiedBy);
    }

    [Fact]
    public async Task CreateAgentAsync_WithSystemPrompt_ShouldUseProvidedPrompt()
    {
        // Arrange
        var provider = LlmProvider.Anthropic;
        var modelName = "claude-3-sonnet";
        var systemPrompt = "You are a specialized math assistant.";
        var userId = "test-user-456";

        // Act
        var result = await _factory.CreateAgentAsync(provider, modelName, systemPrompt, userId);

        // Assert
        Assert.Equal(provider, result.Provider);
        Assert.Equal(modelName, result.ModelName);
        Assert.Equal(systemPrompt, result.SystemPrompt);
        Assert.Equal(userId, result.CreatedBy);
    }

    [Fact]
    public async Task CreateAgentAsync_WithTools_ShouldAddSpecifiedTools()
    {
        // Arrange
        var provider = LlmProvider.OpenAI;
        var modelName = "gpt-5";
        var systemPrompt = "You are a math assistant.";
        var toolIds = new[] { "calculator_add", "calculator_subtract" };
        var userId = "test-user-789";

        // Act
        var result = await _factory.CreateAgentAsync(provider, modelName, systemPrompt, toolIds, userId);

        // Assert
        Assert.Equal(toolIds.Length, result.ToolIds.Count);
        Assert.All(toolIds, id => Assert.Contains(id, result.ToolIds));
        Assert.Equal(AgentCategory.Uncategorized, result.Category); // Should use Uncategorized by default
        Assert.Equal(userId, result.CreatedBy);
    }

    [Fact]
    public async Task CreateAgentAsync_WithCategory_ShouldUseSpecifiedCategory()
    {
        // Arrange
        var provider = LlmProvider.OpenAI;
        var modelName = "gpt-5";
        var systemPrompt = "You are a math assistant.";
        var toolIds = new[] { "calculator_add", "calculator_subtract" };
        var category = AgentCategory.Math;
        var userId = "test-user-101";

        // Act
        var result = await _factory.CreateAgentAsync(
            provider,
            modelName,
            systemPrompt,
            toolIds,
            category,
            userId
        );

        // Assert
        Assert.Equal(toolIds.Length, result.ToolIds.Count);
        Assert.Equal(category, result.Category); // Should use specified category
        Assert.Equal(userId, result.CreatedBy);
        Assert.Equal(userId, result.ModifiedBy);
    }

    [Fact]
    public async Task CreateClientFromAgentDefinitionAsync_ShouldConfigureClient()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            SystemPrompt = "Test prompt",
            ToolIds = new List<string> { "calculator_add" },
            Parameters = new Dictionary<string, object> { { "temperature", 0.5f } },
        };

        var mockChatClient = new Mock<IChatClient>();
        _mockChatClientFactory
            .Setup(f => f.Create(It.IsAny<LlmProvider>(), It.IsAny<ChatClientOptions>()))
            .Returns(mockChatClient.Object);

        // Act
        var result = await _factory.CreateClientFromAgentDefinitionAsync(agent);

        // Assert
        Assert.Same(mockChatClient.Object, result);
        _mockChatClientFactory.Verify(
            f =>
                f.Create(
                    It.Is<LlmProvider>(p => p == agent.Provider),
                    It.Is<ChatClientOptions>(o =>
                        o.Model == agent.ModelName && o.SystemPrompt == agent.SystemPrompt
                    )
                ),
            Times.Once
        );

        mockChatClient.Verify(
            c =>
                c.RegisterTools(
                    It.Is<IEnumerable<Tool>>(tools => tools.Any(t => t.Name == "calculator_add"))
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateClientFromAgentAsync_WhenAgentNotFound_ShouldThrowException()
    {
        // Arrange
        var agentId = "non-existent-id";
        _mockRegistry.Setup(r => r.GetAgentByIdAsync(agentId)).ReturnsAsync((AgentDefinition?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _factory.CreateClientFromAgentAsync(agentId)
        );
    }

    [Fact]
    public async Task GetToolsByCategoryAsync_ShouldReturnCategoryTools()
    {
        // Arrange
        var category = "math";

        // Act
        var result = await _factory.GetToolsByCategoryAsync(category);

        // Assert
        Assert.NotEmpty(result);
        Assert.All(result, id => Assert.StartsWith("calculator_", id));
    }

    [Fact]
    public async Task GetToolCategoriesAsync_ShouldReturnAllCategories()
    {
        // Act
        var result = await _factory.GetToolCategoriesAsync();

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("math", result);
        Assert.Contains("search", result);
    }

    [Fact]
    public async Task CreateAgentWithToolCategoriesAsync_ShouldIncludeAllCategoryTools()
    {
        // Arrange
        var provider = LlmProvider.OpenAI;
        var modelName = "gpt-5";
        var systemPrompt = "Test prompt";
        var categories = new[] { "math", "search" };
        var userId = "test-user-202";

        // Act
        var result = await _factory.CreateAgentWithToolCategoriesAsync(
            provider,
            modelName,
            systemPrompt,
            categories,
            userId
        );

        // Assert
        Assert.Contains("calculator_add", result.ToolIds);
        Assert.Contains("calculator_subtract", result.ToolIds);
        Assert.Contains("google_search", result.ToolIds);
        Assert.Equal(AgentCategory.Uncategorized, result.Category); // Should default to Uncategorized
        Assert.Equal(userId, result.CreatedBy);
    }

    [Fact]
    public async Task CreateAgentWithToolCategoriesAndAgentCategory_ShouldUseSpecifiedCategory()
    {
        // Arrange
        var provider = LlmProvider.OpenAI;
        var modelName = "gpt-5";
        var systemPrompt = "Test prompt";
        var categories = new[] { "math", "search" };
        var agentCategory = AgentCategory.Specialist;
        var userId = "test-user-303";

        // Act
        var result = await _factory.CreateAgentWithToolCategoriesAsync(
            provider,
            modelName,
            systemPrompt,
            categories,
            agentCategory,
            userId
        );

        // Assert
        Assert.Contains("calculator_add", result.ToolIds);
        Assert.Contains("calculator_subtract", result.ToolIds);
        Assert.Contains("google_search", result.ToolIds);
        Assert.Equal(agentCategory, result.Category); // Should use the specified category
        Assert.Equal(userId, result.CreatedBy);
        Assert.Equal(userId, result.ModifiedBy);
    }

    [Fact]
    public async Task CreateClientFromAgentDefinitionAsync_WhenApiKeyNotFound_ShouldThrowException()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            SystemPrompt = "Test prompt",
        };

        _mockApiKeyProvider
            .Setup(p => p.GetApiKeyAsync(LlmProvider.OpenAI))
            .ThrowsAsync(new KeyNotFoundException("API key not found"));

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _factory.CreateClientFromAgentDefinitionAsync(agent)
        );
    }

    [Fact]
    public async Task CreateClientFromAgentDefinitionAsync_WithMissingTools_ShouldThrowToolNotFoundException()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            SystemPrompt = "Test prompt",
            ToolIds = new List<string>
            {
                "calculator_add",
                "non_existent_tool",
                "another_missing_tool",
            },
        };

        var mockChatClient = new Mock<IChatClient>();
        _mockChatClientFactory
            .Setup(f => f.Create(It.IsAny<LlmProvider>(), It.IsAny<ChatClientOptions>()))
            .Returns(mockChatClient.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ToolNotFoundException>(
            () => _factory.CreateClientFromAgentDefinitionAsync(agent)
        );

        // Verify exception properties
        Assert.Equal(2, exception.MissingToolIds.Count);
        Assert.Contains("non_existent_tool", exception.MissingToolIds);
        Assert.Contains("another_missing_tool", exception.MissingToolIds);
        Assert.Contains("Could not find the following tools:", exception.Message);

        // Verify logging was called
        _mockLogger.Verify(
            l =>
                l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (v, t) => v.ToString()!.Contains("Could not find the following tools")
                    ),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateClientFromAgentDefinitionAsync_WithUserId_ShouldUseUserApiKey()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            SystemPrompt = "Test prompt",
        };

        var userId = "test-user-123";
        var userApiKey = "user-specific-api-key";

        var mockUserApiKeyProvider = new Mock<IUserApiKeyProvider>();
        mockUserApiKeyProvider
            .Setup(p => p.GetApiKeyForUserAsync(LlmProvider.OpenAI, userId))
            .ReturnsAsync(userApiKey);

        var factoryWithUserProvider = new AgentFactory(
            _mockRegistry.Object,
            _mockChatClientFactory.Object,
            _mockToolRegistry.Object,
            _mockApiKeyProvider.Object,
            _mockLogger.Object,
            mockUserApiKeyProvider.Object
        );

        var mockChatClient = new Mock<IChatClient>();
        _mockChatClientFactory
            .Setup(f => f.Create(It.IsAny<LlmProvider>(), It.IsAny<ChatClientOptions>()))
            .Returns(mockChatClient.Object);

        // Act
        await factoryWithUserProvider.CreateClientFromAgentDefinitionAsync(agent, userId);

        // Assert
        _mockChatClientFactory.Verify(
            f =>
                f.Create(
                    It.Is<LlmProvider>(p => p == agent.Provider),
                    It.Is<ChatClientOptions>(o => o.ApiKey == userApiKey)
                ),
            Times.Once
        );
    }
}
