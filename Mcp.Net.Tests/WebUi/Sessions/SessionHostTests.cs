using FluentAssertions;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Factories;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.Infrastructure.Persistence;
using Mcp.Net.WebUi.LLM;
using Mcp.Net.WebUi.LLM.Clients;
using Mcp.Net.WebUi.LLM.Factories;
using Mcp.Net.WebUi.LLM.Services;
using Mcp.Net.WebUi.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.WebUi.Sessions;

public class SessionHostTests
{
    [Fact]
    public async Task CreateAsync_ImageOnlyFirstUserMessage_ShouldNotTriggerTitleGeneration()
    {
        var llmClientProvider = new Mock<ILlmClientProvider>();
        llmClientProvider
            .Setup(provider => provider.Create(LlmProvider.OpenAI, "gpt-5.4"))
            .Returns(new StubChatClient(LlmProvider.OpenAI, new ChatClientOptions { Model = "gpt-5.4" }));

        var hubContext = CreateHubContext();
        var historyManager = new InMemoryChatHistoryManager(
            NullLogger<InMemoryChatHistoryManager>.Instance
        );
        var titleService = new Mock<ITitleGenerationService>();
        var sessionFactory = new ChatSessionFactory(NullLoggerFactory.Instance);

        using var host = new SessionHost(
            sessionFactory,
            llmClientProvider.Object,
            hubContext.Object,
            new ToolRegistry(),
            historyManager,
            new DefaultLlmSettings
            {
                Provider = LlmProvider.OpenAI,
                ModelName = "gpt-5.4",
                DefaultSystemPrompt = "You are helpful.",
            },
            [],
            titleService.Object,
            NullLogger<SessionHost>.Instance
        );

        var managed = await host.CreateAsync("session-1");

        var summary = await managed.ChatSession.SendUserMessageAsync(
            [new InlineImageUserContentPart(BinaryData.FromBytes([1, 2, 3, 4]), "image/png")]
        );

        summary.Completion.Should().Be(ChatTurnCompletion.Completed);
        titleService.Verify(
            service => service.GenerateTitleAsync(It.IsAny<string>()),
            Times.Never
        );
    }

    private static Mock<IHubContext<ChatHub>> CreateHubContext()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(proxy => proxy.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(clients => clients.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<ChatHub>>();
        hubContext.SetupGet(context => context.Clients).Returns(hubClients.Object);
        return hubContext;
    }
}
