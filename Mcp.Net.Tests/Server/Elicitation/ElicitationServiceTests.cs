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
    private static (McpServer Server, ToolInvocationContextAccessor Accessor) CreateServer()
    {
        var serverInfo = new ServerInfo { Name = "Test Server", Version = "1.0.0" };
        var serverOptions = new ServerOptions
        {
            Capabilities = new ServerCapabilities(),
            Instructions = "Test server",
        };

        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var accessor = new ToolInvocationContextAccessor();
        var server = new McpServer(
            serverInfo,
            connectionManager,
            serverOptions,
            NullLoggerFactory.Instance,
            toolInvocationContextAccessor: accessor
        );

        return (server, accessor);
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

    [Fact]
    public async Task RequestAsync_ShouldReturnAcceptResult_WhenClientProvidesContent()
    {
        var (server, accessor) = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = accessor.Push(transport.Id());

        var service = new ElicitationService(
            server,
            NullLogger<ElicitationService>.Instance,
            accessor
        );
        var prompt = CreatePrompt();

        var requestTask = service.RequestAsync(prompt);

        transport.SentRequests.Should().ContainSingle();
        var request = transport.SentRequests[0];
        request.Method.Should().Be("elicitation/create");

        var responsePayload = new
        {
            action = "accept",
            content = new { name = "Rogue Trader" },
        };

        transport.SimulateResponse(
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
        var (server, accessor) = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = accessor.Push(transport.Id());

        var service = new ElicitationService(
            server,
            NullLogger<ElicitationService>.Instance,
            accessor
        );
        var prompt = CreatePrompt();

        var requestTask = service.RequestAsync(prompt);
        var request = transport.SentRequests.Single();

        transport.SimulateResponse(
            new JsonRpcResponseMessage("2.0", request.Id, new { action = "decline" }, null)
        );

        var result = await requestTask;
        result.Action.Should().Be(ElicitationAction.Decline);
        result.Content.Should().BeNull();
    }

    [Fact]
    public async Task RequestAsync_ShouldThrow_WhenClientReturnsError()
    {
        var (server, accessor) = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = accessor.Push(transport.Id());

        var service = new ElicitationService(
            server,
            NullLogger<ElicitationService>.Instance,
            accessor
        );
        var prompt = CreatePrompt();

        var requestTask = service.RequestAsync(prompt);
        var request = transport.SentRequests.Single();

        var error = new JsonRpcError
        {
            Code = (int)ErrorCode.InvalidParams,
            Message = "Missing schema",
        };

        transport.SimulateResponse(
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
        var (server, accessor) = CreateServer();
        server.ClientRequestTimeout = TimeSpan.FromMilliseconds(50);
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = accessor.Push(transport.Id());

        var service = new ElicitationService(
            server,
            NullLogger<ElicitationService>.Instance,
            accessor
        );
        var prompt = CreatePrompt();

        await FluentActions
            .Awaiting(() => service.RequestAsync(prompt))
            .Should()
            .ThrowAsync<McpException>()
            .Where(ex => ex.Code == ErrorCode.RequestTimeout);
    }
}
