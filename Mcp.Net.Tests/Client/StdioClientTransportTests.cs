using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using System.Buffers;
using Mcp.Net.Client.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Client;

public class StdioClientTransportTests
{
    [Fact]
    public async Task SendRequestAsync_ShouldSendRequestAndReceiveResponse()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        await using var inputStream = serverToClient.Reader.AsStream();
        await using var outputStream = clientToServer.Writer.AsStream();

        var transport = new StdioClientTransport(inputStream, outputStream, NullLogger.Instance);
        await transport.StartAsync();

        var requestTask = transport.SendRequestAsync("tools/list", new { });

        var readResult = await clientToServer.Reader.ReadAsync();
        var bufferSequence = readResult.Buffer;
        var requestPayload = Encoding.UTF8.GetString(bufferSequence.ToArray());
        requestPayload.Should().EndWith("\n");

        var requestJson = requestPayload.TrimEnd('\n');
        using var requestDoc = JsonDocument.Parse(requestJson);
        var requestId = requestDoc.RootElement.GetProperty("id").GetString();
        requestId.Should().NotBeNull();

        clientToServer.Reader.AdvanceTo(readResult.Buffer.End);

        var responseJson = JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = requestId!,
                    result = new { ok = true },
                }
            )
            + "\n";
        var responseBytes = Encoding.UTF8.GetBytes(responseJson);

        // Write the response in two fragments to ensure fragmented delivery is handled.
        int midpoint = responseBytes.Length / 2;
        await serverToClient.Writer.WriteAsync(responseBytes.AsMemory(0, midpoint));
        await serverToClient.Writer.FlushAsync();
        await serverToClient.Writer.WriteAsync(responseBytes.AsMemory(midpoint));
        await serverToClient.Writer.FlushAsync();

        var result = await requestTask; // Should complete once the response arrives
        var resultElement = result.Should().BeOfType<JsonElement>().Subject;
        resultElement.GetProperty("ok").GetBoolean().Should().BeTrue();

        await transport.CloseAsync();
    }

    [Fact]
    public async Task SendRequestAsync_ShouldTimeoutWhenNoResponse()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        await using var inputStream = serverToClient.Reader.AsStream();
        await using var outputStream = clientToServer.Writer.AsStream();

        var transport = new StdioClientTransport(inputStream, outputStream, NullLogger.Instance)
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
        };
        await transport.StartAsync();

        await FluentActions.Invoking(() => transport.SendRequestAsync("tools/list", new { }))
            .Should()
            .ThrowAsync<TimeoutException>();

        await transport.CloseAsync();
    }

    [Fact]
    public async Task CloseAsync_ShouldCancelOutstandingRequests()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        await using var inputStream = serverToClient.Reader.AsStream();
        await using var outputStream = clientToServer.Writer.AsStream();

        var transport = new StdioClientTransport(inputStream, outputStream, NullLogger.Instance)
        {
            RequestTimeout = Timeout.InfiniteTimeSpan,
        };
        await transport.StartAsync();

        var pendingTask = transport.SendRequestAsync("tools/list", new { });

        await transport.CloseAsync();

        await FluentActions.Awaiting(() => pendingTask)
            .Should()
            .ThrowAsync<OperationCanceledException>();
    }
}
