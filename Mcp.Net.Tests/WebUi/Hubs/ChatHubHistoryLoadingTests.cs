using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.Infrastructure.Services;
using Mcp.Net.WebUi.LLM.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.WebUi.Hubs;

public class ChatHubHistoryLoadingTests
{
    [Fact]
    public async Task SendMessage_ShouldLoadPersistedTranscript_WhenCreatingAdapter()
    {
        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-2), "hello", "turn-1"),
            new AssistantChatEntry(
                "assistant-1",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                new AssistantContentBlock[] { new TextAssistantBlock("text-1", "hi") },
                "turn-1",
                "openai",
                "gpt-5"
            ),
        };

        var repository = new Mock<IChatRepository>();
        repository
            .Setup(r => r.GetChatMetadataAsync("session-history"))
            .ReturnsAsync(
                new ChatSessionMetadata
                {
                    Id = "session-history",
                    Model = "gpt-5",
                    Provider = LlmProvider.OpenAI,
                    SystemPrompt = "Be concise.",
                }
            );
        repository
            .Setup(r => r.GetTranscriptEntriesAsync("session-history"))
            .ReturnsAsync(transcript);

        var adapter = new Mock<ISignalRChatAdapter>();
        adapter
            .Setup(a => a.LoadHistoryAsync(
                It.Is<IReadOnlyList<ChatTranscriptEntry>>(entries => entries.Count == transcript.Length)
            ))
            .Returns(Task.CompletedTask);
        adapter.Setup(a => a.GetLlmClient()).Returns((IChatClient?)null);

        var chatFactory = new Mock<IChatFactory>();
        chatFactory
            .Setup(f => f.CreateSignalRAdapterAsync(
                "session-history",
                "gpt-5",
                "OpenAI",
                "Be concise."
            ))
            .ReturnsAsync(adapter.Object);

        var adapterManager = new Mock<IChatAdapterManager>();
        adapterManager.Setup(m => m.GetActiveSessions()).Returns(Array.Empty<string>());
        adapterManager
            .Setup(m => m.GetOrCreateAdapterAsync("session-history", It.IsAny<Func<string, Task<ISignalRChatAdapter>>>() ))
            .Returns((string sessionId, Func<string, Task<ISignalRChatAdapter>> factory) => factory(sessionId));

        var titleService = new Mock<ITitleGenerationService>();
        var hub = new ChatHub(
            NullLogger<ChatHub>.Instance,
            repository.Object,
            chatFactory.Object,
            adapterManager.Object,
            titleService.Object
        )
        {
            Clients = CreateClients(),
        };

        await hub.SendMessage("session-history", "continue");

        adapter.Verify(a => a.LoadHistoryAsync(It.IsAny<IReadOnlyList<ChatTranscriptEntry>>()), Times.Once);
        adapter.Verify(a => a.Start(), Times.Once);
        adapter.Verify(a => a.ProcessUserInput("continue"), Times.Once);
        titleService.Verify(s => s.GenerateTitleAsync(It.IsAny<string>()), Times.Never);
    }

    private static IHubCallerClients CreateClients()
    {
        var clientsProxy = new TestClientProxy();
        var clientsMock = new Mock<IHubCallerClients>();
        clientsMock.Setup(c => c.Caller).Returns(clientsProxy);
        return clientsMock.Object;
    }

    private sealed class TestClientProxy : ISingleClientProxy
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

        public Task<T> InvokeCoreAsync<T>(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }
    }
}
