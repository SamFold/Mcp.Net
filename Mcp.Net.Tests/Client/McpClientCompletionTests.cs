using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Messages;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.Client;

public class McpClientCompletionTests
{
    [Fact]
    public async Task CompleteAsync_ShouldReturnCompletionValues()
    {
        var transport = TestClientTransport.CreateWithCompletionCapability();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        var expected = new CompletionCompleteResult
        {
            Completion = new CompletionValues
            {
                Values = new[] { "python", "pytorch" },
                Total = 2,
                HasMore = false,
            },
        };

        transport.QueueResponse(expected);

        var reference = new CompletionReference
        {
            Type = "ref/prompt",
            Name = "code_review",
        };
        var argument = new CompletionArgument
        {
            Name = "language",
            Value = "py",
        };

        var result = await client.CompleteAsync(reference, argument);

        transport.LastRequestMethod.Should().Be("completion/complete");
        transport.LastRequestParameters.Should().BeOfType<CompletionCompleteParams>();

        result.Values.Should().Equal("python", "pytorch");
        result.Total.Should().Be(2);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrow_WhenServerDoesNotAdvertiseCapability()
    {
        var transport = TestClientTransport.CreateWithoutCompletionCapability();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        var reference = new CompletionReference
        {
            Type = "ref/prompt",
            Name = "draft",
        };
        var argument = new CompletionArgument
        {
            Name = "topic",
            Value = "security",
        };

        await FluentActions
            .Awaiting(() => client.CompleteAsync(reference, argument))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*completion support*");
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrow_WhenReferenceNull()
    {
        var transport = TestClientTransport.CreateWithCompletionCapability();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        await FluentActions
            .Awaiting(() => client.CompleteAsync(null!, new CompletionArgument()))
            .Should()
            .ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrow_WhenArgumentNull()
    {
        var transport = TestClientTransport.CreateWithCompletionCapability();
        var client = new TestMcpClient(transport);
        await client.Initialize();

        var reference = new CompletionReference
        {
            Type = "ref/resource",
            Uri = "mcp://docs/sample",
        };

        await FluentActions
            .Awaiting(() => client.CompleteAsync(reference, null!))
            .Should()
            .ThrowAsync<ArgumentNullException>();
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

    private sealed class TestClientTransport : IClientTransport
    {
        private readonly Queue<object?> _responses = new();

        public static TestClientTransport CreateWithCompletionCapability()
        {
            var transport = new TestClientTransport();
            transport.QueueResponse(
                new InitializeResponse
                {
                    ProtocolVersion = McpClient.LatestProtocolVersion,
                    Capabilities = new ServerCapabilities
                    {
                        Completions = new { },
                    },
                    ServerInfo = new ServerInfo
                    {
                        Name = "Completion Server",
                        Version = "1.0.0",
                    },
                }
            );
            return transport;
        }

        public static TestClientTransport CreateWithoutCompletionCapability()
        {
            var transport = new TestClientTransport();
            transport.QueueResponse(
                new InitializeResponse
                {
                    ProtocolVersion = McpClient.LatestProtocolVersion,
                    Capabilities = new ServerCapabilities(),
                    ServerInfo = new ServerInfo
                    {
                        Name = "Basic Server",
                        Version = "1.0.0",
                    },
                }
            );
            return transport;
        }

        public string? LastRequestMethod { get; private set; }
        public object? LastRequestParameters { get; private set; }

        public event Action<JsonRpcRequestMessage>? OnRequest
        {
            add { }
            remove { }
        }
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

        public Task StartAsync() => Task.CompletedTask;

        public Task<object> SendRequestAsync(string method, object? parameters = null)
        {
            LastRequestMethod = method;
            LastRequestParameters = parameters;
            var result = _responses.Count > 0 ? _responses.Dequeue() : new object();
            return Task.FromResult(result ?? new object());
        }

        public Task SendNotificationAsync(string method, object? parameters = null) =>
            Task.CompletedTask;

        public Task SendResponseAsync(JsonRpcResponseMessage message) => Task.CompletedTask;

        public Task CloseAsync()
        {
            OnClose?.Invoke();
            return Task.CompletedTask;
        }

        public void QueueResponse(object? response)
        {
            _responses.Enqueue(response);
        }

        public void Dispose()
        {
            OnClose?.Invoke();
        }
    }
}
