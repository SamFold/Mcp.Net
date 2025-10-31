using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client.Transport;
using Mcp.Net.Core.JsonRpc;
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

        var transport = new StdioClientTransport(inputStream, outputStream, "", NullLogger.Instance);
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

        var transport = new StdioClientTransport(inputStream, outputStream, "", NullLogger.Instance)
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

        var transport = new StdioClientTransport(inputStream, outputStream, "", NullLogger.Instance)
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

    [Fact]
    public async Task ProcessMessagesAsync_ShouldRaiseNotificationEvents()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        await using var inputStream = serverToClient.Reader.AsStream();
        await using var outputStream = clientToServer.Writer.AsStream();

        var transport = new StdioClientTransport(inputStream, outputStream, "", NullLogger.Instance);
        await transport.StartAsync();

        var notificationTcs = new TaskCompletionSource<JsonRpcNotificationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnNotification += message => notificationTcs.TrySetResult(message);

        var notification = new JsonRpcNotificationMessage(
            "2.0",
            "notifications/progress",
            new { percentage = 42, message = "Half way there" }
        );
        var payload = JsonSerializer.Serialize(notification) + "\n";
        await serverToClient.Writer.WriteAsync(Encoding.UTF8.GetBytes(payload));
        await serverToClient.Writer.FlushAsync();

        var received = await notificationTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        received.Method.Should().Be("notifications/progress");

        await transport.CloseAsync();
    }

    [Fact]
    public async Task ProcessMessagesAsync_ShouldRaiseErrorOnInvalidJson()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        await using var inputStream = serverToClient.Reader.AsStream();
        await using var outputStream = clientToServer.Writer.AsStream();

        var transport = new StdioClientTransport(inputStream, outputStream, "", NullLogger.Instance);
        await transport.StartAsync();

        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnError += ex => errorTcs.TrySetResult(ex);

        const string invalidPayload = "{\"jsonrpc\":\"2.0\",\"result\":true\n"; // Missing closing brace and newline-delimited
        await serverToClient.Writer.WriteAsync(Encoding.UTF8.GetBytes(invalidPayload));
        await serverToClient.Writer.WriteAsync(Encoding.UTF8.GetBytes("\n"));
        await serverToClient.Writer.FlushAsync();

        var exception = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Invalid JSON message");

        await transport.CloseAsync();
    }

    [Fact]
    public async Task UpdateNegotiatedProtocolVersion_ShouldExposeProtocolForDiagnostics()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        await using var inputStream = serverToClient.Reader.AsStream();
        await using var outputStream = clientToServer.Writer.AsStream();

        var transport = new StdioClientTransport(inputStream, outputStream, "", NullLogger.Instance);
        await transport.StartAsync();

        var method = typeof(StdioClientTransport)
            .GetMethod("UpdateNegotiatedProtocolVersion", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(transport, new object?[] { "2025-06-18" });

        transport.NegotiatedProtocolVersion.Should().Be("2025-06-18");

        await transport.CloseAsync();
    }
}
