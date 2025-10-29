using Mcp.Net.LLM.Agents;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Net.Tests.LLM.Agents;

/// <summary>
/// Unit tests for the AgentManager class
/// </summary>
public class AgentManagerTests
{
    private readonly Mock<IAgentRegistry> _mockRegistry;
    private readonly Mock<IAgentFactory> _mockFactory;
    private readonly Mock<ILogger<AgentManager>> _mockLogger;
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly AgentManager _agentManager;

    // Test data
    private readonly AgentDefinition _testAgent = new()
    {
        Id = "test-agent-id",
        Name = "Test Agent",
        Description = "Test agent description",
        Provider = LlmProvider.OpenAI,
        ModelName = "gpt-5",
        SystemPrompt = "You are a helpful assistant",
        Category = AgentCategory.General,
        ToolIds = new List<string> { "tool1", "tool2" },
        Parameters = new Dictionary<string, object> { { "temperature", 0.7f } },
        CreatedBy = "user1",
        ModifiedBy = "user1",
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        UpdatedAt = DateTime.UtcNow.AddDays(-1),
    };

    public AgentManagerTests()
    {
        _mockRegistry = new Mock<IAgentRegistry>();
        _mockFactory = new Mock<IAgentFactory>();
        _mockLogger = new Mock<ILogger<AgentManager>>();
        _mockChatClient = new Mock<IChatClient>();

        _agentManager = new AgentManager(
            _mockRegistry.Object,
            _mockFactory.Object,
            _mockLogger.Object
        );

        // Common setup
        _mockRegistry.Setup(r => r.GetAgentByIdAsync(_testAgent.Id)).ReturnsAsync(_testAgent);
    }

    [Fact]
    public async Task CreateChatClientAsync_ShouldCreateClientFromAgent_WhenAgentExists()
    {
        // Arrange
        _mockFactory
            .Setup(f => f.CreateClientFromAgentDefinitionAsync(_testAgent))
            .ReturnsAsync(_mockChatClient.Object);

        // Act
        var result = await _agentManager.CreateChatClientAsync(_testAgent.Id);

        // Assert
        Assert.Same(_mockChatClient.Object, result);
        _mockRegistry.Verify(r => r.GetAgentByIdAsync(_testAgent.Id), Times.Once);
        _mockFactory.Verify(f => f.CreateClientFromAgentDefinitionAsync(_testAgent), Times.Once);
    }

