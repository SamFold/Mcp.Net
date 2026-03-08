using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Server;
using Mcp.Net.Server.Elicitation;
using Mcp.Net.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Services;

namespace Mcp.Net.Tests.Server.Elicitation;

public class ElicitationServiceTests
{
    private static McpServer CreateServer()
    {
        var serverInfo = new ServerInfo { Name = "Test Server", Version = "1.0.0" };
        var serverOptions = new ServerOptions
        {
            Capabilities = new ServerCapabilities(),
            Instructions = "Test server",
        };

        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var server = new McpServer(
            serverInfo,
            connectionManager,
            serverOptions,
            NullLoggerFactory.Instance
        );

        return server;
    }

    private static ElicitationPrompt CreatePrompt()
    {
        var schema = new ElicitationSchema()
            .AddProperty(
                "name",
                ElicitationSchemaProperty.ForString(
                    title: "Display Name",
                    description: "Name to display for the user"
                ),
                required: true
            );

        return new ElicitationPrompt("Provide the display name", schema);
    }

    private static Task InitializeSessionAsync(
        McpServer server,
        string sessionId,
        object? capabilities = null
    )
    {
        var initializeRequest = new JsonRpcRequestMessage(
            "2.0",
            Guid.NewGuid().ToString("N"),
            "initialize",
            JsonSerializer.SerializeToElement(
                new
                {
                    clientInfo = new ClientInfo { Name = "Test Client", Version = "1.0" },
                    capabilities = capabilities ?? new { },
                    protocolVersion = McpServer.LatestProtocolVersion,
                }
            )
        );

        return server.ProcessJsonRpcRequest(initializeRequest, sessionId);
    }

    [Fact]
    public async Task RequestAsync_ShouldReturnAcceptResult_WhenClientProvidesContent()
    {
        var server = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        await InitializeSessionAsync(server, transport.Id(), capabilities: new { elicitation = new { } });

        var service = new ElicitationService(
            server,
            transport.Id(),
            NullLogger<ElicitationService>.Instance
        );
        var prompt = CreatePrompt();

        var requestTask = service.RequestAsync(prompt);

        // Allow the request to be sent
        await Task.Delay(10);

        transport.SentRequests.Should().ContainSingle();
        var request = transport.SentRequests[0];
        request.Method.Should().Be("elicitation/create");

        var responsePayload = new
        {
            action = "accept",
            content = new { name = "Rogue Trader" },
        };

        // Route client response through the server entry point (new architecture)
        await server.HandleClientResponseAsync(
            transport.Id(),
            new JsonRpcResponseMessage("2.0", request.Id, responsePayload, null)
        );

        var result = await requestTask;
        result.Action.Should().Be(ElicitationAction.Accept);
        result.Content.Should().NotBeNull();
        result.Content!.Value.GetProperty("name").GetString().Should().Be("Rogue Trader");
    }

    [Fact]
    public async Task RequestAsync_ShouldReturnDecline_WhenClientDeclines()
    {
        var server = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        await InitializeSessionAsync(server, transport.Id(), capabilities: new { elicitation = new { } });

        var service = new ElicitationService(
            server,
            transport.Id(),
            NullLogger<ElicitationService>.Instance
        );
        var prompt = CreatePrompt();

        var requestTask = service.RequestAsync(prompt);

        // Allow the request to be sent
        await Task.Delay(10);

        var request = transport.SentRequests.Single();

        // Route client response through the server entry point (new architecture)
        await server.HandleClientResponseAsync(
            transport.Id(),
            new JsonRpcResponseMessage("2.0", request.Id, new { action = "decline" }, null)
        );

        var result = await requestTask;
        result.Action.Should().Be(ElicitationAction.Decline);
        result.Content.Should().BeNull();
    }

    [Fact]
    public async Task RequestAsync_ShouldThrow_WhenClientReturnsError()
    {
        var server = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        await InitializeSessionAsync(server, transport.Id(), capabilities: new { elicitation = new { } });

        var service = new ElicitationService(
            server,
            transport.Id(),
            NullLogger<ElicitationService>.Instance
        );
        var prompt = CreatePrompt();

        var requestTask = service.RequestAsync(prompt);

        // Allow the request to be sent
        await Task.Delay(10);

        var request = transport.SentRequests.Single();

        var error = new JsonRpcError
        {
            Code = (int)ErrorCode.InvalidParams,
            Message = "Missing schema",
        };

        // Route client response through the server entry point (new architecture)
        await server.HandleClientResponseAsync(
            transport.Id(),
            new JsonRpcResponseMessage("2.0", request.Id, null, error)
        );

        await FluentActions.Awaiting(async () => await requestTask.ConfigureAwait(false))
            .Should()
            .ThrowAsync<McpException>()
            .Where(ex => ex.Code == ErrorCode.InvalidParams);
    }

    [Fact]
    public async Task RequestAsync_ShouldThrowTimeout_WhenClientDoesNotRespond()
    {
        var server = CreateServer();
        server.ClientRequestTimeout = TimeSpan.FromMilliseconds(50);
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        await InitializeSessionAsync(server, transport.Id(), capabilities: new { elicitation = new { } });

        var service = new ElicitationService(
            server,
            transport.Id(),
            NullLogger<ElicitationService>.Instance
        );
        var prompt = CreatePrompt();

        await FluentActions
            .Awaiting(() => service.RequestAsync(prompt))
            .Should()
            .ThrowAsync<McpException>()
            .Where(ex => ex.Code == ErrorCode.RequestTimeout);
    }

    [Fact]
    public async Task RequestAsync_ShouldThrow_WhenClientDidNotAdvertiseElicitationCapability()
    {
        var server = CreateServer();
        server.ClientRequestTimeout = TimeSpan.FromMilliseconds(50);
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        await InitializeSessionAsync(server, transport.Id(), capabilities: new { });

        var service = new ElicitationService(
            server,
            transport.Id(),
            NullLogger<ElicitationService>.Instance
        );
        var prompt = CreatePrompt();

        await FluentActions
            .Awaiting(() => service.RequestAsync(prompt))
            .Should()
            .ThrowAsync<McpException>()
            .Where(ex => ex.Code == ErrorCode.MethodNotFound);

        transport.SentRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestAsync_ShouldNotReuse_ElicitationCapability_OnReplacementTransport_BeforeReinitialize()
    {
        var server = CreateServer();
        server.ClientRequestTimeout = TimeSpan.FromMilliseconds(50);
        var originalTransport = new MockTransport("shared-session");
        await server.ConnectAsync(originalTransport);
        await InitializeSessionAsync(
            server,
            originalTransport.Id(),
            capabilities: new { elicitation = new { } }
        );

        var replacementTransport = new MockTransport("shared-session");
        await server.ConnectAsync(replacementTransport);

        var service = new ElicitationService(
            server,
            replacementTransport.Id(),
            NullLogger<ElicitationService>.Instance
        );
        var prompt = CreatePrompt();

        await FluentActions
            .Awaiting(() => service.RequestAsync(prompt))
            .Should()
            .ThrowAsync<McpException>()
            .Where(ex => ex.Code == ErrorCode.MethodNotFound);

        replacementTransport.SentRequests.Should().BeEmpty(
            "a replacement transport must negotiate client capabilities again before the server sends elicitation/create"
        );
    }
}
