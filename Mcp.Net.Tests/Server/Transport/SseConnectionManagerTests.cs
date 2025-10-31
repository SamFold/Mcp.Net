using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Server;
using Mcp.Net.Server.Transport.Sse;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Tests.Server.Transport;

public class SseConnectionManagerTests
{
    private const string DefaultOrigin = "http://localhost:5000";

    [Fact]
    public async Task HandleMessageAsync_Should_Use_Header_Session_Id()
    {
        // Arrange
        var (connectionManager, transport, writer) = CreateManagerWithTransport();

        // Verify session header exposed on SSE response
        writer
            .Headers.Should()
            .ContainKey("Mcp-Session-Id")
            .WhoseValue.Should()
            .Be(transport.SessionId);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "http";
        context.Request.Host = HostString.FromUriComponent(DefaultOrigin);
        context.Request.Headers["Mcp-Session-Id"] = transport.SessionId;
        context.Response.Body = new MemoryStream();

        var request = new JsonRpcRequestMessage("2.0", "list-1", "tools/list", null);
        var requestJson = JsonSerializer.Serialize(request);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.Body.Position = 0;
        context.Request.Headers["MCP-Protocol-Version"] = McpServer.LatestProtocolVersion;
        context.Request.Headers["Origin"] = DefaultOrigin;

        // Act
        await connectionManager.HandleMessageAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(202);
        context.Response.Headers["Mcp-Session-Id"].ToString().Should().Be(transport.SessionId);
        context.Response.Headers.ContainsKey("MCP-Protocol-Version").Should().BeFalse();
        context.Response.Body.Length.Should().Be(0);
        var dataPayloads = writer.WrittenPayloads.Where(p => p.StartsWith("data: ")).ToList();
        dataPayloads.Should().NotBeEmpty();
        var payload = dataPayloads.Single();
        payload.Should().StartWith("data: ");
        payload.Should().Contain("\"id\":\"list-1\"");
    }

