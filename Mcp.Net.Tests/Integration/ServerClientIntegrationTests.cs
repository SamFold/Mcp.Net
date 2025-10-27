using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Client.Transport;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Core.Transport;
using Mcp.Net.Server;
using Mcp.Net.Server.Elicitation;
using Mcp.Net.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Tests.Integration;

public class ServerClientIntegrationTests
{
    [Fact]
    public async Task Full_Request_Response_Cycle_With_Tool_Call()
    {
        // Arrange - Set up server
        var serverInfo = new ServerInfo { Name = "Integration Test Server", Version = "1.0.0" };
        var serverOptions = new ServerOptions
        {
            Instructions = "Test server for integration tests",
            Capabilities = new ServerCapabilities()
        };
        
        var server = new McpServer(serverInfo, serverOptions);
        
        // Register a simple calculator tool
        server.RegisterTool(
            "add",
            "Add two numbers",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    a = new { type = "number" },
                    b = new { type = "number" }
                },
                required = new[] { "a", "b" }
            }),
            (args) =>
            {
                var a = args!.Value.GetProperty("a").GetDouble();
                var b = args.Value.GetProperty("b").GetDouble();
                var sum = a + b;
                
                return Task.FromResult(new ToolCallResult
                {
                    Content = new[] { new TextContent { Text = $"The sum is {sum}" } },
                    IsError = false
                });
            }
        );
        
        // Create mock transport
        var transport = new MockTransport();
        await server.ConnectAsync(transport);
        
        // Act - Initialize
        var paramsElement = JsonSerializer.SerializeToElement(new
        {
            clientInfo = new { name = "Test Client", version = "1.0" },
            capabilities = new { },
            protocolVersion = "2024-11-05"
        });
        
        transport.SimulateRequest(new JsonRpcRequestMessage(
            "2.0",
            "init-1",
            "initialize",
            paramsElement
        ));
        
        // Check initialization response
        transport.SentMessages.Should().HaveCount(1);
        var initResponse = transport.SentMessages[0];
        initResponse.Id.Should().Be("init-1");
        initResponse.Error.Should().BeNull();
        
        // Now list tools
        transport.SimulateRequest(new JsonRpcRequestMessage(
            "2.0",
            "list-1",
            "tools/list",
            null
        ));
        
        // Check tools list response
        transport.SentMessages.Should().HaveCount(2);
        var toolsResponse = transport.SentMessages[1];
        toolsResponse.Id.Should().Be("list-1");
        toolsResponse.Error.Should().BeNull();
        
        var toolsResult = JsonSerializer.SerializeToElement(toolsResponse.Result!);
        var tools = toolsResult.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        tools[0].GetProperty("name").GetString().Should().Be("add");
        
        // Now call the tool
        var callParamsElement = JsonSerializer.SerializeToElement(new
        {
            name = "add",
            arguments = new { a = 5, b = 7 }
        });
        
        transport.SimulateRequest(new JsonRpcRequestMessage(
            "2.0",
            "call-1",
            "tools/call",
            callParamsElement
        ));
        
        // Check tool call response
        transport.SentMessages.Should().HaveCount(3);
        var callResponse = transport.SentMessages[2];
        callResponse.Id.Should().Be("call-1");
        callResponse.Error.Should().BeNull();
        
        var callResult = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(callResponse.Result!)
        );
        callResult!.IsError.Should().BeFalse();
        callResult.Content.Should().HaveCount(1);
        var content = callResult.Content.First() as TextContent;
        content!.Text.Should().Be("The sum is 12");
    }

    [Fact]
    public async Task SseTransport_ShouldHandleElicitation_And_Completions_EndToEnd()
    {
        await using var serverHost = await IntegrationTestServerFactory.StartSseServerAsync(
            server =>
            {
                var elicitationSchema = new ElicitationSchema()
                    .AddProperty(
                        "alias",
                        ElicitationSchemaProperty.ForString(
                            title: "Display Alias",
                            description: "Alias to register for the agent"
                        ),
                        required: true
                    );

                var toolInputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new { },
                });

                server.RegisterTool(
                    "integration.elicitation",
                    "Demonstrates server-initiated elicitation",
                    toolInputSchema,
                    async _ =>
                    {
                        var prompt = new ElicitationPrompt(
                            "Please provide the display alias",
                            elicitationSchema
                        );

                        var elicitationService = new ElicitationService(
                            server,
                            NullLogger<ElicitationService>.Instance
                        );

                        var result = await elicitationService.RequestAsync(prompt).ConfigureAwait(false);

                        var alias = result.Content.HasValue
                            && result.Content.Value.TryGetProperty("alias", out var aliasElement)
                            ? aliasElement.GetString()
                            : null;

                        return new ToolCallResult
                        {
                            Content = new ContentBase[]
                            {
                                new TextContent
                                {
                                    Text = $"Elicitation {result.Action}: {alias ?? "<none>"}",
                                },
                            },
                        };
                    }
                );

                server.RegisterPromptCompletion(
                    "draft-follow-up-email",
                    (context, _) =>
                    {
                        var argumentName = context.Parameters.Argument?.Name ?? string.Empty;

                        CompletionValues BuildRecipientSuggestions(string prefix)
                        {
                            var candidates = new[]
                            {
                                "engineering@mcp.example",
                                "support@mcp.example",
                            };

                            var matches = candidates
                                .Where(candidate =>
                                    string.IsNullOrWhiteSpace(prefix)
                                    || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                )
                                .Take(100)
                                .ToArray();

                            return new CompletionValues
                            {
                                Values = matches,
                                Total = matches.Length,
                                HasMore = false,
                            };
                        }

                        CompletionValues BuildContextSuggestions() => new()
                        {
                            Values = new[]
                            {
                                "Summarise the calculator results and next actions.",
                                "Reference the elicited alias and provide status." ,
                            },
                            Total = 2,
                            HasMore = false,
                        };

                        return Task.FromResult(
                            argumentName switch
                            {
                                "recipient" => BuildRecipientSuggestions(
                                    context.Parameters.Argument?.Value ?? string.Empty
                                ),
                                "context" => BuildContextSuggestions(),
                                _ => new CompletionValues
                                {
                                    Values = Array.Empty<string>(),
                                    Total = 0,
                                    HasMore = false,
                                },
                            }
                        );
                    },
                    overwrite: true
                );
            }
        );

        var elicitationContexts = new List<ElicitationRequestContext>();

        var clientLogger = NullLoggerFactory.Instance.CreateLogger("SseIntegrationClient");

        var requestClient = serverHost.CreateHttpClient();
        requestClient.BaseAddress = new Uri(serverHost.ServerUrl);

        var streamClient = serverHost.CreateHttpClient();
        streamClient.BaseAddress = new Uri(serverHost.ServerUrl);

        using var client = new TestSseMcpClient(
            new SseClientTransport(requestClient, streamClient, clientLogger),
            clientLogger
        );

        client.SetElicitationHandler((context, cancellationToken) =>
        {
            elicitationContexts.Add(context);
            return Task.FromResult(
                ElicitationClientResponse.Accept(new { alias = "Voyager" })
            );
        });

        await client.Initialize();

        client.ServerCapabilities.Should().NotBeNull();
        client.ServerCapabilities!.Completions.Should().NotBeNull();

        var toolResult = await client.CallTool("integration.elicitation");

        elicitationContexts.Should().ContainSingle();
        var elicitationContext = elicitationContexts[0];
        elicitationContext.Message.Should().Contain("display alias");
        elicitationContext.RequestedSchema.Properties.Should().ContainKey("alias");

        toolResult.IsError.Should().BeFalse();
        toolResult.Content.Should().NotBeNull();
        var toolText = toolResult.Content!
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<TextContent>()
            .Which
            .Text;
        toolText.Should().Contain("Voyager");

        var recipientCompletion = await client.CompleteAsync(
            new CompletionReference
            {
                Type = "ref/prompt",
                Name = "draft-follow-up-email",
            },
            new CompletionArgument
            {
                Name = "recipient",
                Value = "eng",
            }
        );

        recipientCompletion.Values.Should().NotBeNull();
        recipientCompletion.Values.Should().ContainSingle("engineering@mcp.example");
        recipientCompletion.Total.Should().Be(1);
        recipientCompletion.HasMore.Should().BeFalse();

        var contextCompletion = await client.CompleteAsync(
            new CompletionReference
            {
                Type = "ref/prompt",
                Name = "draft-follow-up-email",
            },
            new CompletionArgument
            {
                Name = "context",
                Value = string.Empty,
            },
            new CompletionContext
            {
                Arguments = new Dictionary<string, string>
                {
                    ["recipient"] = "engineering@mcp.example",
                },
            }
        );

        contextCompletion.Values.Should().Contain("Summarise the calculator results and next actions.");
        contextCompletion.Total.Should().Be(2);
        contextCompletion.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task StdioTransport_ShouldHandleElicitation_And_Completions_EndToEnd()
    {
        await using var stdioServer = await IntegrationTestServerFactory.StartStdioServerAsync(
            server =>
            {
                var elicitationSchema = new ElicitationSchema()
                    .AddProperty(
                        "alias",
                        ElicitationSchemaProperty.ForString(
                            title: "Display Alias",
                            description: "Alias to register for the agent"
                        ),
                        required: true
                    );

                server.RegisterTool(
                    "integration.elicitation",
                    "Demonstrates server-initiated elicitation",
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        properties = new { },
                    }),
                    async _ =>
                    {
                        var prompt = new ElicitationPrompt(
                            "Please provide the display alias",
                            elicitationSchema
                        );

                        var elicitationService = new ElicitationService(
                            server,
                            NullLogger<ElicitationService>.Instance
                        );

                        var result = await elicitationService.RequestAsync(prompt).ConfigureAwait(false);

                        var alias = result.Content.HasValue
                            && result.Content.Value.TryGetProperty("alias", out var aliasElement)
                            ? aliasElement.GetString()
                            : null;

                        return new ToolCallResult
                        {
                            Content = new ContentBase[]
                            {
                                new TextContent
                                {
                                    Text = $"Elicitation {result.Action}: {alias ?? "<none>"}",
                                },
                            },
                        };
                    }
                );

                server.RegisterPromptCompletion(
                    "draft-follow-up-email",
                    (context, _) =>
                    {
                        var argumentName = context.Parameters.Argument?.Name ?? string.Empty;

                        CompletionValues BuildRecipientSuggestions(string prefix)
                        {
                            var candidates = new[]
                            {
                                "engineering@mcp.example",
                                "support@mcp.example",
                            };

                            var matches = candidates
                                .Where(candidate =>
                                    string.IsNullOrWhiteSpace(prefix)
                                    || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                )
                                .Take(100)
                                .ToArray();

                            return new CompletionValues
                            {
                                Values = matches,
                                Total = matches.Length,
                                HasMore = false,
                            };
                        }

                        CompletionValues BuildContextSuggestions() => new()
                        {
                            Values = new[]
                            {
                                "Summarise the calculator results and next actions.",
                                "Reference the elicited alias and provide status." ,
                            },
                            Total = 2,
                            HasMore = false,
                        };

                        return Task.FromResult(
                            argumentName switch
                            {
                                "recipient" => BuildRecipientSuggestions(
                                    context.Parameters.Argument?.Value ?? string.Empty
                                ),
                                "context" => BuildContextSuggestions(),
                                _ => new CompletionValues
                                {
                                    Values = Array.Empty<string>(),
                                    Total = 0,
                                    HasMore = false,
                                },
                            }
                        );
                    },
                    overwrite: true
                );
            }
        );

        var elicitationContexts = new List<ElicitationRequestContext>();

        using var client = new TestStdioMcpClient(
            new StdioClientTransport(stdioServer.ClientInput, stdioServer.ClientOutput, NullLogger<StdioClientTransport>.Instance),
            NullLoggerFactory.Instance.CreateLogger("StdioIntegrationClient")
        );

        client.SetElicitationHandler((context, cancellationToken) =>
        {
            elicitationContexts.Add(context);
            return Task.FromResult(
                ElicitationClientResponse.Accept(new { alias = "Voyager" })
            );
        });

        await client.Initialize();

        client.ServerCapabilities.Should().NotBeNull();
        client.ServerCapabilities!.Completions.Should().NotBeNull();

        var toolResult = await client.CallTool("integration.elicitation");

        elicitationContexts.Should().ContainSingle();
        var elicitationContext = elicitationContexts[0];
        elicitationContext.Message.Should().Contain("display alias");
        elicitationContext.RequestedSchema.Properties.Should().ContainKey("alias");

        toolResult.IsError.Should().BeFalse();
        toolResult.Content.Should().NotBeNull();
        var toolText = toolResult.Content!
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<TextContent>()
            .Which
            .Text;
        toolText.Should().Contain("Voyager");

        var recipientCompletion = await client.CompleteAsync(
            new CompletionReference
            {
                Type = "ref/prompt",
                Name = "draft-follow-up-email",
            },
            new CompletionArgument
            {
                Name = "recipient",
                Value = "eng",
            }
        );

        recipientCompletion.Values.Should().ContainSingle("engineering@mcp.example");
        recipientCompletion.Total.Should().Be(1);
        recipientCompletion.HasMore.Should().BeFalse();

        var contextCompletion = await client.CompleteAsync(
            new CompletionReference
            {
                Type = "ref/prompt",
                Name = "draft-follow-up-email",
            },
            new CompletionArgument
            {
                Name = "context",
                Value = string.Empty,
            },
            new CompletionContext
            {
                Arguments = new Dictionary<string, string>
                {
                    ["recipient"] = "engineering@mcp.example",
                },
            }
        );

        contextCompletion.Values.Should().Contain("Summarise the calculator results and next actions.");
        contextCompletion.Total.Should().Be(2);
        contextCompletion.HasMore.Should().BeFalse();
    }
}

