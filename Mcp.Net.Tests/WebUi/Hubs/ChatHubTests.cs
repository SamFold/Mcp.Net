using System;
using System.Collections.Concurrent;
using System.Threading;
using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.Infrastructure.Services;
using Mcp.Net.WebUi.LLM.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.WebUi.Hubs;

public class ChatHubTests
{
    [Fact]
    public async Task SubmitElicitationResponse_ShouldResolveAndMarkActive()
    {
        // Arrange
        var adapter = new Mock<ISignalRChatAdapter>();
        adapter
            .Setup(a => a.TryResolveElicitationAsync(It.IsAny<ElicitationResponseDto>()))
            .ReturnsAsync(true);

        var adapterManager = new Mock<IChatAdapterManager>();
        ISignalRChatAdapter adapterInstance = adapter.Object;
        adapterManager
            .Setup(m => m.TryGetAdapter("session-1", out adapterInstance))
            .Returns(true);

        var clients = CreateClients();
        var hub = CreateHub(adapterManager.Object);
        hub.Clients = clients;

        var response = new ElicitationResponseDto
        {
            RequestId = "req",
            Action = "accept",
        };

        // Act
        await hub.SubmitElicitationResponse("session-1", response);

        // Assert
        adapter.Verify(
            a => a.TryResolveElicitationAsync(It.Is<ElicitationResponseDto>(dto => dto == response)),
            Times.Once
        );
        adapterManager.Verify(m => m.MarkAdapterAsActive("session-1"), Times.Once);
    }

    [Fact]
    public async Task SubmitElicitationResponse_ShouldEmitError_WhenAdapterThrows()
    {
        // Arrange
        var adapter = new Mock<ISignalRChatAdapter>();
        adapter
            .Setup(a => a.TryResolveElicitationAsync(It.IsAny<ElicitationResponseDto>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var adapterManager = new Mock<IChatAdapterManager>();
        ISignalRChatAdapter errAdapter = adapter.Object;
        adapterManager
            .Setup(m => m.TryGetAdapter("session-err", out errAdapter))
            .Returns(true);

        var clientsProxy = new TestClientProxy();
        var clientsMock = new Mock<IHubCallerClients>();
        clientsMock.Setup(c => c.Caller).Returns(clientsProxy);

        var hub = CreateHub(adapterManager.Object);
        hub.Clients = clientsMock.Object;

        var response = new ElicitationResponseDto
        {
            RequestId = "req",
            Action = "accept",
        };

        // Act
        await hub.SubmitElicitationResponse("session-err", response);

        // Assert
        clientsProxy.Messages.Should().ContainSingle(m => m.Method == "ReceiveError");
        adapterManager.Verify(m => m.MarkAdapterAsActive(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitElicitationResponse_ShouldNoop_WhenAdapterMissing()
    {
        var adapterManager = new Mock<IChatAdapterManager>();
        adapterManager
            .Setup(m => m.TryGetAdapter("missing", out It.Ref<ISignalRChatAdapter>.IsAny))
            .Returns(false);

        var hub = CreateHub(adapterManager.Object);
        hub.Clients = CreateClients();

        await hub.SubmitElicitationResponse("missing", new ElicitationResponseDto());

        adapterManager.Verify(m => m.MarkAdapterAsActive(It.IsAny<string>()), Times.Never);
    }

    private static ChatHub CreateHub(IChatAdapterManager adapterManager)
    {
        var repository = new Mock<IChatRepository>().Object;
        var chatFactory = new Mock<IChatFactory>().Object;
        var titleService = new Mock<ITitleGenerationService>().Object;

        return new ChatHub(
            NullLogger<ChatHub>.Instance,
            repository,
            chatFactory,
            adapterManager,
            titleService
        );
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
