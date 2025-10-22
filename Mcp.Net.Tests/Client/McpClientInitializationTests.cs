using FluentAssertions;
using Mcp.Net.Client;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Messages;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.Client;

public class McpClientInitializationTests
{
    [Fact]
    public async Task Initialize_ShouldSendLatestProtocolVersionAndCapabilities()
    {
        var transport = new FakeClientTransport();
        var expectedCapabilities = new ServerCapabilities { Tools = new { listChanged = true } };
        transport.ResponseToReturn = new InitializeResponse
        {
            ProtocolVersion = McpClient.LatestProtocolVersion,
            Capabilities = expectedCapabilities,
            ServerInfo = new ServerInfo
            {
                Name = "Test Server",
                Version = "1.2.3",
                Title = "Test Server Title",
            },
            Instructions = "Follow the white rabbit.",
        };

        var client = new TestMcpClient(
            transport,
            clientName: "Test Client",
            clientVersion: "9.9.9",
            clientTitle: "Display Name"
        );

        await client.Initialize();

        transport.LastRequestMethod.Should().Be("initialize");
        var payload = transport.LastRequestPayload.Should().BeOfType<InitializeRequest>().Subject;
        payload.ProtocolVersion.Should().Be(McpClient.LatestProtocolVersion);
        payload.ClientInfo.Should().NotBeNull();
        payload.ClientInfo!.Name.Should().Be("Test Client");
        payload.ClientInfo.Version.Should().Be("9.9.9");
        payload.ClientInfo.Title.Should().Be("Display Name");
        payload.Capabilities.Should().NotBeNull();
        payload.Capabilities!.Roots.Should().NotBeNull();
        payload.Capabilities!.Roots!.ListChanged.Should().BeTrue();
        payload.Capabilities!.Sampling.Should().NotBeNull();
        payload.Capabilities!.Elicitation.Should().NotBeNull();

        transport.LastNotificationMethod.Should().Be("notifications/initialized");
        transport.LastNotificationParameters.Should().BeNull();

        client.NegotiatedProtocolVersion.Should().Be(McpClient.LatestProtocolVersion);
        client.ServerCapabilities.Should().BeSameAs(expectedCapabilities);
        client.ServerInfo.Should().NotBeNull();
        client.ServerInfo!.Name.Should().Be("Test Server");
        client.ServerInfo.Version.Should().Be("1.2.3");
        client.Instructions.Should().Be("Follow the white rabbit.");
    }

    [Fact]
    public async Task Initialize_ShouldDefaultTitle_WhenNotProvided()
    {
        var transport = new FakeClientTransport
        {
            ResponseToReturn = new InitializeResponse
            {
                ProtocolVersion = McpClient.LatestProtocolVersion,
                Capabilities = new ServerCapabilities(),
                ServerInfo = new ServerInfo { Name = "Server", Version = "1.0.0" },
            },
        };

        var client = new TestMcpClient(transport, clientName: "DefaultTitleClient");

        await client.Initialize();

        var payload = transport.LastRequestPayload.Should().BeOfType<InitializeRequest>().Subject;
        payload.ClientInfo.Should().NotBeNull();
        payload.ClientInfo!.Title.Should().Be("DefaultTitleClient");
    }

    [Fact]
    public async Task Initialize_ShouldThrow_WhenServerReturnsUnsupportedProtocolVersion()
    {
        var transport = new FakeClientTransport
        {
            ResponseToReturn = new InitializeResponse
            {
                ProtocolVersion = "1999-01-01",
                Capabilities = new ServerCapabilities(),
                ServerInfo = new ServerInfo { Name = "Server", Version = "1.0.0" },
            },
        };

        var client = new TestMcpClient(transport);

        var initialize = async () => await client.Initialize();

        await initialize
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*unsupported MCP protocol version*");
        transport.NotificationCount.Should().Be(0);
    }

    private sealed class TestMcpClient : McpClient
    {
        private readonly IClientTransport _transport;

        public TestMcpClient(
            IClientTransport transport,
            string clientName = "TestClient",
            string clientVersion = "1.0.0",
            string? clientTitle = null
        )
            : base(clientName, clientVersion, NullLogger.Instance, clientTitle)
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

    private sealed class FakeClientTransport : IClientTransport
    {
        public event Action<JsonRpcResponseMessage>? OnResponse
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
        public object? LastRequestPayload { get; private set; }
        public string? LastNotificationMethod { get; private set; }
        public object? LastNotificationParameters { get; private set; }
        public int NotificationCount { get; private set; }

        public Task StartAsync() => Task.CompletedTask;

        public Task<object> SendRequestAsync(string method, object? parameters = null)
        {
            LastRequestMethod = method;
            LastRequestPayload = parameters;
            return Task.FromResult(ResponseToReturn ?? new object());
        }

        public Task SendNotificationAsync(string method, object? parameters = null)
        {
            LastNotificationMethod = method;
            LastNotificationParameters = parameters;
            NotificationCount++;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            OnClose?.Invoke();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            OnClose?.Invoke();
        }
    }
}
