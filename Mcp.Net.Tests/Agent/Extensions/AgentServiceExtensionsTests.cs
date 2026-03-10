using Mcp.Net.Agent.Agents;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Extensions;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.Agent.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Net.Tests.Agent.Extensions;

/// <summary>
/// Tests for the agent-related service extensions
/// </summary>
public class AgentServiceExtensionsTests
{
    private readonly ServiceCollection _services;
    private readonly Mock<IAgentManager> _mockAgentManager;
    private readonly Mock<IAgentFactory> _mockAgentFactory;
    private readonly Mock<IMcpClient> _mockMcpClient;
    private readonly Mock<IToolRegistry> _mockToolRegistry;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<ChatSession>> _mockLogger;

    public AgentServiceExtensionsTests()
    {
        _services = new ServiceCollection();

        // Setup mocks
        _mockAgentManager = new Mock<IAgentManager>();
        _mockAgentFactory = new Mock<IAgentFactory>();
        _mockMcpClient = new Mock<IMcpClient>();
        _mockToolRegistry = new Mock<IToolRegistry>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<ChatSession>>();

        // Configure mocks
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_mockLogger.Object);

        // Register mocks in DI
        _services.AddSingleton(_mockAgentManager.Object);
        _services.AddSingleton(_mockAgentFactory.Object);
        _services.AddSingleton(_mockMcpClient.Object);
        _services.AddSingleton(_mockToolRegistry.Object);
        _services.AddSingleton(_mockLoggerFactory.Object);
    }

    [Fact]
    public void AddAgentServices_ShouldRegisterRequiredServices()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        // Register mock loggers
        serviceCollection.AddSingleton<ILoggerFactory>(_mockLoggerFactory.Object);
        serviceCollection.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Register required client factory
        var mockChatClientFactory = new Mock<IChatClientFactory>();
        serviceCollection.AddSingleton<IChatClientFactory>(mockChatClientFactory.Object);

        // Register a mock configuration for the API key provider
        var mockConfiguration = new Mock<IConfiguration>();
        serviceCollection.AddSingleton<IConfiguration>(mockConfiguration.Object);

        // Act
        serviceCollection.AddAgentServices();
        serviceCollection.AddInMemoryAgentStore();
        var provider = serviceCollection.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<IAgentManager>());
        Assert.NotNull(provider.GetService<IAgentRegistry>());
        Assert.NotNull(provider.GetService<IAgentFactory>());
        Assert.NotNull(provider.GetService<IAgentStore>());
        Assert.NotNull(provider.GetService<IApiKeyProvider>());
    }

    [Fact]
    public async Task CreateChatSessionFromAgentAsync_ShouldUseAgentManager()
    {
        // Arrange
        var agentId = "test-agent-id";
        var userId = "test-user-id";
        var testAgent = new AgentDefinition { Id = agentId, Name = "Test Agent" };
        var mockChatClient = new Mock<IChatClient>();

        // Configure mock agent manager
        _mockAgentManager.Setup(m => m.GetAgentByIdAsync(agentId)).ReturnsAsync(testAgent);
        _mockAgentManager
            .Setup(m => m.CreateChatClientAsync(agentId, userId))
            .ReturnsAsync(mockChatClient.Object);

        var provider = _services.BuildServiceProvider();

        // Act
        var session = await provider.CreateChatSessionFromAgentAsync(agentId, userId);

        // Assert
        Assert.NotNull(session);
        Assert.Same(testAgent, session.AgentDefinition);
        _mockAgentManager.Verify(m => m.GetAgentByIdAsync(agentId), Times.Once);
        _mockAgentManager.Verify(m => m.CreateChatClientAsync(agentId, userId), Times.Once);
    }

    [Fact]
    public async Task CreateChatSessionFromAgentDefinitionAsync_ShouldUseAgentFactory()
    {
        // Arrange
        var testAgent = new AgentDefinition { Id = "test-agent-id", Name = "Test Agent" };
        var userId = "test-user-id";
        var mockChatClient = new Mock<IChatClient>();

        // Configure mock agent factory
        _mockAgentFactory
            .Setup(f => f.CreateClientFromAgentDefinitionAsync(testAgent, userId))
            .ReturnsAsync(mockChatClient.Object);

        var provider = _services.BuildServiceProvider();

        // Act
        var session = await provider.CreateChatSessionFromAgentDefinitionAsync(testAgent, userId);

        // Assert
        Assert.NotNull(session);
        Assert.Same(testAgent, session.AgentDefinition);
        _mockAgentFactory.Verify(
            f => f.CreateClientFromAgentDefinitionAsync(testAgent, userId),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateChatSessionFromAgentAsync_ShouldThrowIfRequiredServicesMissing()
    {
        // Arrange
        var limitedServices = new ServiceCollection();
        // Only register some services, not all required ones
        limitedServices.AddSingleton(_mockMcpClient.Object);
        limitedServices.AddSingleton(_mockLoggerFactory.Object);
        var provider = limitedServices.BuildServiceProvider();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CreateChatSessionFromAgentAsync("agent-id")
        );
    }
}
