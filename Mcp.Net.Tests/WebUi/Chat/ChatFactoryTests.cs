using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.LLM.Elicitation;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.Chat.Factories;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.LLM.Factories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.WebUi.Chat;

public class ChatFactoryTests
{
    [Fact]
    public async Task ReleaseSessionResources_ShouldNullOutCoordinatorProvider()
    {
        // Arrange
        var factory = CreateChatFactory();
        var coordinators = GetCoordinatorDictionary(factory);
        var sessionId = "session-release";

        var coordinator = new ElicitationCoordinator(NullLogger<ElicitationCoordinator>.Instance);
        coordinator.SetProvider(new AcceptingProvider());
        coordinators[sessionId] = coordinator;

        var context = CreateSampleContext();
        var preRelease = await coordinator.HandleAsync(context, CancellationToken.None);
        preRelease.Action.Should().Be("accept");

        // Act
        factory.ReleaseSessionResources(sessionId);

        // Assert
        var postRelease = await coordinator.HandleAsync(context, CancellationToken.None);
        postRelease.Action.Should().Be("decline");
        coordinators.ContainsKey(sessionId).Should().BeFalse();
    }

    private static ChatFactory CreateChatFactory()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var chatFactoryLogger = loggerFactory.CreateLogger<ChatFactory>();
        var hubContext = new Mock<IHubContext<ChatHub>>().Object;
        var toolRegistry = new Mcp.Net.LLM.Tools.ToolRegistry(NullLogger.Instance);
        var llmFactory = new LlmClientFactory(
            loggerFactory.CreateLogger<Mcp.Net.LLM.Anthropic.AnthropicChatClient>(),
            loggerFactory.CreateLogger<Mcp.Net.LLM.OpenAI.OpenAiChatClient>(),
            loggerFactory.CreateLogger<LlmClientFactory>()
        );
        var defaultSettings = new DefaultLlmSettings();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:Url"] = "http://localhost:5000/",
            })
            .Build();

        return new ChatFactory(
            chatFactoryLogger,
            loggerFactory,
            hubContext,
            toolRegistry,
            llmFactory,
            defaultSettings,
            configuration
        );
    }

    private static ConcurrentDictionary<string, ElicitationCoordinator> GetCoordinatorDictionary(
        ChatFactory factory
    )
    {
        var field = typeof(ChatFactory).GetField(
            "_elicitationCoordinators",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        field.Should().NotBeNull();
        return (ConcurrentDictionary<string, ElicitationCoordinator>)field!.GetValue(factory)!;
    }

    private static ElicitationRequestContext CreateSampleContext()
    {
        var parameters = new
        {
            message = "Need more info",
            requestedSchema = new
            {
                type = "object",
                properties = new
                {
                    value = new { type = "string" },
                },
            },
        };

        var request = new JsonRpcRequestMessage(
            JsonRpc: "2.0",
            Id: "1",
            Method: "elicitation/create",
            Params: parameters,
            Meta: null
        );

        return new ElicitationRequestContext(request);
    }

    private sealed class AcceptingProvider : Mcp.Net.LLM.Interfaces.IElicitationPromptProvider
    {
        public Task<ElicitationClientResponse> PromptAsync(
            ElicitationRequestContext context,
            CancellationToken cancellationToken
        )
        {
            var payload = JsonSerializer.SerializeToElement(new { value = "granted" });
            return Task.FromResult(ElicitationClientResponse.Accept(payload));
        }
    }
}
