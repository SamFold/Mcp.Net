using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Server.Completions;

public class McpServerCompletionTests
{
    private static McpServer CreateServer()
    {
        var info = new ServerInfo { Name = "Test Server", Version = "1.0.0" };
        var options = new ServerOptions
        {
            Capabilities = new ServerCapabilities(),
        };
        return new McpServer(info, options, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Initialize_ShouldAdvertiseCompletions_WhenHandlerRegistered()
    {
        var server = CreateServer();
        server.RegisterPromptCompletion(
            "draft-email",
            (_, _) => Task.FromResult(new CompletionValues { Values = new[] { "value" } })
        );

        var initializeRequest = new JsonRpcRequestMessage(
            "2.0",
            "1",
            "initialize",
            new
            {
                protocolVersion = McpServer.LatestProtocolVersion,
                capabilities = new { },
                clientInfo = new { name = "test", version = "1.0" },
            }
        );

        var response = await server.ProcessJsonRpcRequest(initializeRequest);
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var resultJson = JsonSerializer.Serialize(response.Result);
        using var document = JsonDocument.Parse(resultJson);
        document.RootElement
            .GetProperty("capabilities")
            .TryGetProperty("completions", out var completionsProp)
            .Should()
            .BeTrue();
        completionsProp.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task CompletionRequest_ShouldReturnSuggestions_FromRegisteredHandler()
    {
        var server = CreateServer();
        server.RegisterPromptCompletion(
            "greeting",
            (context, _) =>
            {
                context.Parameters.Argument.Name.Should().Be("language");
                context.Parameters.Argument.Value.Should().Be("en");
                context.ContextArguments.Should().BeEmpty();

                return Task.FromResult(
                    new CompletionValues
                    {
                        Values = new[] { "english", "england" },
                        Total = 2,
                        HasMore = false,
                    }
                );
            }
        );

        var completionRequest = new JsonRpcRequestMessage(
            "2.0",
            "42",
            "completion/complete",
            new CompletionCompleteParams
            {
                Reference = new CompletionReference
                {
                    Type = "ref/prompt",
                    Name = "greeting",
                },
                Argument = new CompletionArgument
                {
                    Name = "language",
                    Value = "en",
                },
            }
        );

        var response = await server.ProcessJsonRpcRequest(completionRequest);
        response.Error.Should().BeNull();
        response.Result.Should().BeOfType<CompletionCompleteResult>();

        var result = (CompletionCompleteResult)response.Result!;
        result.Completion.Should().NotBeNull();
        result.Completion.Values.Should().Equal("english", "england");
        result.Completion.Total.Should().Be(2);
        result.Completion.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task CompletionRequest_ShouldReturnInvalidParams_WhenHandlerMissing()
    {
        var server = CreateServer();
        server.RegisterPromptCompletion(
            "existing",
            (_, _) => Task.FromResult(new CompletionValues { Values = new[] { "demo" } })
        );

        var request = new JsonRpcRequestMessage(
            "2.0",
            "99",
            "completion/complete",
            new CompletionCompleteParams
            {
                Reference = new CompletionReference
                {
                    Type = "ref/prompt",
                    Name = "missing",
                },
                Argument = new CompletionArgument
                {
                    Name = "arg",
                    Value = "x",
                },
            }
        );

        var response = await server.ProcessJsonRpcRequest(request);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be((int)ErrorCode.InvalidParams);
    }

    [Fact]
    public async Task CompletionRequest_ShouldReturnMethodNotFound_WhenCapabilityNotAdvertised()
    {
        var server = CreateServer();

        var request = new JsonRpcRequestMessage(
            "2.0",
            "100",
            "completion/complete",
            new CompletionCompleteParams
            {
                Reference = new CompletionReference
                {
                    Type = "ref/resource",
                    Uri = "mcp://docs/example",
                },
                Argument = new CompletionArgument
                {
                    Name = "path",
                    Value = "readme",
                },
            }
        );

        var response = await server.ProcessJsonRpcRequest(request);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be((int)ErrorCode.MethodNotFound);
    }

    [Fact]
    public async Task CompletionRequest_ShouldReturnInternalError_WhenHandlerThrows()
    {
        var server = CreateServer();
        server.RegisterResourceCompletion(
            "mcp://docs/example",
            (_, _) => throw new InvalidOperationException("boom")
        );

        var request = new JsonRpcRequestMessage(
            "2.0",
            "101",
            "completion/complete",
            new CompletionCompleteParams
            {
                Reference = new CompletionReference
                {
                    Type = "ref/resource",
                    Uri = "mcp://docs/example",
                },
                Argument = new CompletionArgument
                {
                    Name = "path",
                    Value = "readme",
                },
            }
        );

        var response = await server.ProcessJsonRpcRequest(request);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be((int)ErrorCode.InternalError);
    }
}
