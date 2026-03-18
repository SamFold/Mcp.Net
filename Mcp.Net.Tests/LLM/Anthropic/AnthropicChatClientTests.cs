using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Anthropic.SDK.Messaging;
using FluentAssertions;
using Mcp.Net.LLM.Anthropic;
using Mcp.Net.LLM.Interfaces;
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

        var response = await client.SendAsync(
            CreateRequest(string.Empty, CreateUserTranscript("Count r characters"))
        ).GetResultAsync();

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

        var messageClient = new StubAnthropicMessageClient(
            new MessageResponse
            {
                Content = new List<ContentBase>
                {
                    new TextContent { Text = "Hello from Claude" },
                },
                StopReason = "end_turn",
                Usage = CreateUsage(inputTokens: 11, outputTokens: 7, cacheCreationInputTokens: 4),
            }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        // Act
        var response = await client.SendAsync(
            CreateRequest(string.Empty, CreateUserTranscript("hi"))
        ).GetResultAsync();

        // Assert
        response.Should().BeOfType<ChatClientAssistantTurn>();
        var assistantTurn = (ChatClientAssistantTurn)response;
        assistantTurn.Blocks.Should().ContainSingle();
        assistantTurn.Blocks[0].Should().BeOfType<TextAssistantBlock>().Which.Text.Should().Be("Hello from Claude");
        assistantTurn.StopReason.Should().Be("end_turn");
        assistantTurn.Usage.Should().NotBeNull();
        assistantTurn.Usage!.InputTokens.Should().Be(11);
        assistantTurn.Usage.OutputTokens.Should().Be(7);
        assistantTurn.Usage.TotalTokens.Should().Be(18);
        assistantTurn.Usage.AdditionalCounts.Should().Contain("cacheCreationInputTokens", 4);
    }

    [Fact]
    public async Task SendAsync_ShouldUseOnlyRequestToolsInOutboundRequest()
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

        var tools = new[]
        {
            CreateTool("search", "Search tool"),
            CreateTool("calculate", "Calculator"),
        };

        await client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("hello"), tools))
            .GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Tools.Should().HaveCount(2);
        messageClient.LastParameters.Tools
            .Select(t => t.Function.Name)
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SendAsync_TwoCallsWithDifferentToolSets_ShouldNotRetainPriorToolState()
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

        var firstSet = new[]
        {
            CreateTool("search", "Search tool"),
        };
        var secondSet = new[]
        {
            CreateTool("calculate", "Calculator"),
            CreateTool("weather", "Weather tool"),
        };

        await client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("first"), firstSet))
            .GetResultAsync();
        await client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("hello"), secondSet))
            .GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Tools.Should().HaveCount(2);
        messageClient.LastParameters.Tools
            .Select(t => t.Function.Name)
            .Should()
            .BeEquivalentTo("calculate", "weather");
    }

    [Fact]
    public async Task SendAsync_WithImageUserContent_ShouldMapUserMessageParts()
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

        await client.SendAsync(
            CreateRequest(
                string.Empty,
                [
                    new UserChatEntry(
                        "user-1",
                        DateTimeOffset.UtcNow,
                        new UserContentPart[]
                        {
                            new TextUserContentPart("Describe this image."),
                            new InlineImageUserContentPart(
                                BinaryData.FromBytes([4, 5, 6]),
                                "image/jpeg"
                            ),
                        },
                        "turn-1"
                    ),
                ]
            )
        ).GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Messages.Should().ContainSingle();

        var userMessage = messageClient.LastParameters.Messages[0];
        userMessage.Role.Should().Be(RoleType.User);
        userMessage.Content.Should().HaveCount(2);
        userMessage.Content[0].Should().BeOfType<TextContent>().Which.Text.Should().Be("Describe this image.");

        var imageContent = userMessage.Content[1].Should().BeOfType<ImageContent>().Subject;
        imageContent.Source.Type.Should().Be(SourceType.base64);
        imageContent.Source.MediaType.Should().Be("image/jpeg");
        Convert.FromBase64String(imageContent.Source.Data).Should().Equal([4, 5, 6]);
    }

    [Fact]
    public async Task SendAsync_WithImageGenerationOptions_ShouldReturnFailure()
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

        var result = await client.SendAsync(
            CreateRequest(
                string.Empty,
                CreateUserTranscript("Generate an image."),
                options: new ChatRequestOptions
                {
                    ImageGeneration = new ChatImageGenerationOptions(),
                }
            )
        ).GetResultAsync();

        var failure = result.Should().BeOfType<ChatClientFailure>().Subject;
        failure.Source.Should().Be(ChatErrorSource.Session);
        failure.Message.Should().Contain("image generation");
        messageClient.LastParameters.Should().BeNull();
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
        var response = await client.SendAsync(
            CreateRequest(string.Empty, CreateUserTranscript("find weather"))
        ).GetResultAsync();

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
        var response = await client.SendAsync(
            CreateRequest(string.Empty, CreateUserTranscript("find nested arguments"))
        ).GetResultAsync();

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
        var response = await client.SendAsync(
            CreateRequest(string.Empty, CreateUserTranscript("hello"))
        ).GetResultAsync();

        // Assert
        response.Should().BeOfType<ChatClientFailure>();
        var failure = (ChatClientFailure)response;
        failure.Source.Should().Be(ChatErrorSource.Provider);
        failure.Message.Should().Contain("boom");
    }

    [Fact]
    public async Task SendMessageAsync_WithRequestSystemPrompt_ShouldIncludePromptInOutboundRequest()
    {
        // Arrange
        const string systemPrompt = "Anthropic configured prompt";
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
        await client.SendAsync(
            CreateRequest(systemPrompt, CreateUserTranscript("hello"))
        ).GetResultAsync();

        // Assert
        messageClient.LastParameters.Should().NotBeNull();
        JsonSerializer.Serialize(messageClient.LastParameters).Should().Contain(systemPrompt);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutConfiguredSystemPrompt_ShouldNotInjectPromptInOutboundRequest()
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
        await client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("hello")))
            .GetResultAsync();

        // Assert
        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.System.Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessageAsync_WithoutRequestSharedOptions_ShouldUseAnthropicDefaultMaxTokens()
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

        await client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("hello")))
            .GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.MaxTokens.Should().Be(1024);
    }

    [Fact]
    public async Task SendMessageAsync_WithRequestTemperatureAndMaxOutputTokens_ShouldIncludeRequestOptionsInOutboundRequest()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };
        var requestOptions = new ChatRequestOptions { Temperature = 0.2f, MaxOutputTokens = 888 };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        await client.SendAsync(
            CreateRequest(
                string.Empty,
                CreateUserTranscript("hello"),
                options: requestOptions
            )
        ).GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Temperature.Should().Be(0.2m);
        messageClient.LastParameters.MaxTokens.Should().Be(888);
    }

    [Fact]
    public async Task SendMessageAsync_WithRequestOptions_ShouldSetOnlyRequestValues()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };
        var requestOptions = new ChatRequestOptions { Temperature = 0.1f, MaxOutputTokens = 999 };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        await client.SendAsync(
            CreateRequest(
                string.Empty,
                CreateUserTranscript("hello"),
                options: requestOptions
            )
        ).GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Temperature.Should().Be(0.1m);
        messageClient.LastParameters.MaxTokens.Should().Be(999);
    }

    [Theory]
    [InlineData(ChatToolChoiceKind.Auto, ToolChoiceType.Auto)]
    [InlineData(ChatToolChoiceKind.Required, ToolChoiceType.Any)]
    public async Task SendMessageAsync_WithPredefinedRequestToolChoice_ShouldMapToolChoice(
        ChatToolChoiceKind choiceKind,
        ToolChoiceType expectedType
    )
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };
        var requestOptions = new ChatRequestOptions
        {
            ToolChoice = choiceKind switch
            {
                ChatToolChoiceKind.Auto => ChatToolChoice.Auto,
                ChatToolChoiceKind.Required => ChatToolChoice.Required,
                _ => throw new InvalidOperationException("Unexpected tool choice kind."),
            },
        };
        var tools = new[]
        {
            CreateTool("search", "Search tool"),
            CreateTool("calculate", "Calculator"),
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        await client.SendAsync(
            CreateRequest(
                string.Empty,
                CreateUserTranscript("hello"),
                tools,
                requestOptions
            )
        ).GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Tools.Should().HaveCount(2);
        messageClient.LastParameters.ToolChoice.Should().NotBeNull();
        messageClient.LastParameters.ToolChoice!.Type.Should().Be(expectedType);
    }

    [Fact]
    public async Task SendMessageAsync_WithSpecificRequestToolChoice_ShouldMapToolChoiceName()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };
        var requestOptions = new ChatRequestOptions
        {
            ToolChoice = ChatToolChoice.ForTool("search"),
        };
        var tools = new[]
        {
            CreateTool("search", "Search tool"),
            CreateTool("calculate", "Calculator"),
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        await client.SendAsync(
            CreateRequest(
                string.Empty,
                CreateUserTranscript("hello"),
                tools,
                requestOptions
            )
        ).GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Tools.Should().HaveCount(2);
        messageClient.LastParameters.ToolChoice.Should().NotBeNull();
        messageClient.LastParameters.ToolChoice!.Type.Should().Be(ToolChoiceType.Tool);
        messageClient.LastParameters.ToolChoice.Name.Should().Be("search");
    }

    [Fact]
    public async Task SendMessageAsync_WithRequestToolChoiceNone_ShouldSuppressToolsForAnthropic()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-5-20250929",
        };
        var requestOptions = new ChatRequestOptions
        {
            ToolChoice = ChatToolChoice.None,
        };
        var tools = new[]
        {
            CreateTool("search", "Search tool"),
            CreateTool("calculate", "Calculator"),
        };

        var messageClient = new StubAnthropicMessageClient(
            new ContentBase[] { new TextContent { Text = "ok" } }
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        await client.SendAsync(
            CreateRequest(
                string.Empty,
                CreateUserTranscript("hello"),
                tools,
                requestOptions
            )
        ).GetResultAsync();

        messageClient.LastParameters.Should().NotBeNull();
        messageClient.LastParameters!.Tools.Should().BeEmpty();
        messageClient.LastParameters.ToolChoice.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ShouldIncludePriorHistoryInOutboundRequest()
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

        await client.SendAsync(
            CreateRequest(
                string.Empty,
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
                    new UserChatEntry("user-2", DateTimeOffset.UtcNow, "continue", "turn-2"),
                }
            )
        ).GetResultAsync();

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
    public async Task SendAsync_VisibleReasoningWithoutReplayToken_ShouldReplayAsTextContent()
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

        await client.SendAsync(
            CreateRequest(
                string.Empty,
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
                    new UserChatEntry("user-2", DateTimeOffset.UtcNow, "continue", "turn-2"),
                }
            )
        ).GetResultAsync();

        var assistantContent = messageClient.LastParameters!.Messages[1].Content;
        assistantContent.Should().HaveCount(2);
        assistantContent.Should().AllSatisfy(content => content.Should().BeOfType<TextContent>());
        assistantContent
            .OfType<TextContent>()
            .Select(content => content.Text)
            .Should()
            .ContainInOrder("Let me count first.", "There are three.");
    }

    [Fact]
    public async Task SendMessageAsync_WithAssistantTurnUpdates_ShouldReportReasoningAndTextSnapshotsInOrder()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-6",
        };

        var messageClient = new StubAnthropicMessageClient(
            responses: Array.Empty<IReadOnlyList<ContentBase>>(),
            streamingResponses:
            [
                new MessageResponse[]
                {
                    CreateMessageStart(CreateUsage(inputTokens: 15, cacheReadInputTokens: 3)),
                    CreateContentBlockStart("thinking"),
                    CreateThinkingDelta("Let me count"),
                    CreateSignatureDelta("sig_123"),
                    CreateContentBlockStop(),
                    CreateContentBlockStart("text"),
                    CreateTextDelta("There are three."),
                    CreateContentBlockStop(),
                    CreateMessageDelta("end_turn", CreateUsage(outputTokens: 9)),
                },
            ]
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        var (streamedTurns, result) = await ExecuteStreamAsync(
            client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("Count r characters")))
        );

        streamedTurns.Should().HaveCount(4);
        streamedTurns.Select(turn => turn.Id).Distinct().Should().ContainSingle();

        streamedTurns[0].Blocks.Should().ContainSingle();
        streamedTurns[0].Blocks[0]
            .Should()
            .BeOfType<ReasoningAssistantBlock>()
            .Which.Text.Should()
            .Be("Let me count");

        streamedTurns[1].Blocks.Should().ContainSingle();
        var streamedReasoning = streamedTurns[1]
            .Blocks[0]
            .Should()
            .BeOfType<ReasoningAssistantBlock>()
            .Which;
        streamedReasoning.Text.Should().Be("Let me count");
        streamedReasoning.ReplayToken.Should().Be("sig_123");

        streamedTurns[2].Blocks.Should().HaveCount(2);
        streamedTurns[2].Blocks[0].Id.Should().Be(streamedTurns[0].Blocks[0].Id);
        streamedTurns[2].Blocks[0].Id.Should().Be(streamedTurns[1].Blocks[0].Id);
        streamedTurns[2].Blocks[1]
            .Should()
            .BeOfType<TextAssistantBlock>()
            .Which.Text.Should()
            .Be("There are three.");
        streamedTurns[3].Blocks.Should().BeEquivalentTo(streamedTurns[2].Blocks);
        streamedTurns[3].StopReason.Should().Be("end_turn");
        var streamedUsage = streamedTurns[3].Usage;
        streamedUsage.Should().NotBeNull();
        streamedUsage!.InputTokens.Should().Be(15);
        streamedUsage.OutputTokens.Should().Be(9);
        streamedUsage.TotalTokens.Should().Be(24);
        streamedUsage.AdditionalCounts.Should().Contain("cacheReadInputTokens", 3);

        var assistantTurn = result.Should().BeOfType<ChatClientAssistantTurn>().Subject;
        assistantTurn.Id.Should().Be(streamedTurns[0].Id);
        assistantTurn.StopReason.Should().Be("end_turn");
        assistantTurn.Usage.Should().BeEquivalentTo(streamedTurns[3].Usage);
        assistantTurn.Blocks.Should().HaveCount(2);
        assistantTurn.Blocks[0].Id.Should().Be(streamedTurns[2].Blocks[0].Id);
        assistantTurn.Blocks[1].Id.Should().Be(streamedTurns[2].Blocks[1].Id);
    }

    [Fact]
    public async Task SendAsync_WithAssistantTurnUpdates_ShouldAccumulateToolUseSnapshots()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-6",
        };

        var messageClient = new StubAnthropicMessageClient(
            responses:
            [
                new ContentBase[] { new TextContent { Text = "Done" } },
            ],
            streamingResponses:
            [
                new MessageResponse[]
                {
                    CreateMessageStart(CreateUsage(inputTokens: 9)),
                    CreateContentBlockStart("text"),
                    CreateTextDelta("Checking"),
                    CreateContentBlockStop(),
                    CreateContentBlockStart("tool_use", "toolu_1", "search"),
                    CreateToolDelta("""{"query":"weath"""),
                    CreateToolDelta("""er"}"""),
                    CreateMessageDelta("tool_use", CreateUsage(outputTokens: 4)),
                },
            ]
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        var (streamedTurns, streamResult) = await ExecuteStreamAsync(
            client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("find weather")))
        );

        streamedTurns.Should().NotBeEmpty();
        streamedTurns.Select(turn => turn.Id).Distinct().Should().ContainSingle();

        var streamedTurnsWithTool = streamedTurns
            .Where(turn => turn.Blocks.OfType<ToolCallAssistantBlock>().Any())
            .ToList();
        streamedTurnsWithTool.Should().NotBeEmpty();
        streamedTurnsWithTool.Select(turn => turn.Blocks.OfType<ToolCallAssistantBlock>().Single().Id)
            .Distinct()
            .Should()
            .ContainSingle();

        var finalStreamedTurn = streamedTurns[^1];
        finalStreamedTurn.Blocks.Should().HaveCount(2);
        finalStreamedTurn.Blocks[0]
            .Should()
            .BeOfType<TextAssistantBlock>()
            .Which.Text.Should()
            .Be("Checking");
        finalStreamedTurn.Blocks[1]
            .Should()
            .BeOfType<ToolCallAssistantBlock>()
            .Which.Arguments["query"].Should()
            .Be("weather");
        finalStreamedTurn.StopReason.Should().Be("tool_use");
        finalStreamedTurn.Usage.Should().NotBeNull();
        finalStreamedTurn.Usage!.InputTokens.Should().Be(9);
        finalStreamedTurn.Usage.OutputTokens.Should().Be(4);
        finalStreamedTurn.Usage.TotalTokens.Should().Be(13);

        var assistantTurn = streamResult.Should().BeOfType<ChatClientAssistantTurn>().Subject;
        assistantTurn.Id.Should().Be(finalStreamedTurn.Id);
        assistantTurn.StopReason.Should().Be("tool_use");
        assistantTurn.Usage.Should().BeEquivalentTo(finalStreamedTurn.Usage);
        assistantTurn.Blocks[1].Id.Should().Be(finalStreamedTurn.Blocks[1].Id);
    }

    [Fact]
    public async Task SendAsync_WithAssistantTurnUpdates_ShouldPreserveReasoningTextAndToolOrderingInOneTurn()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "claude-sonnet-4-6",
        };

        var messageClient = new StubAnthropicMessageClient(
            responses: Array.Empty<IReadOnlyList<ContentBase>>(),
            streamingResponses:
            [
                new MessageResponse[]
                {
                    CreateMessageStart(CreateUsage(inputTokens: 12)),
                    CreateContentBlockStart("thinking"),
                    CreateThinkingDelta("Let me inspect"),
                    CreateSignatureDelta("sig_mixed"),
                    CreateContentBlockStop(),
                    CreateContentBlockStart("text"),
                    CreateTextDelta("Checking weather"),
                    CreateContentBlockStop(),
                    CreateContentBlockStart("tool_use", "toolu_mixed", "search"),
                    CreateToolDelta("""{"query":"weath"""),
                    CreateToolDelta("""er"}"""),
                    CreateMessageDelta("tool_use", CreateUsage(outputTokens: 7)),
                },
            ]
        );
        var client = new AnthropicChatClient(options, NullLogger<AnthropicChatClient>.Instance, messageClient);

        var (streamedTurns, result) = await ExecuteStreamAsync(
            client.SendAsync(CreateRequest(string.Empty, CreateUserTranscript("check weather")))
        );

        streamedTurns.Count.Should().BeGreaterThanOrEqualTo(6);
        streamedTurns.Select(turn => turn.Id).Distinct().Should().ContainSingle();

        var reasoningSnapshots = streamedTurns
            .Where(turn => turn.Blocks.OfType<ReasoningAssistantBlock>().Any())
            .ToList();
        reasoningSnapshots.Should().NotBeEmpty();
        reasoningSnapshots
            .Select(turn => turn.Blocks.OfType<ReasoningAssistantBlock>().Single().Id)
            .Distinct()
            .Should()
            .ContainSingle();

        var textSnapshots = streamedTurns
            .Where(turn => turn.Blocks.OfType<TextAssistantBlock>().Any())
            .ToList();
        textSnapshots.Should().NotBeEmpty();
        textSnapshots
            .Select(turn => turn.Blocks.OfType<TextAssistantBlock>().Single().Id)
            .Distinct()
            .Should()
            .ContainSingle();

        var toolSnapshots = streamedTurns
            .Where(turn => turn.Blocks.OfType<ToolCallAssistantBlock>().Any())
            .ToList();
        toolSnapshots.Should().NotBeEmpty();
        toolSnapshots
            .Select(turn => turn.Blocks.OfType<ToolCallAssistantBlock>().Single().Id)
            .Distinct()
            .Should()
            .ContainSingle();

        var finalStreamedTurn = streamedTurns[^1];
        finalStreamedTurn.StopReason.Should().Be("tool_use");
        finalStreamedTurn.Usage.Should().NotBeNull();
        finalStreamedTurn.Usage!.InputTokens.Should().Be(12);
        finalStreamedTurn.Usage.OutputTokens.Should().Be(7);
        finalStreamedTurn.Usage.TotalTokens.Should().Be(19);
        finalStreamedTurn.Blocks.Should().HaveCount(3);

        finalStreamedTurn.Blocks[0]
            .Should()
            .BeOfType<ReasoningAssistantBlock>()
            .Which.Text.Should()
            .Be("Let me inspect");
        finalStreamedTurn.Blocks[1]
            .Should()
            .BeOfType<TextAssistantBlock>()
            .Which.Text.Should()
            .Be("Checking weather");
        finalStreamedTurn.Blocks[2]
            .Should()
            .BeOfType<ToolCallAssistantBlock>()
            .Which.Arguments["query"].Should()
            .Be("weather");

        var finalReasoningId = ((ReasoningAssistantBlock)finalStreamedTurn.Blocks[0]).Id;
        var finalTextId = ((TextAssistantBlock)finalStreamedTurn.Blocks[1]).Id;
        var finalToolId = ((ToolCallAssistantBlock)finalStreamedTurn.Blocks[2]).Id;

        finalReasoningId.Should().Be(((ReasoningAssistantBlock)reasoningSnapshots[0].Blocks[0]).Id);
        finalTextId.Should().Be(textSnapshots[^1].Blocks.OfType<TextAssistantBlock>().Single().Id);
        finalToolId.Should().Be(toolSnapshots[^1].Blocks.OfType<ToolCallAssistantBlock>().Single().Id);

        var assistantTurn = result.Should().BeOfType<ChatClientAssistantTurn>().Subject;
        assistantTurn.Id.Should().Be(finalStreamedTurn.Id);
        assistantTurn.StopReason.Should().Be(finalStreamedTurn.StopReason);
        assistantTurn.Usage.Should().BeEquivalentTo(finalStreamedTurn.Usage);
        assistantTurn.Blocks.Should().HaveCount(3);
        assistantTurn.Blocks[0].Id.Should().Be(finalReasoningId);
        assistantTurn.Blocks[1].Id.Should().Be(finalTextId);
        assistantTurn.Blocks[2].Id.Should().Be(finalToolId);
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

    private static ChatClientRequest CreateRequest(
        string? systemPrompt = null,
        IEnumerable<ChatTranscriptEntry>? transcript = null,
        IEnumerable<ChatClientTool>? tools = null,
        ChatRequestOptions? options = null
    ) =>
        new(
            systemPrompt ?? string.Empty,
            transcript?.ToArray() ?? Array.Empty<ChatTranscriptEntry>(),
            tools?.ToArray() ?? Array.Empty<ChatClientTool>(),
            options
        );

    private static ChatTranscriptEntry[] CreateUserTranscript(string userMessage) =>
        [
            new UserChatEntry("user-1", DateTimeOffset.UtcNow, userMessage, "turn-1"),
        ];

    private static ChatClientTool CreateTool(string name, string description)
    {
        using var schemaDocument = JsonDocument.Parse("{}");
        return new ChatClientTool(name, description, schemaDocument.RootElement.Clone());
    }

    private static MessageResponse CreateContentBlockStart(
        string blockType,
        string? blockId = null,
        string? blockName = null
    ) =>
        new()
        {
            Type = "content_block_start",
            ContentBlock = new ContentBlock
            {
                Type = blockType,
                Id = blockId,
                Name = blockName,
            },
        };

    private static MessageResponse CreateContentBlockStop() => new() { Type = "content_block_stop" };

    private static MessageResponse CreateMessageStart(Usage usage) =>
        new()
        {
            Type = "message_start",
            StreamStartMessage = new StreamMessage
            {
                Usage = usage,
            },
        };

    private static MessageResponse CreateTextDelta(string text) =>
        new()
        {
            Type = "content_block_delta",
            Delta = new Delta
            {
                Text = text,
            },
        };

    private static MessageResponse CreateThinkingDelta(string thinking) =>
        new()
        {
            Type = "content_block_delta",
            Delta = new Delta
            {
                Thinking = thinking,
            },
        };

    private static MessageResponse CreateSignatureDelta(string signature) =>
        new()
        {
            Type = "content_block_delta",
            Delta = new Delta
            {
                Signature = signature,
            },
        };

    private static MessageResponse CreateToolDelta(string partialJson) =>
        new()
        {
            Type = "content_block_delta",
            Delta = new Delta
            {
                PartialJson = partialJson,
            },
        };

    private static MessageResponse CreateMessageDelta(string stopReason, Usage? usage = null) =>
        new()
        {
            Type = "message_delta",
            Delta = new Delta
            {
                StopReason = stopReason,
            },
            Usage = usage,
        };

    private static Usage CreateUsage(
        int inputTokens = 0,
        int outputTokens = 0,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0
    ) =>
        new()
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheCreationInputTokens = cacheCreationInputTokens,
            CacheReadInputTokens = cacheReadInputTokens,
        };

    private static async Task<(List<ChatClientAssistantTurn> Updates, ChatClientTurnResult Result)>
        ExecuteStreamAsync(IChatCompletionStream stream, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatClientAssistantTurn>();

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            updates.Add(update);
        }

        var result = await stream.GetResultAsync(cancellationToken);
        return (updates, result);
    }

    private sealed class StubAnthropicMessageClient : IAnthropicMessageClient
    {
        private readonly Queue<MessageResponse> _responses;
        private readonly Queue<IReadOnlyList<MessageResponse>> _streamingResponses;
        public MessageParameters? LastParameters { get; private set; }

        public StubAnthropicMessageClient(params MessageResponse[] responses)
            : this(responses, Array.Empty<IReadOnlyList<MessageResponse>>())
        {
        }

        public StubAnthropicMessageClient(params IReadOnlyList<ContentBase>[] responses)
            : this(responses, Array.Empty<IReadOnlyList<MessageResponse>>())
        {
        }

        public StubAnthropicMessageClient(
            IEnumerable<IReadOnlyList<ContentBase>> responses,
            IEnumerable<IReadOnlyList<MessageResponse>> streamingResponses
        )
            : this(
                responses.Select(content => new MessageResponse { Content = content.ToList() }),
                streamingResponses
            )
        {
        }

        public StubAnthropicMessageClient(
            IEnumerable<MessageResponse> responses,
            IEnumerable<IReadOnlyList<MessageResponse>> streamingResponses
        )
        {
            _responses = new Queue<MessageResponse>(responses);
            _streamingResponses = new Queue<IReadOnlyList<MessageResponse>>(streamingResponses);
        }

        public Task<MessageResponse> GetResponseAsync(
            MessageParameters parameters,
            CancellationToken cancellationToken = default
        )
        {
            LastParameters = parameters;

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No stubbed responses configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<MessageResponse> StreamResponseAsync(
            MessageParameters parameters,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            LastParameters = parameters;

            if (_streamingResponses.Count == 0)
            {
                throw new InvalidOperationException("No stubbed streaming responses configured.");
            }

            foreach (var response in _streamingResponses.Dequeue())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return response;
                await Task.Yield();
            }
        }
    }

    private sealed class ThrowingAnthropicMessageClient : IAnthropicMessageClient
    {
        private readonly Exception _exception;

        public ThrowingAnthropicMessageClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<MessageResponse> GetResponseAsync(
            MessageParameters parameters,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromException<MessageResponse>(_exception);
        }

        public IAsyncEnumerable<MessageResponse> StreamResponseAsync(
            MessageParameters parameters,
            CancellationToken cancellationToken = default
        ) => ThrowStreamingResponseAsync(cancellationToken);

        private async IAsyncEnumerable<MessageResponse> ThrowStreamingResponseAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken
        )
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            throw _exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

}
