using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Server;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Transport.Stdio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Transport;

public class StdioTransportTests
{
    [Fact]
    public async Task StdioIngressHost_ProcessesFragmentedNewlineDelimitedRequest()
    {
        var (server, transport, host, inputPipe, outputPipe, cts) = CreateServerAndHost();

        server.RegisterTool(
            "echo",
            "Echo tool",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { message = new { type = "string" } } }),
            args =>
            {
                var message = args?.GetProperty("message").GetString();
                return Task.FromResult(new ToolCallResult
                {
                    Content = new[] { new TextContent { Text = message ?? string.Empty } },
                });
            }
        );

        var hostTask = host.RunAsync(cts.Token);

        const string rawMessage =
            "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"tools/call\",\"params\":{\"name\":\"echo\",\"arguments\":{\"message\":\"split\"}}}\n";
        byte[] payload = Encoding.UTF8.GetBytes(rawMessage);

        await inputPipe.Writer.WriteAsync(payload.AsMemory(0, 20));
        await inputPipe.Writer.FlushAsync();

        await inputPipe.Writer.WriteAsync(payload.AsMemory(20));
        await inputPipe.Writer.FlushAsync();

        var response = await ReadResponseAsync(outputPipe.Reader);
        response.Id.Should().Be("1");
        response.Error.Should().BeNull();

        var result = JsonSerializer.SerializeToElement(response.Result!);
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("split");

        cts.Cancel();
        await hostTask;
        await CleanupAsync(server, transport, inputPipe, outputPipe);
    }

    [Fact]
    public async Task StdioIngressHost_ProcessesMultipleMessagesInSingleRead()
    {
        var (server, transport, host, inputPipe, outputPipe, cts) = CreateServerAndHost();

        server.RegisterTool(
            "noop",
            "No-op tool",
            JsonSerializer.SerializeToElement(new { }),
            _ => Task.FromResult(new ToolCallResult { Content = Array.Empty<ContentBase>() })
        );

        var hostTask = host.RunAsync(cts.Token);

        const string message1 = "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"tools/list\"}";
        const string message2 = "{\"jsonrpc\":\"2.0\",\"id\":\"2\",\"method\":\"tools/list\"}";
        string combined = $"{message1}\n{message2}\r\n";

        await inputPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(combined));
        await inputPipe.Writer.FlushAsync();

        var first = await ReadResponseAsync(outputPipe.Reader);
        var second = await ReadResponseAsync(outputPipe.Reader);

        first.Id.Should().Be("1");
        second.Id.Should().Be("2");
        first.Error.Should().BeNull();
        second.Error.Should().BeNull();

        cts.Cancel();
        await hostTask;
        await CleanupAsync(server, transport, inputPipe, outputPipe);
    }

    [Fact]
    public async Task StdioIngressHost_DelaysProcessingUntilNewlineArrives()
    {
        var (server, transport, host, inputPipe, outputPipe, cts) = CreateServerAndHost();

        server.RegisterTool(
            "noop",
            "No-op tool",
            JsonSerializer.SerializeToElement(new { }),
            _ => Task.FromResult(new ToolCallResult { Content = Array.Empty<ContentBase>() })
        );

        var hostTask = host.RunAsync(cts.Token);

        const string payload = "{\"jsonrpc\":\"2.0\",\"id\":\"42\",\"method\":\"tools/list\"}";
        await inputPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(payload));
        await inputPipe.Writer.FlushAsync();

        await Task.Delay(150);

        outputPipe.Reader.TryRead(out var pending).Should().BeFalse("Ingress should not process without newline terminator");

        await inputPipe.Writer.WriteAsync(new byte[] { (byte)'\n' });
        await inputPipe.Writer.FlushAsync();

        var response = await ReadResponseAsync(outputPipe.Reader);
        response.Id.Should().Be("42");
        response.Error.Should().BeNull();

        cts.Cancel();
        await hostTask;
        await CleanupAsync(server, transport, inputPipe, outputPipe);
    }

    private static async Task CleanupAsync(
        McpServer server,
        StdioTransport transport,
        Pipe inputPipe,
        Pipe outputPipe
    )
    {
        await transport.CloseAsync();
        await inputPipe.Writer.CompleteAsync();
        await outputPipe.Reader.CompleteAsync();
        (server as IDisposable)?.Dispose();
    }

    private static async Task<JsonRpcResponseMessage> ReadResponseAsync(PipeReader reader)
    {
        var parser = new JsonRpcMessageParser();

        while (true)
        {
            var result = await reader.ReadAsync();
            var buffer = result.Buffer;
            try
            {
                if (buffer.IsEmpty && result.IsCompleted)
                {
                    throw new InvalidOperationException("Stream completed without response");
                }

                if (TryReadLine(ref buffer, out var line))
                {
                    var json = Encoding.UTF8.GetString(line.ToArray());
                    return parser.DeserializeResponse(json);
                }
            }
            finally
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var position = buffer.PositionOf((byte)'\n');
        if (position == null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private static (McpServer Server, StdioTransport Transport, StdioIngressHost Host, Pipe Input, Pipe Output, CancellationTokenSource Cts) CreateServerAndHost()
    {
        var inputPipe = new Pipe();
        var outputPipe = new Pipe();

        var serverOptions = new ServerOptions
        {
            Capabilities = new Mcp.Net.Core.Models.Capabilities.ServerCapabilities
            {
                Tools = new { },
                Resources = new { },
                Prompts = new { },
            },
        };

        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0.0" },
            new InMemoryConnectionManager(NullLoggerFactory.Instance),
            serverOptions,
            NullLoggerFactory.Instance
        );

        var transport = new StdioTransport(
            "test-stdio",
            inputPipe.Reader.AsStream(),
            outputPipe.Writer.AsStream(),
            NullLogger<StdioTransport>.Instance
        );

        var host = new StdioIngressHost(
            server,
            transport,
            NullLogger<StdioIngressHost>.Instance
        );

        var cts = new CancellationTokenSource();
        return (server, transport, host, inputPipe, outputPipe, cts);
    }
}
