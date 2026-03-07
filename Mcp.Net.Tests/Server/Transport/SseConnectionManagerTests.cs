using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Server;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.ConnectionManagers;
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
        var (connectionManager, transport, writer) = await CreateManagerWithTransport();

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
        var (connectionManager, transport, writer) = await CreateManagerWithTransport();

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
        var (connectionManager, transport, _) = await CreateManagerWithTransport();
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
        var (connectionManager, transport, _) = await CreateManagerWithTransport();
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
        var (connectionManager, transport, writer) = await CreateManagerWithTransport();
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
        var (connectionManager, transport, _) = await CreateManagerWithTransport();
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

    [Fact]
    public async Task HandleMessageAsync_Should_Reject_Post_For_Different_Authenticated_User_Than_Session_Owner()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var transportRegistry = new InMemoryConnectionManager(
            loggerFactory,
            TimeSpan.FromMinutes(30)
        );
        var server = new McpServer(
            new ServerInfo { Name = "Test Server", Version = "1.0.0" },
            transportRegistry,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            loggerFactory
        );
        var authHandler = new HeaderUserAuthHandler();
        var connectionManager = new SseTransportHost(
            server,
            loggerFactory,
            transportRegistry,
            authHandler,
            new[] { DefaultOrigin },
            DefaultOrigin
        );

        var sseContext = CreateGetContext("owner-user");
        using var sseLifetime = new CancellationTokenSource();
        sseContext.RequestAborted = sseLifetime.Token;

        var connectionTask = connectionManager.HandleSseConnectionAsync(sseContext);

        try
        {
            var sessionId = await WaitForSessionIdAsync(sseContext);
            var transport = await WaitForTransportAsync(transportRegistry, sessionId);
            transport.Should().BeOfType<SseTransport>();
            ((SseTransport)transport).Metadata["UserId"].Should().Be("owner-user");

            var hijackRequest = new JsonRpcRequestMessage("2.0", "list-hijack", "tools/list", null);
            var postContext = CreatePostContext(sessionId, hijackRequest);
            postContext.Request.Headers["X-Test-User"] = "other-user";

            await connectionManager.HandleMessageAsync(postContext);

            postContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            postContext.Response.Body.Position = 0;
            using var reader = new StreamReader(
                postContext.Response.Body,
                Encoding.UTF8,
                leaveOpen: true
            );
            var responseBody = await reader.ReadToEndAsync();
            responseBody.Should().Contain("session");
        }
        finally
        {
            sseLifetime.Cancel();
            await connectionTask;
        }
    }

    [Fact]
    public async Task HandleMessageAsync_Should_Validate_Protocol_Header_Per_Session()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var transportRegistry = new InMemoryConnectionManager(
            loggerFactory,
            TimeSpan.FromMinutes(30)
        );
        var server = new McpServer(
            new ServerInfo { Name = "Test Server", Version = "1.0.0" },
            transportRegistry,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            loggerFactory
        );
        var connectionManager = new SseTransportHost(
            server,
            loggerFactory,
            transportRegistry,
            authHandler: null,
            new[] { DefaultOrigin },
            DefaultOrigin
        );

        var writerA = new TestResponseWriter();
        var writerB = new TestResponseWriter();
        var transportA = new SseTransport(writerA, loggerFactory.CreateLogger<SseTransport>());
        var transportB = new SseTransport(writerB, loggerFactory.CreateLogger<SseTransport>());

        await server.ConnectAsync(transportA);
        await server.ConnectAsync(transportB);

        await SendInitializeAsync(
            connectionManager,
            transportA,
            includeProtocolHeader: false,
            requestedProtocolVersion: McpServer.LatestProtocolVersion
        );
        await SendInitializeAsync(
            connectionManager,
            transportB,
            includeProtocolHeader: false,
            requestedProtocolVersion: "2024-11-05"
        );

        var sessionARequest = CreatePostContext(
            transportA,
            new JsonRpcRequestMessage("2.0", "list-a", "tools/list", null)
        );
        sessionARequest.Request.Headers["MCP-Protocol-Version"] = McpServer.LatestProtocolVersion;

        await connectionManager.HandleMessageAsync(sessionARequest);

        sessionARequest.Response.StatusCode.Should().Be(202);
        sessionARequest
            .Response.Headers["MCP-Protocol-Version"]
            .ToString()
            .Should()
            .Be(McpServer.LatestProtocolVersion);

        var sessionBRequest = CreatePostContext(
            transportB,
            new JsonRpcRequestMessage("2.0", "list-b", "tools/list", null)
        );
        sessionBRequest.Request.Headers["MCP-Protocol-Version"] = "2024-11-05";

        await connectionManager.HandleMessageAsync(sessionBRequest);

        sessionBRequest.Response.StatusCode.Should().Be(202);
        sessionBRequest
            .Response.Headers["MCP-Protocol-Version"]
            .ToString()
            .Should()
            .Be("2024-11-05");

        var mismatchedRequest = CreatePostContext(
            transportA,
            new JsonRpcRequestMessage("2.0", "list-a-mismatch", "tools/list", null)
        );
        mismatchedRequest.Request.Headers["MCP-Protocol-Version"] = "2024-11-05";

        await connectionManager.HandleMessageAsync(mismatchedRequest);

        mismatchedRequest.Response.StatusCode.Should().Be(400);
        mismatchedRequest.Response.Body.Position = 0;
        using var reader = new StreamReader(
            mismatchedRequest.Response.Body,
            Encoding.UTF8,
            leaveOpen: true
        );
        var responseBody = await reader.ReadToEndAsync();
        responseBody.Should().Contain(McpServer.LatestProtocolVersion);
    }

    [Fact]
    public async Task CloseAsync_Should_Remove_Transport_And_Cancel_Pending_Request_When_Response_Completion_Fails()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var transportRegistry = new InMemoryConnectionManager(
            loggerFactory,
            TimeSpan.FromMinutes(30)
        );
        var server = new McpServer(
            new ServerInfo { Name = "Test Server", Version = "1.0.0" },
            transportRegistry,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            loggerFactory
        )
        {
            ClientRequestTimeout = Timeout.InfiniteTimeSpan,
        };

        var writer = new ThrowingCompleteResponseWriter();
        var transport = new SseTransport(writer, loggerFactory.CreateLogger<SseTransport>());

        await server.ConnectAsync(transport);

        var pendingRequest = server.SendClientRequestAsync(transport.SessionId, "noop", null);

        var closeException = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.CloseAsync());
        closeException.Message.Should().Be("Simulated response completion failure.");

        var pendingException = await Record.ExceptionAsync(() =>
            pendingRequest.WaitAsync(TimeSpan.FromMilliseconds(200))
        );
        var registeredTransport = await transportRegistry.GetTransportAsync(transport.SessionId);

        using var scope = new AssertionScope();
        pendingException.Should().BeOfType<OperationCanceledException>();
        registeredTransport.Should().BeNull();
    }

    private static async Task<(
        SseTransportHost Manager,
        SseTransport Transport,
        TestResponseWriter Writer
    )> CreateManagerWithTransport(
        string[]? allowedOrigins = null,
        string? canonicalOrigin = DefaultOrigin
    )
    {
        allowedOrigins ??= new[] { DefaultOrigin };
        var loggerFactory = LoggerFactory.Create(builder => { });
        var transportRegistry = new InMemoryConnectionManager(
            loggerFactory,
            TimeSpan.FromMinutes(30)
        );
        var server = new McpServer(
            new ServerInfo { Name = "Test Server", Version = "1.0.0" },
            transportRegistry,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            loggerFactory
        );
        var connectionManager = new SseTransportHost(
            server,
            loggerFactory,
            transportRegistry,
            authHandler: null,
            allowedOrigins,
            canonicalOrigin
        );

        var writer = new TestResponseWriter();
        var transport = new SseTransport(writer, loggerFactory.CreateLogger<SseTransport>());

        await server.ConnectAsync(transport);

        return (connectionManager, transport, writer);
    }

    private static DefaultHttpContext CreatePostContext(
        SseTransport transport,
        JsonRpcRequestMessage request
    ) => CreatePostContext(transport.SessionId, request);

    private static DefaultHttpContext CreatePostContext(
        string sessionId,
        JsonRpcRequestMessage request
    )
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "http";
        context.Request.Host = HostString.FromUriComponent(DefaultOrigin);
        context.Request.Headers["Origin"] = DefaultOrigin;
        context.Request.Headers["Mcp-Session-Id"] = sessionId;
        context.Response.Body = new MemoryStream();

        var requestJson = JsonSerializer.Serialize(request);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.Body.Position = 0;

        return context;
    }

    private static async Task SendInitializeAsync(
        SseTransportHost connectionManager,
        SseTransport transport,
        bool includeProtocolHeader,
        string requestedProtocolVersion = McpServer.LatestProtocolVersion,
        string? expectedProtocolVersion = null
    )
    {
        expectedProtocolVersion ??= requestedProtocolVersion;

        var initializeContext = CreatePostContext(
            transport,
            new JsonRpcRequestMessage(
                "2.0",
                "init-1",
                "initialize",
                JsonSerializer.SerializeToElement(
                    new
                    {
                        protocolVersion = requestedProtocolVersion,
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
            .Be(expectedProtocolVersion);
    }

    private static DefaultHttpContext CreateGetContext(string userId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = HostString.FromUriComponent(DefaultOrigin);
        context.Request.Headers["Origin"] = DefaultOrigin;
        context.Request.Headers["X-Test-User"] = userId;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> WaitForSessionIdAsync(DefaultHttpContext context)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var sessionId = context.Response.Headers["Mcp-Session-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                return sessionId;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Timed out waiting for SSE session identifier.");
    }

    private static async Task<ITransport> WaitForTransportAsync(
        InMemoryConnectionManager transportRegistry,
        string sessionId
    )
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var transport = await transportRegistry.GetTransportAsync(sessionId);
            if (transport != null)
            {
                return transport;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"Timed out waiting for transport '{sessionId}'.");
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

    private sealed class ThrowingCompleteResponseWriter : IResponseWriter
    {
        private readonly List<string> _payloads = new();

        public bool IsCompleted { get; private set; }

        public string Id { get; } = Guid.NewGuid().ToString();

        public string? RemoteIpAddress => null;

        public Task WriteAsync(string content, CancellationToken cancellationToken = default)
        {
            _payloads.Add(content);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void SetHeader(string name, string value) { }

        public IEnumerable<KeyValuePair<string, string>> GetRequestHeaders() =>
            Array.Empty<KeyValuePair<string, string>>();

        public Task CompleteAsync()
        {
            IsCompleted = true;
            throw new InvalidOperationException("Simulated response completion failure.");
        }
    }

    private sealed class HeaderUserAuthHandler : IAuthHandler
    {
        public string SchemeName => "Test";

        public Task<AuthResult> AuthenticateAsync(HttpContext context)
        {
            var userId = context.Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(
                    AuthResult.Fail(
                        "Missing test user header.",
                        StatusCodes.Status401Unauthorized,
                        "invalid_token",
                        "Missing X-Test-User header."
                    )
                );
            }

            return Task.FromResult(
                AuthResult.Success(
                    userId,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["sub"] = userId,
                    }
                )
            );
        }
    }
}
