using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Core.Transport;
using Moq;
using Mcp.Net.Server.ConnectionManagers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Server;

public class McpServerTests
{
    private readonly Mock<IServerTransport> _mockTransport;
    private readonly McpServer _server;

    public McpServerTests()
    {
        _mockTransport = new Mock<IServerTransport>();
        _mockTransport.Setup(t => t.Id()).Returns("test-transport");

        var serverInfo = new ServerInfo { Name = "Test Server", Version = "1.0.0" };

        var options = new ServerOptions
        {
            Instructions = "Test server instructions",
            Capabilities = new ServerCapabilities(),
        };

        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        _server = new McpServer(serverInfo, connectionManager, options, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task ConnectAsync_Should_Subscribe_To_Transport_Events()
    {
        // Arrange
        _mockTransport.Setup(t => t.StartAsync()).Returns(Task.CompletedTask);

        // Act
        await _server.ConnectAsync(_mockTransport.Object);

        // Assert
        _mockTransport.Verify(t => t.StartAsync(), Times.Once);
    }

    [Fact]
    public async Task ProcessJsonRpcRequest_Initialize_Should_Return_ServerInfo()
    {
        // Arrange
        var paramsElement = JsonSerializer.SerializeToElement(
            new
            {
                clientInfo = new ClientInfo { Name = "Test Client", Version = "1.0" },
                capabilities = new object(),
                protocolVersion = McpServer.LatestProtocolVersion,
            }
        );

        var request = new JsonRpcRequestMessage("2.0", "test-id", "initialize", paramsElement);

        // Act
        var response = await _server.ProcessJsonRpcRequest(request);

        // Assert
        response.Id.Should().Be("test-id");
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var resultObj = JsonSerializer.SerializeToElement(response.Result);
        resultObj
            .GetProperty("serverInfo")
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("Test Server");
        resultObj.GetProperty("serverInfo").GetProperty("version").GetString().Should().Be("1.0.0");
        resultObj
            .GetProperty("serverInfo")
            .GetProperty("title")
            .GetString()
            .Should()
            .Be("Test Server");
        resultObj.GetProperty("instructions").GetString().Should().Be("Test server instructions");
        resultObj
            .GetProperty("protocolVersion")
            .GetString()
            .Should()
            .Be(McpServer.LatestProtocolVersion);
        var capabilities = resultObj.GetProperty("capabilities");
        capabilities.TryGetProperty("logging", out _).Should().BeFalse();
        capabilities.TryGetProperty("completions", out _).Should().BeFalse();
        _server.NegotiatedProtocolVersion.Should().Be(McpServer.LatestProtocolVersion);
    }

    [Fact]
    public async Task ProcessJsonRpcRequest_Initialize_Should_Fallback_To_Latest_When_Unsupported()
    {
        var paramsElement = JsonSerializer.SerializeToElement(
            new
            {
                clientInfo = new ClientInfo { Name = "Test Client", Version = "1.0" },
                capabilities = new object(),
                protocolVersion = "2023-01-01",
            }
        );

        var request = new JsonRpcRequestMessage("2.0", "test-id", "initialize", paramsElement);

        var response = await _server.ProcessJsonRpcRequest(request);

        response.Error.Should().BeNull();
        var resultObj = JsonSerializer.SerializeToElement(response.Result);
        resultObj
            .GetProperty("protocolVersion")
            .GetString()
            .Should()
            .Be(McpServer.LatestProtocolVersion);
        _server.NegotiatedProtocolVersion.Should().Be(McpServer.LatestProtocolVersion);
    }

    [Fact]
    public async Task ProcessJsonRpcRequest_Initialize_Should_Return_Error_When_ProtocolVersion_Missing()
    {
        var paramsElement = JsonSerializer.SerializeToElement(
            new
            {
                clientInfo = new ClientInfo { Name = "Test Client", Version = "1.0" },
                capabilities = new object(),
            }
        );

        var request = new JsonRpcRequestMessage("2.0", "test-id", "initialize", paramsElement);

        var response = await _server.ProcessJsonRpcRequest(request);

        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be((int)ErrorCode.InvalidParams);
        response.Error!.Message.Should().Be("protocolVersion is required");
        _server.NegotiatedProtocolVersion.Should().BeNull();
    }

    [Fact]
    public async Task ProcessJsonRpcRequest_Should_Return_Error_For_Unknown_Method()
    {
        // Arrange
        var request = new JsonRpcRequestMessage("2.0", "test-id", "unknown_method", null);

        // Act
        var response = await _server.ProcessJsonRpcRequest(request);

        // Assert
        response.Id.Should().Be("test-id");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601); // Method not found
    }

    [Fact]
    public async Task RegisterTool_And_HandleToolCall_Should_Execute_Tool()
    {
        // Arrange
        string toolName = "test_tool";
        string toolDescription = "A test tool";
        var inputSchema = JsonSerializer.SerializeToElement(
            new { type = "object", properties = new { message = new { type = "string" } } }
        );

        var toolHandler = new Func<JsonElement?, Task<ToolCallResult>>(args =>
        {
            var message = args!.Value.GetProperty("message").GetString();
            return Task.FromResult(
                new ToolCallResult
                {
                    Content = new[] { new TextContent { Text = $"Received message: {message}" } },
                    IsError = false,
                }
            );
        });

        // Register the tool
        _server.RegisterTool(toolName, toolDescription, inputSchema, toolHandler);

        // Create a tool call request
        var callParamsElement = JsonSerializer.SerializeToElement(
            new { name = toolName, arguments = new { message = "Hello, world!" } }
        );

        var request = new JsonRpcRequestMessage(
            "2.0",
            "tool-call",
            "tools/call",
            callParamsElement
        );

        // Act
        var response = await _server.ProcessJsonRpcRequest(request);

        // Assert
        response.Id.Should().Be("tool-call");
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var resultObj = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(response.Result)
        );
        resultObj.Should().NotBeNull();
        resultObj!.IsError.Should().BeFalse();
        resultObj.Content.Should().HaveCount(1);
        resultObj.Content.First().Should().BeOfType<TextContent>();
        ((TextContent)resultObj.Content.First())
            .Text.Should()
            .Be("Received message: Hello, world!");
    }

