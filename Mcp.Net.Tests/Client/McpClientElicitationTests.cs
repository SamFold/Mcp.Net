using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.Core.Models.Messages;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.Client;

public class McpClientElicitationTests
{
    [Fact]
    public async Task HandleElicitationRequest_ShouldReturnAcceptPayload()
    {
        var transport = TestClientTransport.CreateWithDefaultInitialize();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        var handler = new RecordingElicitationHandler(_ => ElicitationClientResponse.Accept(new { alias = "Voyager" }));
        client.SetElicitationHandler(handler);

        var request = CreateElicitationRequest("elicitation-1");
        var response = await transport.TriggerRequestAsync(request, TimeSpan.FromSeconds(2));

        response.Error.Should().BeNull();
        var payload = response.Result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        payload.Should().ContainKey("action").WhoseValue.Should().Be("accept");
        payload.Should().ContainKey("content");
        var content = payload["content"].Should().BeOfType<JsonElement>().Subject;
        content.GetProperty("alias").GetString().Should().Be("Voyager");
        handler.ReceivedContexts.Should().ContainSingle();
        var recorded = handler.ReceivedContexts.Single();
        recorded.Message.Should().Be("Provide display alias");
        recorded.RequestedSchema.Properties.Should().ContainKey("alias");
    }

    [Fact]
    public async Task HandleElicitationRequest_Decline_ShouldReturnDeclineWithoutContent()
    {
        var transport = TestClientTransport.CreateWithDefaultInitialize();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        var handler = new RecordingElicitationHandler(_ => ElicitationClientResponse.Decline());
        client.SetElicitationHandler(handler);

        var request = CreateElicitationRequest("elicitation-2");
        var response = await transport.TriggerRequestAsync(request, TimeSpan.FromSeconds(2));

        response.Error.Should().BeNull();
        var payload = response.Result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        payload.Should().ContainKey("action").WhoseValue.Should().Be("decline");
        payload.Should().NotContainKey("content");
        handler.ReceivedContexts.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleElicitationRequest_WithoutHandler_ShouldReturnMethodNotFound()
    {
        var transport = TestClientTransport.CreateWithDefaultInitialize();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        client.SetElicitationHandler(null);

        var request = CreateElicitationRequest("elicitation-3");
        var response = await transport.TriggerRequestAsync(request, TimeSpan.FromSeconds(2));

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601);
        response.Result.Should().BeNull();
    }

    [Fact]
    public async Task HandleElicitationRequest_WithInvalidPayload_ShouldReturnInvalidParams()
    {
        var transport = TestClientTransport.CreateWithDefaultInitialize();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        var handler = new RecordingElicitationHandler(_ => ElicitationClientResponse.Cancel());
        client.SetElicitationHandler(handler);

        var invalidRequest = new JsonRpcRequestMessage(
            "2.0",
            "elicitation-invalid",
            "elicitation/create",
            new { }
        );

        var response = await transport.TriggerRequestAsync(invalidRequest, TimeSpan.FromSeconds(2));

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602);
        handler.ReceivedContexts.Should().BeEmpty();
    }

    private static JsonRpcRequestMessage CreateElicitationRequest(string id)
    {
        var schema = new ElicitationSchema().AddProperty(
            "alias",
            ElicitationSchemaProperty.ForString(
                title: "Display Alias",
                description: "Nickname to display"
            ),
            required: true
        );

        return new JsonRpcRequestMessage(
            "2.0",
            id,
            "elicitation/create",
            new ElicitationCreateParams
            {
                Message = "Provide display alias",
                RequestedSchema = schema,
            }
        );
    }

    private sealed class TestMcpClient : McpClient
    {
        private readonly IClientTransport _transport;

        public TestMcpClient(IClientTransport transport)
            : base("TestClient", "1.0.0", NullLogger.Instance)
        {
            _transport = transport;
        }

        public override Task Initialize()
        {
            return InitializeProtocolAsync(_transport);
        }

        protected override Task<object> SendRequest(string method, object? parameters = null)
        {
            return _transport.SendRequestAsync(method, parameters);
        }

        protected override Task SendNotification(string method, object? parameters = null)
        {
            return _transport.SendNotificationAsync(method, parameters);
        }

        public override void Dispose()
        {
            _transport.Dispose();
        }
    }

    private sealed class RecordingElicitationHandler : IElicitationRequestHandler
    {
        private readonly Func<ElicitationRequestContext, ElicitationClientResponse> _responseFactory;

        public RecordingElicitationHandler(
            Func<ElicitationRequestContext, ElicitationClientResponse> responseFactory
        )
        {
            _responseFactory =
                responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        public List<ElicitationRequestContext> ReceivedContexts { get; } = new();

        public Task<ElicitationClientResponse> HandleAsync(
            ElicitationRequestContext context,
            CancellationToken cancellationToken = default
        )
        {
            ReceivedContexts.Add(context);
            return Task.FromResult(_responseFactory(context));
        }
    }

    private sealed class TestClientTransport : IClientTransport
    {
        private TaskCompletionSource<JsonRpcResponseMessage>? _pendingResponse;

        public static TestClientTransport CreateWithDefaultInitialize()
        {
            var transport = new TestClientTransport
            {
                ResponseToReturn = new InitializeResponse
                {
                    ProtocolVersion = McpClient.LatestProtocolVersion,
                    Capabilities = new ServerCapabilities(),
                    ServerInfo = new ServerInfo
                    {
                        Name = "Test Server",
                        Version = "1.0.0",
                    },
                },
            };
            return transport;
        }

        public event Action<JsonRpcRequestMessage>? OnRequest;
        public event Action<JsonRpcResponseMessage>? OnResponse
        {
            add { }
            remove { }
        }

        public event Action<JsonRpcNotificationMessage>? OnNotification
        {
            add { }
            remove { }
        }

        public event Action<Exception>? OnError
        {
            add { }
            remove { }
        }
        public event Action? OnClose;

        public object? ResponseToReturn { get; set; }
        public string? LastRequestMethod { get; private set; }
        public object? LastRequestParameters { get; private set; }
        public JsonRpcResponseMessage? LastResponse { get; private set; }

        public Task StartAsync() => Task.CompletedTask;

        public Task<object> SendRequestAsync(string method, object? parameters = null)
        {
            LastRequestMethod = method;
            LastRequestParameters = parameters;
            return Task.FromResult(ResponseToReturn ?? new object());
        }

        public Task SendNotificationAsync(string method, object? parameters = null)
        {
            return Task.CompletedTask;
        }

        public Task SendResponseAsync(JsonRpcResponseMessage message)
        {
            LastResponse = message;
            _pendingResponse?.TrySetResult(message);
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            OnClose?.Invoke();
            return Task.CompletedTask;
        }

        public async Task<JsonRpcResponseMessage> TriggerRequestAsync(
            JsonRpcRequestMessage request,
            TimeSpan timeout
        )
        {
            var tcs = new TaskCompletionSource<JsonRpcResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _pendingResponse = tcs;
            OnRequest?.Invoke(request);
            return await tcs.Task.WaitAsync(timeout);
        }

        public void Dispose()
        {
            OnClose?.Invoke();
        }

        public string Id()
        {
            throw new NotImplementedException();
        }
    }
}
