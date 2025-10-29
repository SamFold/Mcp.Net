using System;
using System.Collections.Generic;
using System.Linq;
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
        var responses = await client.SendMessageAsync(
            new LlmMessage { Type = MessageType.User, Content = "hi" }
        );

        // Assert
        responses.Should().HaveCount(1);
        var response = responses.Single();
        response.Type.Should().Be(MessageType.Assistant);
        response.Content.Should().Be("Hello from Claude");
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
        var responses = await client.SendMessageAsync(
            new LlmMessage { Type = MessageType.User, Content = "find weather" }
        );

        // Assert
        responses.Should().HaveCount(1);
        var response = responses.Single();
        response.Type.Should().Be(MessageType.Tool);
        response.ToolCalls.Should().HaveCount(1);

        var invocation = response.ToolCalls.Single();
        invocation.Id.Should().Be("toolu_1");
        invocation.Name.Should().Be("search");
        invocation.Arguments.Should().ContainKey("query").WhoseValue.Should().Be("weather");
        invocation.Arguments.Should().ContainKey("includeForecast").WhoseValue.Should().Be(true);
    }

    [Fact]
    public async Task SendMessageAsync_FailedRequest_ShouldReturnSystemError()
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
        var responses = await client.SendMessageAsync(
            new LlmMessage { Type = MessageType.User, Content = "hello" }
        );

        // Assert
        responses.Should().HaveCount(1);
        var response = responses.Single();
        response.Type.Should().Be(MessageType.System);
        response.Content.Should().Contain("boom");
    }

    private sealed class StubAnthropicMessageClient : IAnthropicMessageClient
    {
        private readonly Queue<IReadOnlyList<ContentBase>> _responses;

        public StubAnthropicMessageClient(params IReadOnlyList<ContentBase>[] responses)
        {
            _responses = new Queue<IReadOnlyList<ContentBase>>(responses);
        }

        public Task<IReadOnlyList<ContentBase>> GetResponseContentAsync(MessageParameters parameters)
        {
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
