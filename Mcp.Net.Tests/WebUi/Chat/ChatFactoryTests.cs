using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Client;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Agent.Elicitation;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.Authentication;
using Mcp.Net.WebUi.Chat.Factories;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.LLM.Clients;
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

    [Fact]
    public void CreateClientForSession_ShouldOnlyUseConstructionOptions()
    {
        var factory = CreateChatFactory();
        var client = InvokeCreateClientForSession(factory, "session-1", "gpt-5", "openai");
        var options = GetClientOptions(client);

        options.Model.Should().Be("gpt-5");
    }

    private static ChatFactory CreateChatFactory()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var chatFactoryLogger = loggerFactory.CreateLogger<ChatFactory>();
        var hubContext = new Mock<IHubContext<ChatHub>>().Object;
        var toolRegistry = new Mcp.Net.Agent.Tools.ToolRegistry(
            NullLogger<Mcp.Net.Agent.Tools.ToolRegistry>.Instance
        );
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
        var authConfigurator = new Mock<IMcpClientBuilderConfigurator>();
        authConfigurator
            .Setup(c => c.ConfigureAsync(It.IsAny<McpClientBuilder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ChatFactory(
            chatFactoryLogger,
            loggerFactory,
            hubContext,
            toolRegistry,
            llmFactory,
            defaultSettings,
            configuration,
            authConfigurator.Object,
            new ChatTranscriptReplayTransformer()
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

    private static StubChatClient InvokeCreateClientForSession(
        ChatFactory factory,
        string sessionId,
        string? model,
        string? provider
    )
    {
        var method = typeof(ChatFactory).GetMethod(
            "CreateClientForSession",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        method.Should().NotBeNull();

        return method!.Invoke(factory, new object?[] { sessionId, model, provider }).Should().BeOfType<StubChatClient>().Subject;
    }

    private static ChatClientOptions GetClientOptions(StubChatClient client)
    {
        var field = typeof(StubChatClient).GetField(
            "_options",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        field.Should().NotBeNull();
        return (ChatClientOptions)field!.GetValue(client)!;
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

    private sealed class AcceptingProvider : IElicitationPromptProvider
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
