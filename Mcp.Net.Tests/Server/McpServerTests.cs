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
using Mcp.Net.Server.Services;
using Mcp.Net.Server.Models;
using Mcp.Net.Tests.TestUtils;

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
        _server = new McpServer(
            serverInfo,
            connectionManager,
            options,
            NullLoggerFactory.Instance
        );
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
        var response = await _server.ProcessJsonRpcRequest(request, "test-session");

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

        var response = await _server.ProcessJsonRpcRequest(request, "test-session");

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

        var response = await _server.ProcessJsonRpcRequest(request, "test-session");

        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be((int)ErrorCode.InvalidParams);
        response.Error!.Message.Should().Be("protocolVersion is required");
        _server.NegotiatedProtocolVersion.Should().BeNull();
    }

    [Fact]
    public async Task ProcessJsonRpcRequest_Initialize_Should_Track_Negotiated_Protocol_Per_Session()
    {
        var latestRequest = new JsonRpcRequestMessage(
            "2.0",
            "init-a",
            "initialize",
            JsonSerializer.SerializeToElement(
                new
                {
                    clientInfo = new ClientInfo { Name = "Client A", Version = "1.0" },
                    capabilities = new object(),
                    protocolVersion = McpServer.LatestProtocolVersion,
                }
            )
        );
        var legacyRequest = new JsonRpcRequestMessage(
            "2.0",
            "init-b",
            "initialize",
            JsonSerializer.SerializeToElement(
                new
                {
                    clientInfo = new ClientInfo { Name = "Client B", Version = "1.0" },
                    capabilities = new object(),
                    protocolVersion = "2024-11-05",
                }
            )
        );

        await _server.ProcessJsonRpcRequest(latestRequest, "session-a");
        await _server.ProcessJsonRpcRequest(legacyRequest, "session-b");

        _server.GetNegotiatedProtocolVersion("session-a").Should().Be(McpServer.LatestProtocolVersion);
        _server.GetNegotiatedProtocolVersion("session-b").Should().Be("2024-11-05");
        _server.NegotiatedProtocolVersion.Should().BeNull();
    }

    [Fact]
    public async Task ConnectAsync_Should_Ignore_Close_From_Replaced_Transport_When_Tracking_ProtocolVersion()
    {
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0" },
            connectionManager,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            NullLoggerFactory.Instance
        );

        var originalTransport = new MockTransport("session-reconnect");
        var replacementTransport = new MockTransport("session-reconnect");

        await server.ConnectAsync(originalTransport);
        await server.ProcessJsonRpcRequest(
            CreateInitializeRequest("init-original", McpServer.LatestProtocolVersion),
            "session-reconnect"
        );

        await server.ConnectAsync(replacementTransport);
        await server.ProcessJsonRpcRequest(
            CreateInitializeRequest("init-replacement", "2024-11-05"),
            "session-reconnect"
        );

        originalTransport.SimulateClose();
        await Task.Delay(50);

        server.GetNegotiatedProtocolVersion("session-reconnect").Should().Be("2024-11-05");
    }

    [Fact]
    public async Task ProcessJsonRpcRequest_Should_Return_Error_For_Unknown_Method()
    {
        // Arrange
        var request = new JsonRpcRequestMessage("2.0", "test-id", "unknown_method", null);

        // Act
        var response = await _server.ProcessJsonRpcRequest(request, "test-session");

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
        _server.RegisterTool(toolName, toolDescription, inputSchema, (args, _) => toolHandler(args));

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
        var response = await _server.ProcessJsonRpcRequest(request, "test-session");

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
    public async Task HandleRequestAsync_Should_PassSessionId_ToTool()
    {
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);

        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0" },
            connectionManager,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            NullLoggerFactory.Instance
        );

        server.RegisterTool(
            "echoSession",
            "Returns the current session id",
            JsonSerializer.SerializeToElement(new { type = "object" }),
            (_, sessionId) =>
                Task.FromResult(
                    new ToolCallResult
                    {
                        Content = new[] { new TextContent { Text = sessionId ?? "<none>" } },
                    }
                )
        );

        var request = new JsonRpcRequestMessage(
            "2.0",
            "call-1",
            "tools/call",
            JsonSerializer.SerializeToElement(new { name = "echoSession" })
        );

        var context = new ServerRequestContext("session-123", "transport-abc", request);

        var response = await server.HandleRequestAsync(context);

        response.Error.Should().BeNull();
        var result = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(response.Result)
        );
        result.Should().NotBeNull();
        result!.Content.Should().ContainSingle()
            .Which.Should().BeOfType<TextContent>()
            .Subject.As<TextContent>().Text.Should().Be("session-123");
    }

    [Fact]
    public async Task HandleRequestAsync_Should_PassRequestCancellationToken_ToResourceReader()
    {
        var resource = new Resource
        {
            Uri = "mcp://test/cancellable-resource",
            Name = "Cancellable Resource",
        };

        CancellationToken observedToken = default;
        _server.RegisterResource(
            resource,
            cancellationToken =>
            {
                observedToken = cancellationToken;
                return Task.FromResult(Array.Empty<ResourceContent>());
            }
        );

        using var cts = new CancellationTokenSource();
        var request = new JsonRpcRequestMessage(
            "2.0",
            "resource-read-context",
            "resources/read",
            JsonSerializer.SerializeToElement(new { uri = resource.Uri })
        );

        var context = new ServerRequestContext(
            "session-resource",
            "transport-resource",
            request,
            cts.Token
        );

        var response = await _server.HandleRequestAsync(context);

        response.Error.Should().BeNull();
        observedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task HandleRequestAsync_Should_PassRequestCancellationToken_ToPromptFactory()
    {
        var prompt = new Prompt
        {
            Name = "cancellable-prompt",
            Description = "Prompt used to verify request context propagation",
        };

        CancellationToken observedToken = default;
        _server.RegisterPrompt(
            prompt,
            cancellationToken =>
            {
                observedToken = cancellationToken;
                return Task.FromResult(Array.Empty<object>());
            }
        );

        using var cts = new CancellationTokenSource();
        var request = new JsonRpcRequestMessage(
            "2.0",
            "prompt-get-context",
            "prompts/get",
            JsonSerializer.SerializeToElement(new { name = prompt.Name })
        );

        var context = new ServerRequestContext(
            "session-prompt",
            "transport-prompt",
            request,
            cts.Token
        );

        var response = await _server.HandleRequestAsync(context);

        response.Error.Should().BeNull();
        observedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task HandleTransportClosed_Should_CancelPendingClientRequests()
    {
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0" },
            connectionManager,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            NullLoggerFactory.Instance
        )
        {
            ClientRequestTimeout = Timeout.InfiniteTimeSpan,
        };

        var transport = new Mock<IServerTransport>();
        transport.Setup(t => t.Id()).Returns("session-close");
        transport.Setup(t => t.SendRequestAsync(It.IsAny<JsonRpcRequestMessage>()))
            .Returns(Task.CompletedTask);

        await connectionManager.RegisterTransportAsync("session-close", transport.Object);

        var pendingTask = server.SendClientRequestAsync("session-close", "noop", null);

        server.HandleTransportClosed("session-close");

        await Assert.ThrowsAsync<OperationCanceledException>(() => pendingTask);
    }

    [Fact]
    public async Task HandleTransportClosed_Should_OnlyCancelPendingRequests_For_Session()
    {
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0" },
            connectionManager,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            NullLoggerFactory.Instance
        )
        {
            ClientRequestTimeout = Timeout.InfiniteTimeSpan,
        };

        var sessionA = new MockTransport("session-a");
        var sessionB = new MockTransport("session-b");

        await connectionManager.RegisterTransportAsync(sessionA.Id(), sessionA);
        await connectionManager.RegisterTransportAsync(sessionB.Id(), sessionB);

        var pendingA = server.SendClientRequestAsync(sessionA.Id(), "noop", null);
        var pendingB = server.SendClientRequestAsync(sessionB.Id(), "noop", null);

        sessionA.SentRequests.Should().ContainSingle();
        sessionB.SentRequests.Should().ContainSingle();

        server.HandleTransportClosed(sessionA.Id());

        await Assert.ThrowsAsync<OperationCanceledException>(() => pendingA);

        var responseB = new JsonRpcResponseMessage(
            "2.0",
            sessionB.SentRequests.Single().Id,
            new { ok = true },
            null
        );

        await server.HandleClientResponseAsync(sessionB.Id(), responseB);

        var resultB = await pendingB;
        resultB.Error.Should().BeNull();
    }

    [Fact]
    public async Task HandleClientResponseAsync_Should_NotComplete_Request_From_Different_Session()
    {
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0" },
            connectionManager,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            NullLoggerFactory.Instance
        );

        var sessionA = new MockTransport("session-a");
        var sessionB = new MockTransport("session-b");

        await connectionManager.RegisterTransportAsync(sessionA.Id(), sessionA);
        await connectionManager.RegisterTransportAsync(sessionB.Id(), sessionB);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var pending = server.SendClientRequestAsync(sessionA.Id(), "noop", null, cts.Token);

        sessionA.SentRequests.Should().ContainSingle();

        var response = new JsonRpcResponseMessage(
            "2.0",
            sessionA.SentRequests.Single().Id,
            new { ok = true },
            null
        );

        await server.HandleClientResponseAsync(sessionB.Id(), response);

        await Assert.ThrowsAsync<TaskCanceledException>(() => pending);
    }

    [Fact]
    public async Task HandleTransportError_Should_OnlyCancelPendingRequests_For_Session()
    {
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0" },
            connectionManager,
            new ServerOptions { Capabilities = new ServerCapabilities() },
            NullLoggerFactory.Instance
        )
        {
            ClientRequestTimeout = Timeout.InfiniteTimeSpan,
        };

        var sessionA = new MockTransport("session-a");
        var sessionB = new MockTransport("session-b");

        await server.ConnectAsync(sessionA);
        await server.ConnectAsync(sessionB);

        var pendingA = server.SendClientRequestAsync(sessionA.Id(), "noop", null);
        var pendingB = server.SendClientRequestAsync(sessionB.Id(), "noop", null);

        sessionA.SentRequests.Should().ContainSingle();
        sessionB.SentRequests.Should().ContainSingle();

        sessionA.SimulateError(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => pendingA);

        var responseB = new JsonRpcResponseMessage(
            "2.0",
            sessionB.SentRequests.Single().Id,
            new { ok = true },
            null
        );

        await server.HandleClientResponseAsync(sessionB.Id(), responseB);

        var resultB = await pendingB;
        resultB.Error.Should().BeNull();
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
            (_, _) => Task.FromResult(new ToolCallResult())
        );

        var request = new JsonRpcRequestMessage("2.0", "list-tools", "tools/list", null);

        // Act
        var response = await _server.ProcessJsonRpcRequest(request, "test-session");

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
            (_, _) => Task.FromResult(new ToolCallResult()),
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
        var listResponse = await _server.ProcessJsonRpcRequest(listRequest, "test-session");
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

        var readResponse = await _server.ProcessJsonRpcRequest(readRequest, "test-session");
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
        var listResponse = await _server.ProcessJsonRpcRequest(listRequest, "test-session");
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

        var getResponse = await _server.ProcessJsonRpcRequest(getRequest, "test-session");
        getResponse.Error.Should().BeNull();

        var promptPayload = JsonSerializer.SerializeToElement(getResponse.Result!);
        promptPayload.GetProperty("messages").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task RegisteringServerPrimitives_AfterInitialize_Should_Notify_Connected_Client_With_ListChangedNotifications()
    {
        var connectionManager = new InMemoryConnectionManager(NullLoggerFactory.Instance);
        var server = new McpServer(
            new ServerInfo { Name = "Test", Version = "1.0.0" },
            connectionManager,
            new ServerOptions
            {
                Capabilities = new ServerCapabilities
                {
                    Tools = new { listChanged = true },
                    Prompts = new { listChanged = true },
                    Resources = new { listChanged = true },
                },
            },
            NullLoggerFactory.Instance
        );

        var transport = new MockTransport("session-list-changed");
        await server.ConnectAsync(transport);

        var initializeResponse = await server.ProcessJsonRpcRequest(
            CreateInitializeRequest("init-list-changed", McpServer.LatestProtocolVersion),
            transport.Id()
        );

        initializeResponse.Error.Should().BeNull();

        server.RegisterTool(
            "dynamic.tool",
            "Tool registered after initialization",
            JsonSerializer.SerializeToElement(new { type = "object" }),
            (_, _) => Task.FromResult(new ToolCallResult())
        );

        server.RegisterPrompt(
            new Prompt
            {
                Name = "dynamic.prompt",
                Description = "Prompt registered after initialization",
            },
            _ => Task.FromResult(Array.Empty<object>())
        );

        server.RegisterResource(
            new Resource
            {
                Uri = "mcp://dynamic/resource",
                Name = "Dynamic Resource",
            },
            Array.Empty<ResourceContent>()
        );

        await WaitForNotificationCountAsync(transport, expectedCount: 3, timeout: TimeSpan.FromMilliseconds(300));

        transport.SentNotifications
            .Select(notification => notification.Method)
            .Should()
            .Equal(
                "notifications/tools/list_changed",
                "notifications/prompts/list_changed",
                "notifications/resources/list_changed"
            );
    }

    private static JsonRpcRequestMessage CreateInitializeRequest(
        string requestId,
        string protocolVersion
    ) =>
        new(
            "2.0",
            requestId,
            "initialize",
            JsonSerializer.SerializeToElement(
                new
                {
                    clientInfo = new ClientInfo { Name = "Test Client", Version = "1.0" },
                    capabilities = new object(),
                    protocolVersion,
                }
            )
        );

    private static async Task WaitForNotificationCountAsync(
        MockTransport transport,
        int expectedCount,
        TimeSpan timeout
    )
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (transport.SentNotifications.Count >= expectedCount)
            {
                return;
            }

            await Task.Delay(10);
        }

        transport.SentNotifications.Count.Should().BeGreaterThanOrEqualTo(expectedCount);
    }
}
