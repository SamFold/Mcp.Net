using System;
using System.Collections;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq.Expressions;
using System.Reflection;
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
            (args, _) =>
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
            (_, _) => Task.FromResult(new ToolCallResult { Content = Array.Empty<ContentBase>() })
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
            (_, _) => Task.FromResult(new ToolCallResult { Content = Array.Empty<ContentBase>() })
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

    [Fact]
    public async Task StdioIngressHost_BackToBackInitializeAndInitializedNotification_Should_LeaveSessionReady_ForServerDrivenNotifications()
    {
        var (server, transport, host, inputPipe, outputPipe, cts) = CreateServerAndHost(
            capabilities: new ServerCapabilities
            {
                Tools = new { listChanged = true },
                Resources = new { },
                Prompts = new { },
            }
        );

        await server.ConnectAsync(transport);

        var initializeStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseInitialize = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        DelayInitializeHandler(server, initializeStarted, releaseInitialize.Task);

        var hostTask = host.RunAsync(cts.Token);

        try
        {
            var initializeRequest = new JsonRpcRequestMessage(
                "2.0",
                "init-1",
                "initialize",
                JsonSerializer.SerializeToElement(
                    new
                    {
                        clientInfo = new { name = "stdio-test", version = "1.0.0" },
                        capabilities = new { },
                        protocolVersion = McpServer.LatestProtocolVersion,
                    }
                )
            );
            var initializedNotification = new JsonRpcNotificationMessage(
                "2.0",
                "notifications/initialized",
                null
            );

            var payload = string.Join(
                "\n",
                JsonSerializer.Serialize(initializeRequest),
                JsonSerializer.Serialize(initializedNotification),
                string.Empty
            );

            await inputPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(payload));
            await inputPipe.Writer.FlushAsync();

            await initializeStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(500));

            // Keep initialize blocked long enough for the follow-on lifecycle notification
            // to be queued behind it on the same stdio stream.
            await Task.Delay(100);

            releaseInitialize.TrySetResult(true);

            var initializeResponse = await ReadResponseAsync(outputPipe.Reader);
            initializeResponse.Id.Should().Be("init-1");
            initializeResponse.Error.Should().BeNull();

            server.RegisterTool(
                "dynamic.tool",
                "Tool registered after stdio lifecycle handshake",
                JsonSerializer.SerializeToElement(new { type = "object" }),
                (_, _) => Task.FromResult(new ToolCallResult())
            );

            var listChangedNotification = await TryReadNotificationAsync(
                outputPipe.Reader,
                TimeSpan.FromMilliseconds(300)
            );

            listChangedNotification.Should().NotBeNull(
                "back-to-back stdio initialize and notifications/initialized should leave the session ready for later server-driven notifications"
            );
            listChangedNotification!.Method.Should().Be("notifications/tools/list_changed");
        }
        finally
        {
            cts.Cancel();
            await hostTask;
            await CleanupAsync(server, transport, inputPipe, outputPipe);
        }
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

    private static async Task<JsonRpcNotificationMessage?> TryReadNotificationAsync(
        PipeReader reader,
        TimeSpan timeout
    )
    {
        var parser = new JsonRpcMessageParser();
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(cts.Token);
                var buffer = result.Buffer;
                try
                {
                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        return null;
                    }

                    if (TryReadLine(ref buffer, out var line))
                    {
                        var json = Encoding.UTF8.GetString(line.ToArray());
                        return parser.DeserializeNotification(json);
                    }
                }
                finally
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
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

    private static (
        McpServer Server,
        StdioTransport Transport,
        StdioIngressHost Host,
        Pipe Input,
        Pipe Output,
        CancellationTokenSource Cts
    ) CreateServerAndHost(ServerCapabilities? capabilities = null)
    {
        var inputPipe = new Pipe();
        var outputPipe = new Pipe();

        var serverOptions = new ServerOptions
        {
            Capabilities =
                capabilities
                ?? new Mcp.Net.Core.Models.Capabilities.ServerCapabilities
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

    private static void DelayInitializeHandler(
        McpServer server,
        TaskCompletionSource<bool> initializeStarted,
        Task releaseInitialize
    )
    {
        var methodHandlersField = typeof(McpServer).GetField(
            "_methodHandlers",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        methodHandlersField.Should().NotBeNull();

        var methodHandlers = methodHandlersField!.GetValue(server).Should().BeAssignableTo<IDictionary>().Subject;
        var originalHandler = methodHandlers["initialize"].Should().BeAssignableTo<Delegate>().Subject;

        var contextType = typeof(McpServer).GetNestedType(
            "RequestExecutionContext",
            BindingFlags.NonPublic
        );
        contextType.Should().NotBeNull();

        var delayedHandlerType = typeof(Func<,,>).MakeGenericType(
            typeof(string),
            contextType!,
            typeof(Task<object>)
        );

        var jsonParams = Expression.Parameter(typeof(string), "jsonParams");
        var context = Expression.Parameter(contextType, "context");
        var delayedHandlerMethod = typeof(StdioTransportTests).GetMethod(
            nameof(InvokeDelayedInitializeHandlerAsync),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        delayedHandlerMethod.Should().NotBeNull();

        var delayedHandler = Expression
            .Lambda(
                delayedHandlerType,
                Expression.Call(
                    delayedHandlerMethod!,
                    jsonParams,
                    Expression.Convert(context, typeof(object)),
                    Expression.Constant(originalHandler, typeof(Delegate)),
                    Expression.Constant(initializeStarted),
                    Expression.Constant(releaseInitialize, typeof(Task))
                ),
                jsonParams,
                context
            )
            .Compile();

        methodHandlers["initialize"] = delayedHandler;
    }

    private static async Task<object> InvokeDelayedInitializeHandlerAsync(
        string jsonParams,
        object? context,
        Delegate originalHandler,
        TaskCompletionSource<bool> initializeStarted,
        Task releaseInitialize
    )
    {
        initializeStarted.TrySetResult(true);
        await releaseInitialize.ConfigureAwait(false);

        var result = originalHandler.DynamicInvoke(jsonParams, context);
        result.Should().BeAssignableTo<Task<object>>();
        return await ((Task<object>)result!).ConfigureAwait(false);
    }
}
