using System.IO;
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
    [Fact]
    public async Task HandleMessageAsync_Should_Use_Header_Session_Id()
    {
        // Arrange
        var serverInfo = new ServerInfo { Name = "Test Server", Version = "1.0.0" };
        var server = new McpServer(
            serverInfo,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            new LoggerFactory()
        );
        var loggerFactory = LoggerFactory.Create(builder => { });
        var connectionManager = new SseConnectionManager(server, loggerFactory);

        var writer = new TestResponseWriter();
        var transport = new SseTransport(
            writer,
            loggerFactory.CreateLogger<SseTransport>()
        );

        connectionManager.RegisterTransport(transport);
        await server.ConnectAsync(transport);

        // Verify session header exposed on SSE response
        writer.Headers.Should().ContainKey("Mcp-Session-Id").WhoseValue.Should().Be(transport.SessionId);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers["Mcp-Session-Id"] = transport.SessionId;
        context.Response.Body = new MemoryStream();

        var request = new JsonRpcRequestMessage(
            "2.0",
            "list-1",
            "tools/list",
            null
        );
        var requestJson = JsonSerializer.Serialize(request);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.Body.Position = 0;

        // Act
        await connectionManager.HandleMessageAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(202);
        writer.WrittenPayloads.Should().NotBeEmpty();
        var payload = writer.WrittenPayloads.Single();
        payload.Should().StartWith("data: ");
        payload.Should().Contain("\"id\":\"list-1\"");
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
