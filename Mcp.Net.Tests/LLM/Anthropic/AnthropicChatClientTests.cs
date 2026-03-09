using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Anthropic.SDK.Messaging;
using FluentAssertions;
using Mcp.Net.LLM.Anthropic;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.LLM.Anthropic;

public class AnthropicChatClientTests
{
    [Fact]
    public async Task SendMessageAsync_TextResponse_ShouldReturnAssistantMessage()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };

        var content = new ContentBase[]
        {
            new TextContent { Text = "Hello from Claude" },
        };

        var messageClient = new StubAnthropicMessageClient(content);
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        // Act
        var response = await client.SendMessageAsync("hi");

        // Assert
        response.Should().BeOfType<ChatClientAssistantTurn>();
        var assistantTurn = (ChatClientAssistantTurn)response;
        assistantTurn.Blocks.Should().ContainSingle();
        assistantTurn.Blocks[0].Should().BeOfType<TextAssistantBlock>().Which.Text.Should().Be("Hello from Claude");
    }

    [Fact]
    public async Task SendMessageAsync_ToolCall_ShouldParseToolInvocation()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };

        var toolContent = new ToolUseContent
        {
            Id = "toolu_1",
            Name = "search",
            Input = JsonNode.Parse("""{ "query": "weather", "includeForecast": true }"""),
        };

        var messageClient = new StubAnthropicMessageClient(new ContentBase[] { toolContent });
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        // Act
        var response = await client.SendMessageAsync("find weather");

        // Assert
        response.Should().BeOfType<ChatClientAssistantTurn>();
        var assistantTurn = (ChatClientAssistantTurn)response;
        var invocation = assistantTurn.Blocks.OfType<ToolCallAssistantBlock>().Single();
        invocation.ToolCallId.Should().Be("toolu_1");
        invocation.ToolName.Should().Be("search");
        invocation.Arguments.Should().ContainKey("query").WhoseValue.Should().Be("weather");
        invocation.Arguments.Should().ContainKey("includeForecast").WhoseValue.Should().Be(true);
    }

    [Fact]
    public async Task SendMessageAsync_ToolCallWithNestedObjectAndArray_ShouldPreserveStructuredValues()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };

        var toolContent = new ToolUseContent
        {
            Id = "toolu_nested",
            Name = "search",
            Input = JsonNode.Parse(
                """
                {
                  "query": "bugs",
                  "filter": {
                    "status": "open",
                    "labels": ["server", "urgent"]
                  },
                  "tags": ["triage", "backend"]
                }
                """
            ),
        };

        var messageClient = new StubAnthropicMessageClient(new ContentBase[] { toolContent });
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        // Act
        var response = await client.SendMessageAsync("find nested arguments");

        // Assert
        response.Should().BeOfType<ChatClientAssistantTurn>();
        var invocation = ((ChatClientAssistantTurn)response).Blocks.OfType<ToolCallAssistantBlock>().Single();
        invocation.Arguments.Should().ContainKey("query").WhoseValue.Should().Be("bugs");
        JsonSerializer.Serialize(invocation.Arguments["filter"])
            .Should()
            .Be("""{"status":"open","labels":["server","urgent"]}""");
        JsonSerializer.Serialize(invocation.Arguments["tags"])
            .Should()
            .Be("""["triage","backend"]""");
    }

    [Fact]
    public async Task SendMessageAsync_FailedRequest_ShouldReturnProviderFailure()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };

        var messageClient = new ThrowingAnthropicMessageClient(new InvalidOperationException("boom"));
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        // Act
        var response = await client.SendMessageAsync("hello");

        // Assert
        response.Should().BeOfType<ChatClientFailure>();
        var failure = (ChatClientFailure)response;
        failure.Source.Should().Be(ChatErrorSource.Provider);
        failure.Message.Should().Contain("boom");
    }

    [Fact]
    public async Task SendMessageAsync_WithConfiguredSystemPrompt_ShouldIncludePromptInOutboundRequest()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
            SystemPrompt = "Anthropic configured prompt",
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        // Act
        await client.SendMessageAsync("hello");

        // Assert
        messageClient.LastParameters.Should().NotBeNull();
        JsonSerializer.Serialize(messageClient.LastParameters).Should().Contain(options.SystemPrompt);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutConfiguredSystemPrompt_ShouldIncludeDefaultPromptInOutboundRequest()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        // Act
        await client.SendMessageAsync("hello");

        // Assert
        messageClient.LastParameters.Should().NotBeNull();
        client.GetSystemPrompt().Should().NotBeNullOrWhiteSpace();
        JsonSerializer.Serialize(messageClient.LastParameters)
            .Should()
            .Contain(client.GetSystemPrompt());
    }

    private sealed class StubAnthropicMessageClient : IAnthropicMessageClient
    {
        private readonly Queue<IReadOnlyList<ContentBase>> _responses;
        public MessageParameters? LastParameters { get; private set; }

        public StubAnthropicMessageClient(params IReadOnlyList<ContentBase>[] responses)
        {
            _responses = new Queue<IReadOnlyList<ContentBase>>(responses);
        }

        public Task<IReadOnlyList<ContentBase>> GetResponseContentAsync(MessageParameters parameters)
        {
            LastParameters = parameters;

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No stubbed responses configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ThrowingAnthropicMessageClient : IAnthropicMessageClient
    {
        private readonly Exception _exception;

        public ThrowingAnthropicMessageClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<IReadOnlyList<ContentBase>> GetResponseContentAsync(MessageParameters parameters)
        {
            return Task.FromException<IReadOnlyList<ContentBase>>(_exception);
        }
    }
}
