using System;
using System.Collections.Generic;
using System.IO;
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
    public async Task SendMessageAsync_ProbeReasoningResponse_ShouldReturnReasoningAndTextBlocksInOrder()
    {
        var fixture = AnthropicReasoningFixture.Load();
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-6",
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[]
            {
                new ThinkingContent
                {
                    Thinking = fixture.Thinking,
                    Signature = fixture.Signature,
                },
                new TextContent { Text = fixture.Text },
            }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        var response = await client.SendMessageAsync("Count r characters");

        response.Should().BeOfType<ChatClientAssistantTurn>();
        var assistantTurn = (ChatClientAssistantTurn)response;
        assistantTurn.Blocks.Should().HaveCount(2);
        assistantTurn
            .Blocks[0]
            .Should()
            .BeEquivalentTo(
                new ReasoningAssistantBlock(
                    assistantTurn.Blocks[0].Id,
                    fixture.Thinking,
                    ReasoningVisibility.Visible,
                    fixture.Signature
                )
            );
        assistantTurn.Blocks[1].Should().BeOfType<TextAssistantBlock>().Which.Text.Should().Be(fixture.Text);
    }

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

    [Fact]
    public async Task LoadReplayTranscript_ShouldIncludePriorHistoryInOutboundRequest()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        client.LoadReplayTranscript(
            new Mcp.Net.LLM.Replay.ProviderReplayTranscript(
                client.GetReplayTarget(),
                new ChatTranscriptEntry[]
                {
                    new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-3), "Use the tool", "turn-1"),
                    new AssistantChatEntry(
                        "assistant-1",
                        DateTimeOffset.UtcNow.AddMinutes(-2),
                        new AssistantContentBlock[]
                        {
                            new TextAssistantBlock("text-1", "Checking"),
                            new ToolCallAssistantBlock(
                                "tool-1",
                                "toolu_1",
                                "search",
                                new Dictionary<string, object?> { ["query"] = "weather" }
                            ),
                        },
                        "turn-1",
                        "anthropic",
                        "claude-sonnet-4-5-20250929"
                    ),
                    new ToolResultChatEntry(
                        "tool-result-1",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        "toolu_1",
                        "search",
                        new ToolInvocationResult(
                            "toolu_1",
                            "search",
                            false,
                            new[] { "sunny" },
                            structured: null,
                            resourceLinks: Array.Empty<ToolResultResourceLink>(),
                            metadata: null
                        ),
                        false,
                        "turn-1"
                    ),
                }
            )
        );

        await client.SendMessageAsync("continue");

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Messages.Should().HaveCount(4);
        messageClient.LastParameters.Messages[0].Role.Should().Be(RoleType.User);
        messageClient.LastParameters.Messages[1].Role.Should().Be(RoleType.Assistant);
        messageClient.LastParameters.Messages[2].Role.Should().Be(RoleType.User);
        messageClient.LastParameters.Messages[3].Role.Should().Be(RoleType.User);

        var assistantContent = messageClient.LastParameters.Messages[1].Content;
        assistantContent.OfType<ToolUseContent>().Should().ContainSingle();
        assistantContent
            .OfType<TextContent>()
            .Select(content => content.Text)
            .Should()
            .ContainSingle()
            .Which.Should().Be("Checking");

        var toolResultContent = messageClient.LastParameters.Messages[2]
            .Content
            .Single()
            .Should()
            .BeOfType<ToolResultContent>()
            .Which;
        toolResultContent.ToolUseId.Should().Be("toolu_1");
        toolResultContent.Content.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadReplayTranscript_VisibleReasoningWithoutReplayToken_ShouldReplayAsTextContent()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-6",
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        client.LoadReplayTranscript(
            new Mcp.Net.LLM.Replay.ProviderReplayTranscript(
                client.GetReplayTarget(),
                new ChatTranscriptEntry[]
                {
                    new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-2), "Count r characters", "turn-1"),
                    new AssistantChatEntry(
                        "assistant-1",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        new AssistantContentBlock[]
                        {
                            new ReasoningAssistantBlock(
                                "reasoning-1",
                                "Let me count first.",
                                ReasoningVisibility.Visible
                            ),
                            new TextAssistantBlock("text-1", "There are three."),
                        },
                        "turn-1",
                        "anthropic",
                        "claude-sonnet-4-6"
                    ),
                }
            )
        );

        await client.SendMessageAsync("continue");

        var assistantContent = messageClient.LastParameters!.Messages[1].Content;
        assistantContent.Should().HaveCount(2);
        assistantContent.Should().AllSatisfy(content => content.Should().BeOfType<TextContent>());
        assistantContent
            .OfType<TextContent>()
            .Select(content => content.Text)
            .Should()
            .ContainInOrder("Let me count first.", "There are three.");
    }

    private sealed record AnthropicReasoningFixture(string Thinking, string Signature, string Text)
    {
        public static AnthropicReasoningFixture Load()
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(FindRepoRootFile("artifacts/llm-probe/anthropic-reasoning.json"))
            );

            var payloadContent = document
                .RootElement[0]
                .GetProperty("Payload")
                .GetProperty("Content");

            return new AnthropicReasoningFixture(
                payloadContent[0].GetProperty("Thinking").GetString()!,
                payloadContent[0].GetProperty("Signature").GetString()!,
                payloadContent[1].GetProperty("Text").GetString()!
            );
        }
    }

    private static string FindRepoRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mcp.Net.sln")))
            {
                return Path.Combine(directory.FullName, relativePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
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
