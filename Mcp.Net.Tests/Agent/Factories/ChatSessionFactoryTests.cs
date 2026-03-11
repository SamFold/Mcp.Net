using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Factories;

public class ChatSessionFactoryTests
{
    [Fact]
    public async Task Create_ShouldReturnConfiguredChatSession()
    {
        var llmClient = new Mock<IChatClient>();
        var toolExecutor = new Mock<IToolExecutor>();
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);
        var registeredTool = CreateToolDescriptor("search", "Searches documents");
        var configuration = new ChatSessionConfiguration
        {
            SystemPrompt = "Be concise.",
            Tools = new[] { registeredTool },
            RequestDefaults = new ChatRequestOptions { Temperature = 0.25f, MaxOutputTokens = 1024 },
        };
        ChatClientRequest? capturedRequest = null;

        llmClient
            .Setup(client => client.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatClientRequest, CancellationToken>((request, _) => capturedRequest = request)
            .Returns(
                ChatCompletionStream.FromResult(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                    )
                )
            );

        var session = factory.Create(llmClient.Object, toolExecutor.Object, configuration);

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.SystemPrompt.Should().Be("Be concise.");
        capturedRequest.Tools.Select(tool => tool.Name).Should().Equal("search");
        capturedRequest.Options.Should().NotBeNull();
        capturedRequest.Options!.Temperature.Should().Be(0.25f);
        capturedRequest.Options.MaxOutputTokens.Should().Be(1024);
    }

    [Fact]
    public async Task CreateAsync_LocalToolsOnly_ShouldConfigureLocalToolDescriptorsAndExecutor()
    {
        var llmClient = new Mock<IChatClient>();
        var localTool = new TestLocalTool(
            CreateToolDescriptor("search_local", "Searches local documents"),
            (_, _) => Task.FromResult(CreateToolResult("call-1", "search_local", "sunny"))
        );
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);
        var capturedRequests = new List<ChatClientRequest>();

        var providerCallCount = 0;
        llmClient
            .Setup(client => client.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest request, CancellationToken _) =>
                {
                    capturedRequests.Add(request);
                    providerCallCount++;

                    return providerCallCount == 1
                        ? ChatCompletionStream.FromResult(
                            new ChatClientAssistantTurn(
                                "turn-1",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[]
                                {
                                    new ToolCallAssistantBlock(
                                        "tool-block-1",
                                        "call-1",
                                        "search_local",
                                        new Dictionary<string, object?> { ["query"] = "weather" }
                                    ),
                                }
                            )
                        )
                        : ChatCompletionStream.FromResult(
                            new ChatClientAssistantTurn(
                                "turn-2",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                            )
                        );
                }
            );

        var session = await factory.CreateAsync(
            llmClient.Object,
            new ChatSessionFactoryOptions
            {
                SystemPrompt = "Be helpful.",
                LocalTools = new[] { localTool },
            }
        );

        await session.SendUserMessageAsync("hello");

        capturedRequests.Should().HaveCount(2);
        capturedRequests[0].Tools.Select(tool => tool.Name).Should().Equal("search_local");
        session.Transcript
            .OfType<ToolResultChatEntry>()
            .Single()
            .Result.Text.Should().ContainSingle()
            .Which.Should().Be("sunny");
    }

    [Fact]
    public async Task CreateAsync_McpClientOnly_ShouldDiscoverRemoteToolsAndUseMcpToolExecutor()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>(MockBehavior.Strict);
        var remoteTool = CreateToolDescriptor("search_remote", "Searches remote documents");
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);
        var capturedRequests = new List<ChatClientRequest>();

        mcpClient.Setup(client => client.ListTools()).ReturnsAsync(new[] { remoteTool });
        mcpClient
            .Setup(client => client.CallTool(
                "search_remote",
                It.IsAny<object?>()
            ))
            .ReturnsAsync(CreateMcpToolCallResult("rainy"));

        var providerCallCount = 0;
        llmClient
            .Setup(client => client.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest request, CancellationToken _) =>
                {
                    capturedRequests.Add(request);
                    providerCallCount++;

                    return providerCallCount == 1
                        ? ChatCompletionStream.FromResult(
                            new ChatClientAssistantTurn(
                                "turn-1",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[]
                                {
                                    new ToolCallAssistantBlock(
                                        "tool-block-1",
                                        "call-1",
                                        "search_remote",
                                        new Dictionary<string, object?> { ["query"] = "weather" }
                                    ),
                                }
                            )
                        )
                        : ChatCompletionStream.FromResult(
                            new ChatClientAssistantTurn(
                                "turn-2",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                            )
                        );
                }
            );

        var session = await factory.CreateAsync(
            llmClient.Object,
            new ChatSessionFactoryOptions { McpClient = mcpClient.Object }
        );

        await session.SendUserMessageAsync("hello");

        mcpClient.Verify(client => client.ListTools(), Times.Once);
        mcpClient.Verify(client => client.CallTool("search_remote", It.IsAny<object?>()), Times.Once);
        capturedRequests[0].Tools.Select(tool => tool.Name).Should().Equal("search_remote");
        session.Transcript
            .OfType<ToolResultChatEntry>()
            .Single()
            .Result.Text.Should().ContainSingle()
            .Which.Should().Be("rainy");
    }

    [Fact]
    public async Task CreateAsync_LocalAndMcpTools_ShouldComposeCompositeExecutor()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>(MockBehavior.Strict);
        var localTool = new TestLocalTool(
            CreateToolDescriptor("search_local", "Searches local documents"),
            (_, _) => Task.FromResult(CreateToolResult("call-1", "search_local", "sunny"))
        );
        var remoteTool = CreateToolDescriptor("search_remote", "Searches remote documents");
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);
        var capturedRequests = new List<ChatClientRequest>();

        mcpClient.Setup(client => client.ListTools()).ReturnsAsync(new[] { remoteTool });
        mcpClient
            .Setup(client => client.CallTool("search_remote", It.IsAny<object?>()))
            .ReturnsAsync(CreateMcpToolCallResult("rainy"));

        var providerCallCount = 0;
        llmClient
            .Setup(client => client.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest request, CancellationToken _) =>
                {
                    capturedRequests.Add(request);
                    providerCallCount++;

                    return providerCallCount == 1
                        ? ChatCompletionStream.FromResult(
                            new ChatClientAssistantTurn(
                                "turn-1",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[]
                                {
                                    new ToolCallAssistantBlock(
                                        "tool-block-1",
                                        "call-1",
                                        "search_local",
                                        new Dictionary<string, object?> { ["query"] = "weather" }
                                    ),
                                    new ToolCallAssistantBlock(
                                        "tool-block-2",
                                        "call-2",
                                        "search_remote",
                                        new Dictionary<string, object?> { ["query"] = "forecast" }
                                    ),
                                }
                            )
                        )
                        : ChatCompletionStream.FromResult(
                            new ChatClientAssistantTurn(
                                "turn-2",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                            )
                        );
                }
            );

        var session = await factory.CreateAsync(
            llmClient.Object,
            new ChatSessionFactoryOptions
            {
                LocalTools = new[] { localTool },
                McpClient = mcpClient.Object,
            }
        );

        await session.SendUserMessageAsync("hello");

        capturedRequests[0].Tools.Select(tool => tool.Name).Should().Equal("search_local", "search_remote");
        session.Transcript
            .OfType<ToolResultChatEntry>()
            .Select(entry => entry.ToolName)
            .Should()
            .Equal("search_local", "search_remote");
        mcpClient.Verify(client => client.CallTool("search_remote", It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenToolNamesConflict_ShouldThrowInvalidOperationException()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>(MockBehavior.Strict);
        var localTool = new TestLocalTool(
            CreateToolDescriptor("search", "Searches local documents"),
            (_, _) => Task.FromResult(CreateToolResult("call-1", "search", "sunny"))
        );
        var remoteTool = CreateToolDescriptor("search", "Searches remote documents");
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);

        mcpClient.Setup(client => client.ListTools()).ReturnsAsync(new[] { remoteTool });

        var act = () => factory.CreateAsync(
            llmClient.Object,
            new ChatSessionFactoryOptions
            {
                LocalTools = new[] { localTool },
                McpClient = mcpClient.Object,
            }
        );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate tool name*");
    }

    [Fact]
    public async Task CreateAsync_WhenNoToolsAreConfigured_ShouldCreateSessionWithEmptyToolCatalog()
    {
        var llmClient = new Mock<IChatClient>();
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);
        ChatClientRequest? capturedRequest = null;

        llmClient
            .Setup(client => client.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatClientRequest, CancellationToken>((request, _) => capturedRequest = request)
            .Returns(
                ChatCompletionStream.FromResult(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                    )
                )
            );

        var session = await factory.CreateAsync(llmClient.Object, new ChatSessionFactoryOptions());

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldNotDisposeProvidedMcpClient()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>(MockBehavior.Strict);
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);

        mcpClient.Setup(client => client.ListTools()).ReturnsAsync(Array.Empty<Tool>());
        llmClient
            .Setup(client => client.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                ChatCompletionStream.FromResult(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                    )
                )
            );

        var session = await factory.CreateAsync(
            llmClient.Object,
            new ChatSessionFactoryOptions { McpClient = mcpClient.Object }
        );

        await session.SendUserMessageAsync("hello");

        mcpClient.Verify(client => client.Dispose(), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldPreserveConfiguredRequestDefaultsAndSystemPrompt()
    {
        var llmClient = new Mock<IChatClient>();
        var factory = new Mcp.Net.Agent.Factories.ChatSessionFactory(NullLoggerFactory.Instance);
        ChatClientRequest? capturedRequest = null;

        llmClient
            .Setup(client => client.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatClientRequest, CancellationToken>((request, _) => capturedRequest = request)
            .Returns(
                ChatCompletionStream.FromResult(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                    )
                )
            );

        var session = await factory.CreateAsync(
            llmClient.Object,
            new ChatSessionFactoryOptions
            {
                SystemPrompt = "Be concise.",
                RequestDefaults = new ChatRequestOptions { Temperature = 0.4f, MaxOutputTokens = 2048 },
            }
        );

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.SystemPrompt.Should().Be("Be concise.");
        capturedRequest.Options.Should().NotBeNull();
        capturedRequest.Options!.Temperature.Should().Be(0.4f);
        capturedRequest.Options.MaxOutputTokens.Should().Be(2048);
    }

    private static Tool CreateToolDescriptor(string name, string description)
    {
        using var schemaDocument = JsonDocument.Parse("{}");
        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = schemaDocument.RootElement.Clone(),
        };
    }

    private static ToolInvocationResult CreateToolResult(
        string toolCallId,
        string toolName,
        params string[] text
    ) =>
        new(
            toolCallId,
            toolName,
            false,
            text,
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );

    private static ToolCallResult CreateMcpToolCallResult(params string[] text) =>
        new()
        {
            IsError = false,
            Content = text.Select(fragment => new TextContent { Text = fragment }).ToArray(),
        };

    private sealed class TestLocalTool(
        Tool descriptor,
        Func<RuntimeToolInvocation, CancellationToken, Task<ToolInvocationResult>> executeAsync
    ) : ILocalTool
    {
        public Tool Descriptor { get; } = descriptor;

        public Task<ToolInvocationResult> ExecuteAsync(
            RuntimeToolInvocation invocation,
            CancellationToken cancellationToken = default
        ) => executeAsync(invocation, cancellationToken);
    }
}