internal sealed class TestSseMcpClient : McpClient
{
    private readonly IClientTransport _transport;

    public TestSseMcpClient(IClientTransport transport, ILogger logger)
        : base("IntegrationClient", "1.0.0", logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.OnError += RaiseOnError;
        _transport.OnClose += RaiseOnClose;
        _transport.OnResponse += RaiseOnResponse;
        _transport.OnNotification += RaiseOnNotification;
    }

    public override async Task Initialize()
    {
        await _transport.StartAsync().ConfigureAwait(false);
        await InitializeProtocolAsync(_transport).ConfigureAwait(false);
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
        _transport.CloseAsync().GetAwaiter().GetResult();
    }
}

internal sealed class TestStdioMcpClient : McpClient
{
    private readonly StdioClientTransport _transport;

    public TestStdioMcpClient(StdioClientTransport transport, ILogger logger)
        : base("IntegrationClient", "1.0.0", logger)
    {
        _transport = transport;
        _transport.OnError += RaiseOnError;
        _transport.OnClose += RaiseOnClose;
        _transport.OnResponse += RaiseOnResponse;
        _transport.OnNotification += RaiseOnNotification;
    }

    public override async Task Initialize()
    {
        await _transport.StartAsync().ConfigureAwait(false);
        await InitializeProtocolAsync(_transport).ConfigureAwait(false);
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
        _transport.CloseAsync().GetAwaiter().GetResult();
    }
}