    [Fact]
    public async Task HandleToolsList_Should_Return_Registered_Tools()
    {
        // Arrange
        string toolName = "test_tool";
        string toolDescription = "A test tool";
        var inputSchema = JsonSerializer.SerializeToElement(new { type = "object" });

        _server.RegisterTool(
            toolName,
            toolDescription,
            inputSchema,
            _ => Task.FromResult(new ToolCallResult())
        );

        var request = new JsonRpcRequestMessage("2.0", "list-tools", "tools/list", null);

        // Act
        var response = await _server.ProcessJsonRpcRequest(request);

        // Assert
        response.Id.Should().Be("list-tools");
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var resultObj = JsonSerializer.SerializeToElement(response.Result);
        var tools = resultObj.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);

        var tool = tools[0];
        tool.GetProperty("name").GetString().Should().Be(toolName);
        tool.GetProperty("description").GetString().Should().Be(toolDescription);
        tool.GetProperty("inputSchema").Should().NotBeNull();
        tool.TryGetProperty("annotations", out _).Should().BeFalse();
    }

    [Fact]
    public async Task HandleToolsList_ShouldIncludeAnnotations_WhenProvided()
    {
        var annotations = new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["id"] = "server",
                ["displayName"] = "Server Tools",
            },
        };

        _server.RegisterTool(
            "annotated_tool",
            "Annotated",
            JsonSerializer.SerializeToElement(new { type = "object" }),
            _ => Task.FromResult(new ToolCallResult()),
            annotations
        );

        var request = new JsonRpcRequestMessage("2.0", "list-tools", "tools/list", null);
        var response = await _server.ProcessJsonRpcRequest(request);

        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var resultObj = JsonSerializer.SerializeToElement(response.Result);
        var annotated = resultObj
            .GetProperty("tools")
            .EnumerateArray()
            .First(t => t.GetProperty("name").GetString() == "annotated_tool");

        annotated.GetProperty("annotations").GetProperty("category").GetProperty("id").GetString().Should().Be("server");
        annotated
            .GetProperty("annotations")
            .GetProperty("category")
            .GetProperty("displayName")
            .GetString()
            .Should()
            .Be("Server Tools");
    }

    [Fact]
    public async Task RegisterResource_Should_List_And_Read()
    {
        var resource = new Resource
        {
            Uri = "mcp://test/resource",
            Name = "Test Resource",
            Description = "Resource used by unit test",
            MimeType = "text/plain",
        };

        var contents = new[]
        {
            new ResourceContent
            {
                Uri = resource.Uri,
                MimeType = "text/plain",
                Text = "Hello from the resource catalogue.",
            },
        };

        _server.RegisterResource(resource, contents);

        var listRequest = new JsonRpcRequestMessage("2.0", "res-list", "resources/list", null);
        var listResponse = await _server.ProcessJsonRpcRequest(listRequest);
        listResponse.Error.Should().BeNull();

        var listPayload = JsonSerializer.SerializeToElement(listResponse.Result!);
        listPayload
            .GetProperty("resources")
            .EnumerateArray()
            .Should()
            .ContainSingle(r => r.GetProperty("uri").GetString() == resource.Uri);

        var readParams = JsonSerializer.SerializeToElement(new { uri = resource.Uri });
        var readRequest = new JsonRpcRequestMessage(
            "2.0",
            "res-read",
            "resources/read",
            readParams
        );

        var readResponse = await _server.ProcessJsonRpcRequest(readRequest);
        readResponse.Error.Should().BeNull();

        var readPayload = JsonSerializer.SerializeToElement(readResponse.Result!);
        readPayload
            .GetProperty("contents")
            .EnumerateArray()
            .Should()
            .ContainSingle(c => c.GetProperty("text").GetString() == contents[0].Text);
    }

    [Fact]
    public async Task RegisterPrompt_Should_List_And_ReturnMessages()
    {
        var prompt = new Prompt
        {
            Name = "demo",
            Description = "Prompt used by unit test",
        };

        Task<object[]> MessageFactory(CancellationToken _)
        {
            object[] messages =
            {
                new
                {
                    role = "system",
                    content = new ContentBase[]
                    {
                        new TextContent { Text = "You are a test harness." },
                    },
                },
            };

            return Task.FromResult(messages);
        }

        _server.RegisterPrompt(prompt, MessageFactory);

        var listRequest = new JsonRpcRequestMessage("2.0", "prompt-list", "prompts/list", null);
        var listResponse = await _server.ProcessJsonRpcRequest(listRequest);
        listResponse.Error.Should().BeNull();

        var listPayload = JsonSerializer.SerializeToElement(listResponse.Result!);
        listPayload
            .GetProperty("prompts")
            .EnumerateArray()
            .Should()
            .ContainSingle(p => p.GetProperty("name").GetString() == prompt.Name);

        var getParams = JsonSerializer.SerializeToElement(new { name = prompt.Name });
        var getRequest = new JsonRpcRequestMessage(
            "2.0",
            "prompt-get",
            "prompts/get",
            getParams
        );

        var getResponse = await _server.ProcessJsonRpcRequest(getRequest);
        getResponse.Error.Should().BeNull();

        var promptPayload = JsonSerializer.SerializeToElement(getResponse.Result!);
        promptPayload.GetProperty("messages").GetArrayLength().Should().Be(1);
    }
}
