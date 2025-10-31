using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Server.Transport.Stdio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Transport;

public class StdioTransportTests
{
    [Fact]
    public async Task StdioTransport_ProcessesFragmentedNewlineDelimitedMessages()
    {
        var inputPipe = new Pipe();
        var outputPipe = new Pipe();
        await using var inputStream = inputPipe.Reader.AsStream();
        await using var outputStream = outputPipe.Writer.AsStream();

        var transport = new StdioTransport(
            "",
            inputStream,
            outputStream,
            NullLogger<StdioTransport>.Instance
        );

        var requestCompletion = new TaskCompletionSource<JsonRpcRequestMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        transport.OnRequest += message => requestCompletion.TrySetResult(message);

        await transport.StartAsync();

        const string rawMessage =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"echo\",\"arguments\":\"split😊\"}}\n";
        byte[] payload = Encoding.UTF8.GetBytes(rawMessage);

        // Write the message in two fragments to simulate partial reads
        await inputPipe.Writer.WriteAsync(payload.AsMemory(0, 20));
        await inputPipe.Writer.FlushAsync();

        await inputPipe.Writer.WriteAsync(payload.AsMemory(20));
        await inputPipe.Writer.FlushAsync();

        var request = await requestCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("tools/call", request.Method);
        Assert.NotNull(request.Params);

        await transport.CloseAsync();
        await inputPipe.Writer.CompleteAsync();
        await outputPipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task StdioTransport_ProcessesMultipleMessagesInSingleRead()
    {
        var inputPipe = new Pipe();
        var outputPipe = new Pipe();
        await using var inputStream = inputPipe.Reader.AsStream();
        await using var outputStream = outputPipe.Writer.AsStream();

        var transport = new StdioTransport(
            "",
            inputStream,
            outputStream,
            NullLogger<StdioTransport>.Instance
        );

        var firstMessage = new TaskCompletionSource<JsonRpcRequestMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var secondMessage = new TaskCompletionSource<JsonRpcRequestMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        int callIndex = 0;
        transport.OnRequest += message =>
        {
            int current = Interlocked.Increment(ref callIndex);
            if (current == 1)
            {
                firstMessage.TrySetResult(message);
            }
            else if (current == 2)
            {
                secondMessage.TrySetResult(message);
            }
        };

        await transport.StartAsync();

        const string message1 = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"first\"}";
        const string message2 = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"second\"}";

        // Second message uses CRLF to ensure carriage returns are trimmed.
        string combined = $"{message1}\n{message2}\r\n";
        await inputPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(combined));
        await inputPipe.Writer.FlushAsync();

        var first = await firstMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = await secondMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("first", first.Method);
        Assert.Equal("second", second.Method);

        await transport.CloseAsync();
        await inputPipe.Writer.CompleteAsync();
        await outputPipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task StdioTransport_DelaysProcessingUntilNewlineArrives()
    {
        var inputPipe = new Pipe();
        var outputPipe = new Pipe();
        await using var inputStream = inputPipe.Reader.AsStream();
        await using var outputStream = outputPipe.Writer.AsStream();

        var transport = new StdioTransport(
            "",
            inputStream,
            outputStream,
            NullLogger<StdioTransport>.Instance
        );

        var messageCompletion = new TaskCompletionSource<JsonRpcRequestMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        transport.OnRequest += message => messageCompletion.TrySetResult(message);

        await transport.StartAsync();

        const string payload = "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"delayed\"}";
        await inputPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(payload));
        await inputPipe.Writer.FlushAsync();

        await Task.Delay(150);
        Assert.False(
            messageCompletion.Task.IsCompleted,
            "Message should not be processed without terminating newline"
        );

        await inputPipe.Writer.WriteAsync(new byte[] { (byte)'\n' });
        await inputPipe.Writer.FlushAsync();

        var message = await messageCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("delayed", message.Method);

        await transport.CloseAsync();
        await inputPipe.Writer.CompleteAsync();
        await outputPipe.Reader.CompleteAsync();
    }
}
