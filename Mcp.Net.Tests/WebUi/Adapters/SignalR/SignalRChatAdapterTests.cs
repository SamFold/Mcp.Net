using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.Agent.Tools;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Mcp.Net.Tests.WebUi.Adapters.SignalR;

public class SignalRChatAdapterTests
{
    [Fact]
    public async Task SendUserMessageAsync_StreamingAssistantUpdate_ShouldBroadcastUpdateMessage()
    {
        var llmClient = new Mock<IChatClient>();
        var hubContext = CreateHubContext(out var clientProxy);
        var toolRegistry = new ToolRegistry();
        var catalog = new Mock<IPromptResourceCatalog>();
        var completionService = new Mock<ICompletionService>();

        llmClient
            .Setup(c => c.SendAsync(
                It.Is<ChatClientRequest>(request =>
                    request.Transcript.OfType<UserChatEntry>().Single().Content == "Hi there"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                (
                    ChatClientRequest request,
                    CancellationToken cancellationToken
                ) =>
                {
                    request.Transcript.OfType<UserChatEntry>().Single().Content.Should().Be("Hi there");
                    cancellationToken.Should().Be(CancellationToken.None);
                    return ChatCompletionStream.FromStreaming(
                        [
                            new ChatClientAssistantTurn(
                                "assistant-1",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[]
                                {
                                    new TextAssistantBlock("text-1", "Hel"),
                                }
                            ),
                        ],
                        new ChatClientAssistantTurn(
                            "assistant-1",
                            "openai",
                            "gpt-5",
                            new AssistantContentBlock[]
                            {
                                new TextAssistantBlock("text-1", "Hello"),
                            }
                        )
                    );
                }
            );

        var session = new ChatSession(
            llmClient.Object,
            Mock.Of<IToolExecutor>(),
            NullLogger<ChatSession>.Instance
        );
        using var adapter = new SignalRChatAdapter(
            session,
            hubContext.Object,
            NullLogger<SignalRChatAdapter>.Instance,
            "session-1",
            toolRegistry,
            catalog.Object,
            completionService.Object
        );

        var messageEvents = new List<ChatMessageEventArgs>();
        adapter.MessageReceived += (_, args) => messageEvents.Add(args);

        await session.SendUserMessageAsync("Hi there");

        clientProxy.Messages.Should().Contain(message => message.Method == "ReceiveMessage");
        clientProxy.Messages.Should().Contain(message => message.Method == "UpdateMessage");
        messageEvents.Should().Contain(args => args.ChangeKind == ChatTranscriptChangeKind.Updated);
    }

    private static Mock<IHubContext<ChatHub>> CreateHubContext(out TestClientProxy clientProxy)
    {
        clientProxy = new TestClientProxy();

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group("session-1")).Returns(clientProxy);

        var hubContext = new Mock<IHubContext<ChatHub>>();
        hubContext.SetupGet(c => c.Clients).Returns(clients.Object);
        return hubContext;
    }

    private sealed class TestClientProxy : IClientProxy
    {
        public ConcurrentQueue<(string Method, object?[] Args)> Messages { get; } = new();

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default
        )
        {
            Messages.Enqueue((method, args));
            return Task.CompletedTask;
        }
    }
}
