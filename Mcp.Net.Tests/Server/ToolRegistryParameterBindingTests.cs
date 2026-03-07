using System.Threading.Tasks;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Server;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Server.Elicitation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Services;
using Microsoft.Extensions.Logging;
using Mcp.Net.Tests.TestUtils;

namespace Mcp.Net.Tests.Server;

public class ToolRegistryParameterBindingTests
{
    private static ToolRegistry CreateRegistry(IServiceProvider services) =>
        new ToolRegistry(services, NullLogger<ToolRegistry>.Instance);

    private static McpServer CreateServer()
    {
        var info = new ServerInfo { Name = "Test", Version = "1.0.0" };
        var options = new ServerOptions
        {
            Capabilities = new ServerCapabilities
            {
                Tools = new { listChanged = true },
                Resources = new { },
                Prompts = new { },
            },
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
    public async Task ToolRegistry_BindsCamelCaseParameterWithoutLowercasing()
    {
        var server = CreateServer();
        var services = new ServiceCollection()
            .AddSingleton(server)
            .AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var registry = CreateRegistry(services);
        registry.AddAssembly(typeof(CaseSensitiveTool).Assembly);
        registry.RegisterToolsWithServer(server);

        var request = new JsonRpcRequestMessage(
            "2.0",
            "1",
            "tools/call",
            new { name = "caseSensitive.echo", arguments = new { userId = "Value42" } }
        );

        var response = await server.ProcessJsonRpcRequest(request, "test-session");
        Assert.Null(response.Error);

        var toolResult = Assert.IsType<ToolCallResult>(response.Result);
        Assert.False(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var text = Assert.IsType<TextContent>(content);
        Assert.Equal("Value42", text.Text);
    }

    [Fact]
    public async Task ToolRegistry_BindsPascalCaseParameterNames()
    {
        var server = CreateServer();
        var services = new ServiceCollection()
            .AddSingleton(server)
            .AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var registry = CreateRegistry(services);
        registry.AddAssembly(typeof(PascalCaseTool).Assembly);
        registry.RegisterToolsWithServer(server);

        var request = new JsonRpcRequestMessage(
            "2.0",
            "9",
            "tools/call",
            new { name = "pascal.echo", arguments = new { ApiKey = "Snake" } }
        );

        var response = await server.ProcessJsonRpcRequest(request, "test-session");
        Assert.Null(response.Error);

        var toolResult = Assert.IsType<ToolCallResult>(response.Result);
        Assert.False(toolResult.IsError);
        var text = Assert.IsType<TextContent>(Assert.Single(toolResult.Content));
        Assert.Equal("Snake", text.Text);
    }

    [Fact]
    public async Task ToolRegistry_CanRegisterTools_WhenServiceProviderDoesNotContainServer()
    {
        var server = CreateServer();
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var registry = CreateRegistry(services);
        registry.AddAssembly(typeof(CaseSensitiveTool).Assembly);

        registry.RegisterToolsWithServer(server);

        var response = await server.ProcessJsonRpcRequest(
            new JsonRpcRequestMessage(
                "2.0",
                "11",
                "tools/call",
                new { name = "caseSensitive.echo", arguments = new { userId = "Detached" } }
            ),
            "test-session"
        );

        Assert.Null(response.Error);
        var toolResult = Assert.IsType<ToolCallResult>(response.Result);
        var text = Assert.IsType<TextContent>(Assert.Single(toolResult.Content));
        Assert.Equal("Detached", text.Text);
    }

    [Fact]
    public async Task ToolRegistry_UsesTargetServerForSessionBoundServices()
    {
        var providerServer = CreateServer();
        var targetServer = CreateServer();
        var providerTransport = new MockTransport("provider-session");
        var targetTransport = new MockTransport("target-session");

        await providerServer.ConnectAsync(providerTransport);
        await targetServer.ConnectAsync(targetTransport);

        var services = new ServiceCollection()
            .AddSingleton(providerServer)
            .AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var registry = CreateRegistry(services);
        registry.AddAssembly(typeof(ElicitationAwareTool).Assembly);
        registry.RegisterToolsWithServer(targetServer);

        var responseTask = targetServer.ProcessJsonRpcRequest(
            new JsonRpcRequestMessage(
                "2.0",
                "12",
                "tools/call",
                new { name = "elicitation.await_name", arguments = new { } }
            ),
            targetTransport.Id()
        );

        await Task.Delay(10);

        Assert.Empty(providerTransport.SentRequests);
        var outboundRequest = Assert.Single(targetTransport.SentRequests);
        Assert.Equal("elicitation/create", outboundRequest.Method);

        await targetServer.HandleClientResponseAsync(
            targetTransport.Id(),
            new JsonRpcResponseMessage(
                "2.0",
                outboundRequest.Id,
                new
                {
                    action = "accept",
                    content = new { name = "Rogue Trader" },
                },
                null
            )
        );

        var response = await responseTask;
        Assert.Null(response.Error);

        var toolResult = Assert.IsType<ToolCallResult>(response.Result);
        var text = Assert.IsType<TextContent>(Assert.Single(toolResult.Content));
        Assert.Equal("Rogue Trader", text.Text);
    }

    private class CaseSensitiveTool
    {
        [McpTool("caseSensitive.echo", "Echoes the provided user identifier")]
        public ToolCallResult Echo([McpParameter(required: true)] string userId)
        {
            return new ToolCallResult
            {
                Content = new[] { new TextContent { Text = userId } },
                IsError = false,
            };
        }
    }

    private class PascalCaseTool
    {
        [McpTool("pascal.echo", "Echoes a PascalCase argument name")]
        public ToolCallResult EchoPascal([McpParameter(required: true)] string ApiKey)
        {
            return new ToolCallResult
            {
                Content = new[] { new TextContent { Text = ApiKey } },
                IsError = false,
            };
        }
    }

    private class ElicitationAwareTool
    {
        private readonly IElicitationService _elicitationService;

        public ElicitationAwareTool(IElicitationService elicitationService)
        {
            _elicitationService = elicitationService;
        }

        [McpTool("elicitation.await_name", "Requests a name from the connected client")]
        public async Task<ToolCallResult> RequestNameAsync()
        {
            var result = await _elicitationService.RequestAsync(
                new ElicitationPrompt(
                    "Provide a name",
                    new ElicitationSchema().AddProperty(
                        "name",
                        ElicitationSchemaProperty.ForString(title: "Name"),
                        required: true
                    )
                )
            );

            return new ToolCallResult
            {
                Content = new[]
                {
                    new TextContent
                    {
                        Text = result.Content?.GetProperty("name").GetString() ?? string.Empty,
                    },
                },
                IsError = false,
            };
        }
    }
}
