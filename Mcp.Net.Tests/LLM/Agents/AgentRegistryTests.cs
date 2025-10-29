using Mcp.Net.LLM.Agents;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Net.Tests.LLM.Agents;

/// <summary>
/// Unit tests for the AgentRegistry class
/// </summary>
public class AgentRegistryTests
{
    private readonly Mock<IAgentStore> _mockStore;
    private readonly Mock<ILogger<AgentRegistry>> _mockLogger;
    private readonly AgentRegistry _registry;

    public AgentRegistryTests()
    {
        _mockStore = new Mock<IAgentStore>();
        _mockLogger = new Mock<ILogger<AgentRegistry>>();

        // Setup the mock store
        _mockStore.Setup(s => s.SaveAgentAsync(It.IsAny<AgentDefinition>())).ReturnsAsync(true);
        _mockStore.Setup(s => s.DeleteAgentAsync(It.IsAny<string>())).ReturnsAsync(true);
        _mockStore.Setup(s => s.ListAgentsAsync()).ReturnsAsync(new List<AgentDefinition>());

        _registry = new AgentRegistry(_mockStore.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task RegisterAgentAsync_ShouldRequireUserId()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Name = "Test Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _registry.RegisterAgentAsync(agent, "")
        );
    }

    [Fact]
    public async Task RegisterAgentAsync_ShouldSetCreatorAndModifier()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Name = "Test Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
        };
        var userId = "test-user-123";

        // Need to capture the saved agent to verify its properties
        AgentDefinition? savedAgent = null;
        _mockStore
            .Setup(s => s.SaveAgentAsync(It.IsAny<AgentDefinition>()))
            .Callback<AgentDefinition>(a => savedAgent = a)
            .ReturnsAsync(true);

        // Act
        var result = await _registry.RegisterAgentAsync(agent, userId);

        // Assert
        Assert.True(result);
        Assert.NotNull(savedAgent);
        Assert.Equal(userId, savedAgent!.CreatedBy);
        Assert.Equal(userId, savedAgent.ModifiedBy);
        Assert.Equal(agent.Id, savedAgent.Id);
    }

    [Fact]
    public async Task UpdateAgentAsync_ShouldRequireUserId()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Id = "agent-123",
            Name = "Test Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _registry.UpdateAgentAsync(agent, "")
        );
    }

    [Fact]
    public async Task UpdateAgentAsync_ShouldReturnFalseWhenAgentNotFound()
    {
        // Arrange
        var agent = new AgentDefinition
        {
            Id = "non-existent-agent",
            Name = "Test Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
        };
        var userId = "test-user-456";

        // Set up the mock to return null for GetAgentByIdAsync
        _mockStore.Setup(s => s.GetAgentByIdAsync(agent.Id)).ReturnsAsync((AgentDefinition?)null);

        // Act
        var result = await _registry.UpdateAgentAsync(agent, userId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateAgentAsync_ShouldPreserveCreationMetadata()
    {
        // Arrange
        // IMPORTANT: Need to set up the cache with the original agent first
        var originalAgent = new AgentDefinition
        {
            Id = "agent-789",
            Name = "Original Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            CreatedBy = "original-creator",
            CreatedAt = DateTime.UtcNow.AddDays(-7), // Created a week ago
        };

        // Setup the agent store to have the original agent
        _mockStore
            .Setup(s => s.ListAgentsAsync())
            .ReturnsAsync(new List<AgentDefinition> { originalAgent });
        await _registry.ReloadAgentsAsync(); // This will load the original agent into the cache

        var updatedAgent = new AgentDefinition
        {
            Id = "agent-789",
            Name = "Updated Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5-mini",
        };

        var modifierUserId = "test-modifier-789";

        // Need to capture the saved agent to verify its properties
        AgentDefinition? savedAgent = null;
        _mockStore
            .Setup(s => s.SaveAgentAsync(It.IsAny<AgentDefinition>()))
            .Callback<AgentDefinition>(a => savedAgent = a)
            .ReturnsAsync(true);

        // Act
        var result = await _registry.UpdateAgentAsync(updatedAgent, modifierUserId);

        // Assert
        Assert.True(result);
        Assert.NotNull(savedAgent);
        Assert.Equal(originalAgent.CreatedBy, savedAgent!.CreatedBy); // Original creator preserved
        Assert.Equal(originalAgent.CreatedAt, savedAgent.CreatedAt); // Original creation date preserved
        Assert.Equal(modifierUserId, savedAgent.ModifiedBy); // Modifier updated
        Assert.NotEqual(originalAgent.UpdatedAt, savedAgent.UpdatedAt); // Update date changed
    }

    [Fact]
    public async Task UpdateAgentAsync_ShouldRaiseAgentUpdatedEvent()
    {
        // Arrange
        // IMPORTANT: Need to set up the cache with the original agent first
        var originalAgent = new AgentDefinition
        {
            Id = "agent-event-test",
            Name = "Event Test Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
        };

        // Setup the agent store to have the original agent
        _mockStore
            .Setup(s => s.ListAgentsAsync())
            .ReturnsAsync(new List<AgentDefinition> { originalAgent });
        await _registry.ReloadAgentsAsync(); // This will load the original agent into the cache

        var updatedAgent = new AgentDefinition
        {
            Id = "agent-event-test",
            Name = "Updated Event Test Agent",
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
        };
        var userId = "test-event-user";

        bool eventRaised = false;
        AgentDefinition? eventAgent = null;

        _registry.AgentUpdated += (sender, args) =>
        {
            eventRaised = true;
            eventAgent = args;
        };

        // Act
        var result = await _registry.UpdateAgentAsync(updatedAgent, userId);

        // Assert
        Assert.True(result);
        Assert.True(eventRaised);
        Assert.Equal(updatedAgent.Id, eventAgent?.Id);
        Assert.Equal(updatedAgent.Name, eventAgent?.Name);
    }

    [Fact]
    public async Task GetAllAgentsAsync_ShouldReturnAllAgents()
    {
        // Arrange
        var agents = new List<AgentDefinition>
        {
            new AgentDefinition { Id = "agent1", Name = "Test Agent 1" },
            new AgentDefinition { Id = "agent2", Name = "Test Agent 2" },
        };
        _mockStore.Setup(s => s.ListAgentsAsync()).ReturnsAsync(agents);

        // Force registry to reload from store
        await _registry.ReloadAgentsAsync();

        // Act
        var result = await _registry.GetAllAgentsAsync();

        // Assert
        Assert.Equal(agents.Count, result.Count());
        Assert.All(agents, a => Assert.Contains(result, r => r.Id == a.Id));
    }

    [Fact]
    public async Task GetAgentByIdAsync_ShouldReturnCorrectAgent()
    {
        // Arrange
        var agents = new List<AgentDefinition>
        {
            new AgentDefinition { Id = "agent1", Name = "Test Agent 1" },
            new AgentDefinition { Id = "agent2", Name = "Test Agent 2" },
        };
        _mockStore.Setup(s => s.ListAgentsAsync()).ReturnsAsync(agents);

        // Force registry to reload from store
        await _registry.ReloadAgentsAsync();

        // Act
        var result = await _registry.GetAgentByIdAsync("agent1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("agent1", result!.Id);
        Assert.Equal("Test Agent 1", result.Name);
    }

    [Fact]
    public async Task GetAgentsByCategoryAsync_ShouldReturnAgentsInCategory()
    {
        // Arrange
        var agents = new List<AgentDefinition>
        {
            new AgentDefinition
            {
                Id = "agent1",
                Name = "Test Agent 1",
                Category = AgentCategory.General,
            },
            new AgentDefinition
            {
                Id = "agent2",
                Name = "Test Agent 2",
                Category = AgentCategory.Math,
            },
            new AgentDefinition
            {
                Id = "agent3",
                Name = "Test Agent 3",
                Category = AgentCategory.Math,
            },
        };
        _mockStore.Setup(s => s.ListAgentsAsync()).ReturnsAsync(agents);

        // Force registry to reload from store
        await _registry.ReloadAgentsAsync();

        // Act
        var result = await _registry.GetAgentsByCategoryAsync(AgentCategory.Math);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.All(result, a => Assert.Equal(AgentCategory.Math, a.Category));
    }
}
