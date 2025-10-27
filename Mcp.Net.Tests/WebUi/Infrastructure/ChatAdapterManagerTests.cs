using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.WebUi.Infrastructure;

public class ChatAdapterManagerTests
{
    [Fact]
    public async Task RemoveAdapterAsync_ShouldDisposeAdapter_AndReleaseSessionResources()
    {
        // Arrange
        var sessionId = "session-123";
        var adapterMock = new Mock<ISignalRChatAdapter>();
        var factoryMock = new Mock<IChatFactory>(MockBehavior.Strict);
        factoryMock.Setup(f => f.ReleaseSessionResources(sessionId));

        var manager = new ChatAdapterManager(
            NullLogger<ChatAdapterManager>.Instance,
            factoryMock.Object
        );

        await manager.GetOrCreateAdapterAsync(sessionId, _ => Task.FromResult(adapterMock.Object));

        // Act
        await manager.RemoveAdapterAsync(sessionId);

        // Assert
        adapterMock.Verify(a => a.Dispose(), Times.Once);
        factoryMock.Verify(f => f.ReleaseSessionResources(sessionId), Times.Once);

        manager.TryGetAdapter(sessionId, out _).Should().BeFalse("adapter should be removed");
    }
}
