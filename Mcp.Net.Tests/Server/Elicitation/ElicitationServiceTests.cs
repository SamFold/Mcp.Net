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

        return new McpServer(serverInfo, serverOptions, NullLoggerFactory.Instance);
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
        var server = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = server.PushSessionContext(transport.Id());

        var service = new ElicitationService(server, NullLogger<ElicitationService>.Instance);
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
        var server = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = server.PushSessionContext(transport.Id());

        var service = new ElicitationService(server, NullLogger<ElicitationService>.Instance);
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
        var server = CreateServer();
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = server.PushSessionContext(transport.Id());

        var service = new ElicitationService(server, NullLogger<ElicitationService>.Instance);
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
        var server = CreateServer();
        server.ClientRequestTimeout = TimeSpan.FromMilliseconds(50);
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        using var scope = server.PushSessionContext(transport.Id());

        var service = new ElicitationService(server, NullLogger<ElicitationService>.Instance);
        var prompt = CreatePrompt();

        await FluentActions
            .Awaiting(() => service.RequestAsync(prompt))
            .Should()
            .ThrowAsync<McpException>()
            .Where(ex => ex.Code == ErrorCode.RequestTimeout);
    }
}
