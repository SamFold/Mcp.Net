using FluentAssertions;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Server.ConnectionManagers;

public class InMemoryConnectionManagerTests
{
    [Fact]
    public async Task RegisterTransportAsync_ShouldKeepReplacementTransport_WhenPreviousTransportCloses()
    {
        var manager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var originalTransport = new MockTransport("original");
        var replacementTransport = new MockTransport("replacement");

        await manager.RegisterTransportAsync("session-1", originalTransport);
        await manager.RegisterTransportAsync("session-1", replacementTransport);

        originalTransport.SimulateClose();

        var resolvedTransport = await manager.GetTransportAsync("session-1");
        resolvedTransport.Should().BeSameAs(replacementTransport);
    }

    [Fact]
    public async Task RemoveTransportAsync_WithExpectedTransport_ShouldNotRemoveReplacementTransport()
    {
        var manager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var originalTransport = new MockTransport("original");
        var replacementTransport = new MockTransport("replacement");

        await manager.RegisterTransportAsync("session-1", originalTransport);
        await manager.RegisterTransportAsync("session-1", replacementTransport);

        var removed = await manager.RemoveTransportAsync("session-1", originalTransport);

        removed.Should().BeFalse();

        var resolvedTransport = await manager.GetTransportAsync("session-1");
        resolvedTransport.Should().BeSameAs(replacementTransport);
    }

    [Fact]
    public async Task RemoveTransportAsync_WithExpectedTransport_ShouldRemoveCurrentTransport()
    {
        var manager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var transport = new MockTransport("current");

        await manager.RegisterTransportAsync("session-1", transport);

        var removed = await manager.RemoveTransportAsync("session-1", transport);

        removed.Should().BeTrue();

        var resolvedTransport = await manager.GetTransportAsync("session-1");
        resolvedTransport.Should().BeNull();
    }
}