    [Fact]
    public async Task HandleMessageAsync_Should_Set_Protocol_Header_After_Initialize()
    {
        var (connectionManager, transport, writer) = CreateManagerWithTransport();

        await SendInitializeAsync(connectionManager, transport, includeProtocolHeader: false);

        var initializeResponse = writer.WrittenPayloads.First(p => p.StartsWith("data: "));
        initializeResponse.Should().Contain("\"id\":\"init-1\"");

        var requestContext = CreatePostContext(
            transport,
            new JsonRpcRequestMessage("2.0", "list-2", "tools/list", null)
        );

        requestContext.Request.Headers["MCP-Protocol-Version"] = McpServer.LatestProtocolVersion;

        await connectionManager.HandleMessageAsync(requestContext);

        requestContext.Response.StatusCode.Should().Be(202);
        requestContext
            .Response.Headers["MCP-Protocol-Version"]
            .ToString()
            .Should()
            .Be(McpServer.LatestProtocolVersion);
        requestContext.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task HandleMessageAsync_Should_Return_400_When_Protocol_Header_Missing()
    {
        var (connectionManager, transport, _) = CreateManagerWithTransport();
        await SendInitializeAsync(connectionManager, transport, includeProtocolHeader: false);

        var requestContext = CreatePostContext(
            transport,
            new JsonRpcRequestMessage("2.0", "list-3", "tools/list", null)
        );

        await connectionManager.HandleMessageAsync(requestContext);

        requestContext.Response.StatusCode.Should().Be(400);
        requestContext.Response.Body.Position = 0;
        using var reader = new StreamReader(
            requestContext.Response.Body,
            Encoding.UTF8,
            leaveOpen: true
        );
        var responseBody = await reader.ReadToEndAsync();
        responseBody.Should().Contain("Missing MCP-Protocol-Version header");
    }

    [Fact]
    public async Task HandleMessageAsync_Should_Return_400_When_Protocol_Header_Mismatched()
    {
        var (connectionManager, transport, _) = CreateManagerWithTransport();
        await SendInitializeAsync(connectionManager, transport, includeProtocolHeader: false);

        var requestContext = CreatePostContext(
            transport,
            new JsonRpcRequestMessage("2.0", "list-4", "tools/list", null)
        );

        requestContext.Request.Headers["MCP-Protocol-Version"] = "2024-11-05";

        await connectionManager.HandleMessageAsync(requestContext);

        requestContext.Response.StatusCode.Should().Be(400);
        requestContext.Response.Body.Position = 0;
        using var reader = new StreamReader(
            requestContext.Response.Body,
            Encoding.UTF8,
            leaveOpen: true
        );
        var responseBody = await reader.ReadToEndAsync();
        responseBody.Should().Contain("Unsupported MCP-Protocol-Version");
    }

    [Fact]
    public async Task HandleMessageAsync_Should_Accept_Notification()
    {
        var (connectionManager, transport, writer) = CreateManagerWithTransport();
        await SendInitializeAsync(connectionManager, transport, includeProtocolHeader: false);

        var notificationContext = new DefaultHttpContext();
        notificationContext.Request.Method = HttpMethods.Post;
        notificationContext.Request.Scheme = "http";
        notificationContext.Request.Host = HostString.FromUriComponent(DefaultOrigin);
        notificationContext.Request.Headers["Mcp-Session-Id"] = transport.SessionId;
        notificationContext.Request.Headers["MCP-Protocol-Version"] =
            McpServer.LatestProtocolVersion;
        notificationContext.Request.Headers["Origin"] = DefaultOrigin;
        notificationContext.Response.Body = new MemoryStream();

        var notificationJson = JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                method = "notifications/ping",
                @params = new { },
            }
        );
        notificationContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(notificationJson)
        );
        notificationContext.Request.Body.Position = 0;

        writer.Reset();

        await connectionManager.HandleMessageAsync(notificationContext);

        notificationContext.Response.StatusCode.Should().Be(202);
        notificationContext.Response.Body.Length.Should().Be(0);
        writer.WrittenPayloads.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleMessageAsync_Should_Reject_Invalid_Origin()
    {
        var (connectionManager, transport, _) = CreateManagerWithTransport();
        await SendInitializeAsync(connectionManager, transport, includeProtocolHeader: false);

        var request = new JsonRpcRequestMessage("2.0", "list-5", "tools/list", null);
        var context = CreatePostContext(transport, request);
        context.Request.Headers["MCP-Protocol-Version"] = McpServer.LatestProtocolVersion;
        context.Request.Headers["Origin"] = "https://malicious.example";

        await connectionManager.HandleMessageAsync(context);

        context.Response.StatusCode.Should().Be(403);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var responseBody = await reader.ReadToEndAsync();
        responseBody.Should().Contain("invalid_origin");
    }

    private static (
        SseTransportHost Manager,
        SseTransport Transport,
        TestResponseWriter Writer
    ) CreateManagerWithTransport(
        string[]? allowedOrigins = null,
        string? canonicalOrigin = DefaultOrigin
    )
    {
        allowedOrigins ??= new[] { DefaultOrigin };
        var serverInfo = new ServerInfo { Name = "Test Server", Version = "1.0.0" };
        var server = new McpServer(
            serverInfo,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            new LoggerFactory()
        );
        var loggerFactory = LoggerFactory.Create(builder => { });
        var connectionManager = new SseTransportHost(
            server,
            loggerFactory,
            TimeSpan.FromMinutes(30),
            authHandler: null,
            allowedOrigins,
            canonicalOrigin
        );

        var writer = new TestResponseWriter();
        var transport = new SseTransport(writer, loggerFactory.CreateLogger<SseTransport>());

        connectionManager.RegisterTransport(transport);
        server.ConnectAsync(transport).GetAwaiter().GetResult();

        return (connectionManager, transport, writer);
    }

    private static DefaultHttpContext CreatePostContext(
        SseTransport transport,
        JsonRpcRequestMessage request
    )
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "http";
        context.Request.Host = HostString.FromUriComponent(DefaultOrigin);
        context.Request.Headers["Origin"] = DefaultOrigin;
        context.Request.Headers["Mcp-Session-Id"] = transport.SessionId;
        context.Response.Body = new MemoryStream();

        var requestJson = JsonSerializer.Serialize(request);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.Body.Position = 0;

        return context;
    }

    private static async Task SendInitializeAsync(
        SseTransportHost connectionManager,
        SseTransport transport,
        bool includeProtocolHeader
    )
    {
        var initializeContext = CreatePostContext(
            transport,
            new JsonRpcRequestMessage(
                "2.0",
                "init-1",
                "initialize",
                JsonSerializer.SerializeToElement(
                    new
                    {
                        protocolVersion = McpServer.LatestProtocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "client", version = "1.0" },
                    }
                )
            )
        );

        if (includeProtocolHeader)
        {
            initializeContext.Request.Headers["MCP-Protocol-Version"] =
                McpServer.LatestProtocolVersion;
        }

        await connectionManager.HandleMessageAsync(initializeContext);
        initializeContext.Response.StatusCode.Should().Be(202);
        initializeContext
            .Response.Headers["MCP-Protocol-Version"]
            .ToString()
            .Should()
            .Be(McpServer.LatestProtocolVersion);
    }

    private sealed class TestResponseWriter : IResponseWriter
    {
        private readonly List<string> _payloads = new();
        private bool _completed;

        public string Id { get; } = Guid.NewGuid().ToString();

        public string? RemoteIpAddress => null;

        public bool IsCompleted => _completed;

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> WrittenPayloads => _payloads;

        public void Reset() => _payloads.Clear();

        public Task CompleteAsync()
        {
            _completed = true;
            return Task.CompletedTask;
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public IEnumerable<KeyValuePair<string, string>> GetRequestHeaders() =>
            Array.Empty<KeyValuePair<string, string>>();

        public void SetHeader(string name, string value)
        {
            Headers[name] = value;
        }

        public Task WriteAsync(string content, CancellationToken cancellationToken = default)
        {
            _payloads.Add(content);
            return Task.CompletedTask;
        }
    }
}