    [Fact]
    public async Task CreateChatClientAsync_ShouldPassUserId_WhenProvided()
    {
        // Arrange
        var userId = "test-user-id";
        _mockFactory
            .Setup(f => f.CreateClientFromAgentDefinitionAsync(_testAgent, userId))
            .ReturnsAsync(_mockChatClient.Object);

        // Act
        var result = await _agentManager.CreateChatClientAsync(_testAgent.Id, userId);

        // Assert
        Assert.Same(_mockChatClient.Object, result);
        _mockFactory.Verify(
            f => f.CreateClientFromAgentDefinitionAsync(_testAgent, userId),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateChatClientAsync_ShouldThrowKeyNotFoundException_WhenAgentNotFound()
    {
        // Arrange
        var nonExistentId = "non-existent-id";
        _mockRegistry
            .Setup(r => r.GetAgentByIdAsync(nonExistentId))
            .ReturnsAsync((AgentDefinition?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _agentManager.CreateChatClientAsync(nonExistentId)
        );

        Assert.Contains(nonExistentId, exception.Message);
    }

    [Fact]
    public async Task GetAgentByIdAsync_ShouldReturnAgent_WhenExists()
    {
        // Act
        var result = await _agentManager.GetAgentByIdAsync(_testAgent.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testAgent.Id, result.Id);
        _mockRegistry.Verify(r => r.GetAgentByIdAsync(_testAgent.Id), Times.Once);
    }

    [Fact]
    public async Task GetAgentByIdAsync_ShouldReturnNull_WhenAgentNotFound()
    {
        // Arrange
        var nonExistentId = "non-existent-id";
        _mockRegistry
            .Setup(r => r.GetAgentByIdAsync(nonExistentId))
            .ReturnsAsync((AgentDefinition?)null);

        // Act
        var result = await _agentManager.GetAgentByIdAsync(nonExistentId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAgentsAsync_ShouldReturnAllAgents_WhenNoCategoryProvided()
    {
        // Arrange
        var agents = new List<AgentDefinition>
        {
            new() { Id = "agent1", Name = "Agent 1" },
            new() { Id = "agent2", Name = "Agent 2" },
        };

        _mockRegistry.Setup(r => r.GetAllAgentsAsync()).ReturnsAsync(agents);

        // Act
        var result = await _agentManager.GetAgentsAsync();

        // Assert
        Assert.Equal(2, result.Count());
        _mockRegistry.Verify(r => r.GetAllAgentsAsync(), Times.Once);
        _mockRegistry.Verify(
            r => r.GetAgentsByCategoryAsync(It.IsAny<AgentCategory>()),
            Times.Never
        );
    }

    [Fact]
    public async Task GetAgentsAsync_ShouldFilterByCategory_WhenCategoryProvided()
    {
        // Arrange
        var category = AgentCategory.Research;
        var agents = new List<AgentDefinition>
        {
            new()
            {
                Id = "agent1",
                Name = "Research Agent",
                Category = category,
            },
        };

        _mockRegistry.Setup(r => r.GetAgentsByCategoryAsync(category)).ReturnsAsync(agents);

        // Act
        var result = await _agentManager.GetAgentsAsync(category);

        // Assert
        Assert.Single(result);
        Assert.All(result, a => Assert.Equal(category, a.Category));
        _mockRegistry.Verify(r => r.GetAgentsByCategoryAsync(category), Times.Once);
        _mockRegistry.Verify(r => r.GetAllAgentsAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldValidateName()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            // Missing name
            ModelName = "gpt-5",
            Provider = LlmProvider.OpenAI,
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _agentManager.CreateAgentAsync(agent, "user1")
        );

        Assert.Contains("name", exception.Message, StringComparison.OrdinalIgnoreCase);
        _mockRegistry.Verify(
            r => r.RegisterAgentAsync(It.IsAny<AgentDefinition>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldValidateModelName()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Name = "Test Agent",
            // Missing model name
            Provider = LlmProvider.OpenAI,
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _agentManager.CreateAgentAsync(agent, "user1")
        );

        Assert.Contains("model", exception.Message, StringComparison.OrdinalIgnoreCase);
        _mockRegistry.Verify(
            r => r.RegisterAgentAsync(It.IsAny<AgentDefinition>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldSetTimestamps()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Name = "New Agent",
            ModelName = "gpt-5",
            Provider = LlmProvider.OpenAI,
        };

        var initialTime = DateTime.UtcNow.AddDays(-10); // Old time that should be updated
        agent.CreatedAt = initialTime;
        agent.UpdatedAt = initialTime;

        var userId = "user123";

        _mockRegistry
            .Setup(r => r.RegisterAgentAsync(It.IsAny<AgentDefinition>(), userId))
            .ReturnsAsync(true);

        // Act
        var result = await _agentManager.CreateAgentAsync(agent, userId);

        // Assert
        Assert.NotEqual(initialTime, result.CreatedAt);
        Assert.NotEqual(initialTime, result.UpdatedAt);
        Assert.True(result.CreatedAt >= DateTime.UtcNow.AddMinutes(-1));
        Assert.True(result.UpdatedAt >= DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldThrowWhenRegistrationFails()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Name = "Rejected Agent",
            ModelName = "gpt-5",
            Provider = LlmProvider.OpenAI,
        };

        var userId = "user123";

        _mockRegistry
            .Setup(r => r.RegisterAgentAsync(It.IsAny<AgentDefinition>(), userId))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _agentManager.CreateAgentAsync(agent, userId)
        );
    }

    [Fact]
    public async Task UpdateAgentAsync_ShouldReturnTrue_WhenUpdateSucceeds()
    {
        // Arrange
        var updatedAgent = new AgentDefinition
        {
            Id = _testAgent.Id,
            Name = "Updated Agent",
            ModelName = "gpt-5-mini",
            Provider = LlmProvider.OpenAI,
        };

        var userId = "user456";

        _mockRegistry.Setup(r => r.UpdateAgentAsync(updatedAgent, userId)).ReturnsAsync(true);

        // Act
        var result = await _agentManager.UpdateAgentAsync(updatedAgent, userId);

        // Assert
        Assert.True(result);
        _mockRegistry.Verify(r => r.UpdateAgentAsync(updatedAgent, userId), Times.Once);
    }

    [Fact]
    public async Task UpdateAgentAsync_ShouldReturnFalse_WhenAgentNotFound()
    {
        // Arrange
        var updatedAgent = new AgentDefinition
        {
            Id = "non-existent-id",
            Name = "Non-existent Agent",
            ModelName = "gpt-5",
            Provider = LlmProvider.OpenAI,
        };

        var userId = "user456";

        _mockRegistry.Setup(r => r.UpdateAgentAsync(updatedAgent, userId)).ReturnsAsync(false);

        // Act
        var result = await _agentManager.UpdateAgentAsync(updatedAgent, userId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAgentAsync_ShouldCallRegistry_AndReturnResult()
    {
        // Arrange
        var agentId = "agent-to-delete";
        _mockRegistry.Setup(r => r.UnregisterAgentAsync(agentId)).ReturnsAsync(true);

        // Act
        var result = await _agentManager.DeleteAgentAsync(agentId);

        // Assert
        Assert.True(result);
        _mockRegistry.Verify(r => r.UnregisterAgentAsync(agentId), Times.Once);
    }

    [Fact]
    public async Task CloneAgentAsync_ShouldThrowKeyNotFoundException_WhenSourceAgentNotFound()
    {
        // Arrange
        var sourceAgentId = "non-existent-id";
        _mockRegistry
            .Setup(r => r.GetAgentByIdAsync(sourceAgentId))
            .ReturnsAsync((AgentDefinition?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _agentManager.CloneAgentAsync(sourceAgentId, "user123")
        );
    }

    [Fact]
    public async Task CloneAgentAsync_ShouldCreateProperCloneWithNewId()
    {
        // Arrange
        var sourceAgentId = _testAgent.Id;
        var userId = "user789";
        var customName = "Custom Clone Name";

        AgentDefinition? capturedAgent = null;
        _mockRegistry
            .Setup(r => r.RegisterAgentAsync(It.IsAny<AgentDefinition>(), userId))
            .ReturnsAsync(true)
            .Callback<AgentDefinition, string>((agent, _) => capturedAgent = agent);

        // Act
        var result = await _agentManager.CloneAgentAsync(sourceAgentId, userId, customName);

        // Assert
        Assert.NotEqual(sourceAgentId, result.Id);
        Assert.Equal(customName, result.Name);
        Assert.Contains(_testAgent.Name, result.Description);
        Assert.Contains(_testAgent.Id, result.Description);
        Assert.Equal(_testAgent.Provider, result.Provider);
        Assert.Equal(_testAgent.ModelName, result.ModelName);
        Assert.Equal(_testAgent.Category, result.Category);
        Assert.Equal(_testAgent.ToolIds?.Count ?? 0, result.ToolIds?.Count ?? 0);

        _mockRegistry.Verify(
            r => r.RegisterAgentAsync(It.IsAny<AgentDefinition>(), userId),
            Times.Once
        );
    }

    [Fact]
    public async Task CloneAgentAsync_ShouldUseDefaultName_WhenNoNameProvided()
    {
        // Arrange
        var sourceAgentId = _testAgent.Id;
        var userId = "user789";

        // Make a clone for the mock to return
        var clonedAgent = new AgentDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = _testAgent.Name,
            Description = _testAgent.Description,
            Provider = _testAgent.Provider,
            ModelName = _testAgent.ModelName,
            SystemPrompt = _testAgent.SystemPrompt,
            Category = _testAgent.Category,
            ToolIds = new List<string>(_testAgent.ToolIds),
            Parameters = new Dictionary<string, object>(_testAgent.Parameters),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _mockRegistry.Setup(r => r.GetAgentByIdAsync(sourceAgentId)).ReturnsAsync(_testAgent);

        _mockRegistry
            .Setup(r => r.RegisterAgentAsync(It.IsAny<AgentDefinition>(), userId))
            .ReturnsAsync(true)
            .Callback<AgentDefinition, string>(
                (agent, _) =>
                {
                    // Update the clonedAgent reference to match what would be registered
                    clonedAgent = agent;
                }
            );

        // Act
        var result = await _agentManager.CloneAgentAsync(sourceAgentId, userId);

        // Assert
        Assert.StartsWith("Copy of", result.Name);
        Assert.Contains(_testAgent.Name, result.Name);
    }

    [Fact]
    public async Task GetToolCategoriesAsync_ShouldDelegateToFactory()
    {
        // Arrange
        var categories = new[] { "math", "search", "general" };
        _mockFactory.Setup(f => f.GetToolCategoriesAsync()).ReturnsAsync(categories);

        // Act
        var result = await _agentManager.GetToolCategoriesAsync();

        // Assert
        Assert.Equal(categories, result);
        _mockFactory.Verify(f => f.GetToolCategoriesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetToolsByCategoryAsync_ShouldDelegateToFactory()
    {
        // Arrange
        var category = "math";
        var toolIds = new[] { "calculator_add", "calculator_subtract" };
        _mockFactory.Setup(f => f.GetToolsByCategoryAsync(category)).ReturnsAsync(toolIds);

        // Act
        var result = await _agentManager.GetToolsByCategoryAsync(category);

        // Assert
        Assert.Equal(toolIds, result);
        _mockFactory.Verify(f => f.GetToolsByCategoryAsync(category), Times.Once);
    }
}
