using FluentAssertions;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Transport.Stdio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Server;

public class StdioServerBuilderTests
{
    [Fact]
    public void BuildTransport_ShouldUseStableStdioSessionId()
    {
        var firstBuilder = new StdioServerBuilder(NullLoggerFactory.Instance);
        var secondBuilder = new StdioServerBuilder(NullLoggerFactory.Instance);

        var firstTransport = firstBuilder.BuildTransport();
        var secondTransport = secondBuilder.BuildTransport();

        firstTransport.Id().Should().Be(StdioTransport.DefaultSessionId);
        secondTransport.Id().Should().Be(StdioTransport.DefaultSessionId);
    }
}
