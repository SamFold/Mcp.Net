using System;
using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.Controllers;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Infrastructure.Services;
using Mcp.Net.WebUi.LLM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.WebUi.Controllers;

public class ChatControllerTests
{
    [Fact]
    public async Task SendMessage_ShouldAcceptUserMessageRequestDtoAndForwardContent()
    {
        var repository = new Mock<IChatRepository>();
        repository
            .Setup(r => r.GetTranscriptEntriesAsync("session-1"))
            .ReturnsAsync(
                new ChatTranscriptEntry[]
                {
                    new UserChatEntry("user-0", DateTimeOffset.UtcNow.AddMinutes(-1), "earlier"),
                }
            );

        var adapter = new Mock<ISignalRChatAdapter>();
        var adapterManager = new Mock<IChatAdapterManager>();
        adapterManager
            .Setup(m => m.GetOrCreateAdapterAsync("session-1", It.IsAny<Func<string, Task<ISignalRChatAdapter>>>() ))
            .ReturnsAsync(adapter.Object);

        var controller = new ChatController(
            NullLogger<ChatController>.Instance,
            repository.Object,
            new Mock<IChatFactory>().Object,
            new Mock<ITitleGenerationService>().Object,
            adapterManager.Object
        );

        var result = await controller.SendMessage(
            "session-1",
            new UserMessageRequestDto { Content = "hello" }
        );

        result.Should().BeOfType<OkResult>();
        adapter.Verify(a => a.ProcessUserInput("hello"), Times.Once);
    }

    [Fact]
    public async Task SendMessage_WithEmptyContent_ShouldReturnBadRequest()
    {
        var controller = new ChatController(
            NullLogger<ChatController>.Instance,
            new Mock<IChatRepository>().Object,
            new Mock<IChatFactory>().Object,
            new Mock<ITitleGenerationService>().Object,
            new Mock<IChatAdapterManager>().Object
        );

        var result = await controller.SendMessage(
            "session-1",
            new UserMessageRequestDto { Content = "   " }
        );

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
