using Mcp.Net.Agent.Agents;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Extensions;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.Agent.Tools;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Net.Tests.Agent.Extensions;

/// <summary>
/// Tests for the agent-related service extensions
/// </summary>
public class AgentServiceExtensionsTests
{
    private readonly ServiceCollection _services;
    private readonly Mock<IAgentManager> _mockAgentManager;
    private readonly Mock<IAgentFactory> _mockAgentFactory;
    private readonly Mock<IToolExecutor> _mockToolExecutor;
    private readonly Mock<IToolRegistry> _mockToolRegistry;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<ChatSession>> _mockLogger;

    public AgentServiceExtensionsTests()
    {
        _services = new ServiceCollection();

        // Setup mocks
        _mockAgentManager = new Mock<IAgentManager>();
        _mockAgentFactory = new Mock<IAgentFactory>();
        _mockToolExecutor = new Mock<IToolExecutor>();
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
        _services.AddSingleton(_mockToolExecutor.Object);
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
        serviceCollection.AddSingleton(new Mock<Mcp.Net.Client.Interfaces.IMcpClient>().Object);

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
        Assert.NotNull(provider.GetService<IToolExecutor>());
    }

    [Fact]
    public async Task CreateChatSessionFromAgentAsync_ShouldUseAgentManager()
    {
        // Arrange
        var agentId = "test-agent-id";
        var userId = "test-user-id";
        var testAgent = new AgentDefinition
        {
            Id = agentId,
            Name = "Test Agent",
            SystemPrompt = "Be concise.",
            Parameters = new Dictionary<string, object>
            {
                ["temperature"] = 0.3f,
                ["max_tokens"] = 512,
            },
        };
        var mockChatClient = new Mock<IChatClient>();
        ChatClientRequest? capturedRequest = null;

        // Configure mock agent manager
        _mockAgentManager.Setup(m => m.GetAgentByIdAsync(agentId)).ReturnsAsync(testAgent);
        _mockAgentManager
            .Setup(m => m.CreateChatClientAsync(agentId, userId))
            .ReturnsAsync(mockChatClient.Object);
        _mockToolRegistry.SetupGet(r => r.EnabledTools).Returns(Array.Empty<Mcp.Net.Core.Models.Tools.Tool>());
        _mockToolRegistry.SetupGet(r => r.AllTools).Returns(Array.Empty<Mcp.Net.Core.Models.Tools.Tool>());
        mockChatClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatClientRequest, CancellationToken>((request, _) => capturedRequest = request)
            .Returns(
                new TestChatCompletionStream(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        Array.Empty<AssistantContentBlock>()
                    )
                )
            );

        var provider = _services.BuildServiceProvider();

        // Act
        var session = await provider.CreateChatSessionFromAgentAsync(agentId, userId);
        await session.SendUserMessageAsync("hello");

        // Assert
        Assert.NotNull(session);
        session.GetSystemPrompt().Should().Be("Be concise.");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Options!.Temperature.Should().Be(0.3f);
        capturedRequest.Options.MaxOutputTokens.Should().Be(512);
        _mockAgentManager.Verify(m => m.GetAgentByIdAsync(agentId), Times.Once);
        _mockAgentManager.Verify(m => m.CreateChatClientAsync(agentId, userId), Times.Once);
    }

    [Fact]
    public async Task CreateChatSessionFromAgentDefinitionAsync_ShouldUseAgentFactory()
    {
        // Arrange
        var testAgent = new AgentDefinition
        {
            Id = "test-agent-id",
            Name = "Test Agent",
            SystemPrompt = "Use tools carefully.",
        };
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
        session.GetSystemPrompt().Should().Be("Use tools carefully.");
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
        limitedServices.AddSingleton(_mockToolExecutor.Object);
        limitedServices.AddSingleton(_mockLoggerFactory.Object);
        var provider = limitedServices.BuildServiceProvider();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CreateChatSessionFromAgentAsync("agent-id")
        );
    }

    private sealed class TestChatCompletionStream(ChatClientTurnResult result) : IChatCompletionStream
    {
        public ValueTask<ChatClientTurnResult> GetResultAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(result);

        public async IAsyncEnumerator<ChatClientAssistantTurn> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
