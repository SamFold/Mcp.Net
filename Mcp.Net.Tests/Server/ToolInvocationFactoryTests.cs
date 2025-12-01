using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Server.Tools;
using Mcp.Net.Server;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Services;
using Mcp.Net.Core.Models.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Server;

public class ToolInvocationFactoryTests
{
    private readonly ToolDiscoveryService _discoveryService;

    public ToolInvocationFactoryTests()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        _discoveryService = new ToolDiscoveryService(
            loggerFactory.CreateLogger<ToolDiscoveryService>()
        );
    }

    [Fact]
    public async Task CreateHandler_UsesDefaultParameterValuesWhenMissing()
    {
        var descriptor = GetDescriptor("default.value");
        var services = new ServiceCollection()
            .AddSingleton(new McpServer(new ServerInfo { Name = "Test", Version = "1.0" }, new InMemoryConnectionManager(NullLoggerFactory.Instance), new ServerOptions(), NullLoggerFactory.Instance))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var factory = new ToolInvocationFactory(
            services,
            services.GetRequiredService<McpServer>(),
            services.GetRequiredService<ILoggerFactory>(),
            NullLoggerFactory.Instance.CreateLogger<ToolInvocationFactory>()
        );

        var handler = factory.CreateHandler(descriptor);
        var result = await handler(JsonSerializer.SerializeToElement(new { }), "session-1");

        Assert.False(result.IsError);
        var text = Assert.IsType<TextContent>(Assert.Single(result.Content));
        Assert.Equal("5", text.Text);
    }

    [Fact]
    public async Task CreateHandler_NormalizesTaskResults()
    {
        var descriptor = GetDescriptor("task.value");
        var services = new ServiceCollection()
            .AddSingleton(new McpServer(new ServerInfo { Name = "Test", Version = "1.0" }, new InMemoryConnectionManager(NullLoggerFactory.Instance), new ServerOptions(), NullLoggerFactory.Instance))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var factory = new ToolInvocationFactory(
            services,
            services.GetRequiredService<McpServer>(),
            services.GetRequiredService<ILoggerFactory>(),
            NullLoggerFactory.Instance.CreateLogger<ToolInvocationFactory>()
        );

        var handler = factory.CreateHandler(descriptor);
        var arguments = JsonSerializer.SerializeToElement(new { message = "hello world" });
        var result = await handler(arguments, "session-1");

        Assert.False(result.IsError);
        var text = Assert.IsType<TextContent>(Assert.Single(result.Content));
        Assert.Equal("HELLO WORLD", text.Text);
    }

    private ToolDescriptor GetDescriptor(string toolName)
    {
        var descriptors = _discoveryService.DiscoverTools(new[] { typeof(DefaultParameterTool).Assembly });
        return descriptors.Single(d => d.Name == toolName);
    }

    private class DefaultParameterTool
    {
        [McpTool("default.value", "Returns the provided count or its default")]
        public ToolCallResult Execute([McpParameter] int count = 5)
        {
            return new ToolCallResult
            {
                Content = new ContentBase[] { new TextContent { Text = count.ToString() } },
                IsError = false,
            };
        }

        [McpTool("task.value", "Uppercases the supplied message asynchronously")]
        public async Task<string> ExecuteAsync([McpParameter(required: true)] string message)
        {
            await Task.Delay(1);
            return message.ToUpperInvariant();
        }
    }
}
