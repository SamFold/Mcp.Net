using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Server;
using Mcp.Net.Server.Completions;
using Mcp.Net.Server.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Services;

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
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        return new McpServer(
            info,
            connectionManager,
            options,
            NullLoggerFactory.Instance
        );
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

        var response = await server.ProcessJsonRpcRequest(initializeRequest, "test-session");
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

        var response = await server.ProcessJsonRpcRequest(completionRequest, "test-session");
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

        var response = await server.ProcessJsonRpcRequest(request, "test-session");
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

    [Fact]
    public async Task HandleRequestAsync_Should_PassRequestCancellationToken_ToCompletionHandler()
    {
        var server = CreateServer();
        CancellationToken observedToken = default;

        server.RegisterPromptCompletion(
            "draft-email",
            (_, cancellationToken) =>
            {
                observedToken = cancellationToken;
                return Task.FromResult(new CompletionValues { Values = new[] { "value" } });
            }
        );

        using var cts = new CancellationTokenSource();
        var request = new JsonRpcRequestMessage(
            "2.0",
            "ctx-completion",
            "completion/complete",
            new CompletionCompleteParams
            {
                Reference = new CompletionReference
                {
                    Type = "ref/prompt",
                    Name = "draft-email",
                },
                Argument = new CompletionArgument
                {
                    Name = "subject",
                    Value = "demo",
                },
            }
        );

        var context = new ServerRequestContext(
            "session-completion",
            "transport-completion",
            request,
            cts.Token
        );

        var response = await server.HandleRequestAsync(context);

        response.Error.Should().BeNull();
        observedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task HandleRequestAsync_Should_Propagate_RequestCancellation_FromCompletionHandler()
    {
        var server = CreateServer();

        server.RegisterPromptCompletion(
            "draft-email",
            (_, cancellationToken) => Task.FromCanceled<CompletionValues>(cancellationToken)
        );

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new JsonRpcRequestMessage(
            "2.0",
            "ctx-completion-cancelled",
            "completion/complete",
            new CompletionCompleteParams
            {
                Reference = new CompletionReference
                {
                    Type = "ref/prompt",
                    Name = "draft-email",
                },
                Argument = new CompletionArgument
                {
                    Name = "subject",
                    Value = "demo",
                },
            }
        );

        var context = new ServerRequestContext(
            "session-completion-cancelled",
            "transport-completion-cancelled",
            request,
            cts.Token
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => server.HandleRequestAsync(context)
        );
    }

    [Fact]
    public async Task HandleRequestAsync_Should_Expose_RequestMetadata_And_SessionContext_ToCompletionHandler()
    {
        var server = CreateServer();

        CompletionRequestContext? observedContext = null;
        server.RegisterPromptCompletion(
            "draft-email",
            (context, _) =>
            {
                observedContext = context;
                return Task.FromResult(new CompletionValues { Values = new[] { "value" } });
            }
        );

        var request = new JsonRpcRequestMessage(
            "2.0",
            "ctx-completion-metadata",
            "completion/complete",
            new CompletionCompleteParams
            {
                Reference = new CompletionReference
                {
                    Type = "ref/prompt",
                    Name = "draft-email",
                },
                Argument = new CompletionArgument
                {
                    Name = "subject",
                    Value = "demo",
                },
            }
        );

        var metadata = new Dictionary<string, string>
        {
            ["UserId"] = "user-123",
            ["TraceId"] = "trace-abc",
        };

        var context = new ServerRequestContext(
            "session-completion-metadata",
            "transport-completion-metadata",
            request,
            CancellationToken.None,
            metadata
        );

        var response = await server.HandleRequestAsync(context);

        response.Error.Should().BeNull();
        Assert.NotNull(observedContext);
        observedContext!.RequestContext.Should().NotBeNull();
        observedContext.RequestContext!.SessionId.Should().Be("session-completion-metadata");
        observedContext.RequestContext.TransportId.Should().Be(
            "transport-completion-metadata"
        );
        observedContext.RequestContext.Metadata
            .Should()
            .Contain(new KeyValuePair<string, string>("UserId", "user-123"))
            .And.Contain(new KeyValuePair<string, string>("TraceId", "trace-abc"));
    }
}
