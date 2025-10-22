using System.Threading.Tasks;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Server;
using Mcp.Net.Core.JsonRpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

        return new McpServer(info, options, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task ToolRegistry_BindsCamelCaseParameterWithoutLowercasing()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var registry = CreateRegistry(services);
        registry.AddAssembly(typeof(CaseSensitiveTool).Assembly);

        var server = CreateServer();
        registry.RegisterToolsWithServer(server);

        var request = new JsonRpcRequestMessage(
            "2.0",
            "1",
            "tools/call",
            new { name = "caseSensitive.echo", arguments = new { userId = "Value42" } }
        );

        var response = await server.ProcessJsonRpcRequest(request);
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
        var services = new ServiceCollection().BuildServiceProvider();
        var registry = CreateRegistry(services);
        registry.AddAssembly(typeof(PascalCaseTool).Assembly);

        var server = CreateServer();
        registry.RegisterToolsWithServer(server);

        var request = new JsonRpcRequestMessage(
            "2.0",
            "9",
            "tools/call",
            new { name = "pascal.echo", arguments = new { ApiKey = "Snake" } }
        );

        var response = await server.ProcessJsonRpcRequest(request);
        Assert.Null(response.Error);

        var toolResult = Assert.IsType<ToolCallResult>(response.Result);
        Assert.False(toolResult.IsError);
        var text = Assert.IsType<TextContent>(Assert.Single(toolResult.Content));
        Assert.Equal("Snake", text.Text);
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
}
