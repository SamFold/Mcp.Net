using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;

namespace Mcp.Net.Tests.LLM.OpenAI;

public class OpenAiChatClientTests
{
    [Fact]
    public async Task SendMessageAsync_WithConfiguredSystemPrompt_ShouldIncludePromptInFirstOutboundRequest()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        // Act
        await client.SendMessageAsync("hello");

        // Assert
        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages.Should().HaveCount(2);
        ExtractSystemPrompt(completionInvoker.CapturedMessages!).Should().Be(options.SystemPrompt);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutConfiguredSystemPrompt_ShouldIncludeDefaultPromptInFirstOutboundRequest()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        // Act
        await client.SendMessageAsync("hello");

        // Assert
        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages.Should().HaveCount(2);
        ExtractSystemPrompt(completionInvoker.CapturedMessages!).Should().Be(client.GetSystemPrompt());
        client.GetSystemPrompt().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParseToolArguments_WithNestedObjectAndArray_ShouldPreserveStructuredValues()
    {
        // Arrange
        const string argumentsJson =
            """
            {
              "query": "bugs",
              "filter": {
                "status": "open",
                "labels": ["server", "urgent"]
              },
              "tags": ["triage", "backend"]
            }
            """;

        // Act
        var arguments = ParseToolArguments(argumentsJson);

        // Assert
        arguments.Should().ContainKey("query").WhoseValue.Should().Be("bugs");
        JsonSerializer.Serialize(arguments["filter"])
            .Should()
            .Be("""{"status":"open","labels":["server","urgent"]}""");
        JsonSerializer.Serialize(arguments["tags"])
            .Should()
            .Be("""["triage","backend"]""");
    }

    [Fact]
    public async Task LoadReplayTranscript_ShouldIncludePriorHistoryInFirstOutboundRequest()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        client.LoadReplayTranscript(
            new Mcp.Net.LLM.Replay.ProviderReplayTranscript(
                client.GetReplayTarget(),
                new ChatTranscriptEntry[]
                {
                    new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-2), "Earlier user", "turn-1"),
                    new AssistantChatEntry(
                        "assistant-1",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Earlier answer") },
                        "turn-1",
                        "openai",
                        "gpt-5"
                    ),
                }
            )
        );

        await client.SendMessageAsync("New question");

        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages!.Should().HaveCount(4);
        completionInvoker.CapturedMessages[1].Should().BeOfType<UserChatMessage>();
        completionInvoker.CapturedMessages[2].Should().BeOfType<AssistantChatMessage>();
        completionInvoker.CapturedMessages[3].Should().BeOfType<UserChatMessage>();
        completionInvoker.CapturedMessages[2].Content.Single().Text.Should().Be("Earlier answer");
        completionInvoker.CapturedMessages[3].Content.Single().Text.Should().Be("New question");
    }

    [Fact]
    public async Task LoadReplayTranscript_WithAssistantTextAndToolCall_ShouldKeepSingleAssistantHistoryMessage()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

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
                                "call_probe",
                                "record_probe",
                                new Dictionary<string, object?>
                                {
                                    ["status"] = "ok",
                                    ["code"] = 200d,
                                }
                            ),
                        },
                        "turn-1",
                        "openai",
                        "gpt-5"
                    ),
                    new ToolResultChatEntry(
                        "tool-result-1",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        "call_probe",
                        "record_probe",
                        new ToolInvocationResult(
                            "call_probe",
                            "record_probe",
                            false,
                            new[] { "saved" },
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

        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages!.Should().HaveCount(5);
        completionInvoker.CapturedMessages[2].Should().BeOfType<AssistantChatMessage>();
        completionInvoker.CapturedMessages[3].Should().BeOfType<ToolChatMessage>();
        completionInvoker.CapturedMessages[4].Should().BeOfType<UserChatMessage>();

        var assistantMessage = (AssistantChatMessage)completionInvoker.CapturedMessages[2];
        assistantMessage.Content.Should().ContainSingle();
        assistantMessage.Content.Single().Text.Should().Be("Checking");
        assistantMessage.ToolCalls.Should().ContainSingle();
        assistantMessage.ToolCalls.Single().Id.Should().Be("call_probe");
        assistantMessage.ToolCalls.Single().FunctionName.Should().Be("record_probe");
    }

    private static string ExtractSystemPrompt(IReadOnlyList<ChatMessage> messages)
    {
        messages[0].Should().BeOfType<SystemChatMessage>();
        return messages[0].Content.Single().Text;
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(string argumentsJson)
    {
        var parseMethod = typeof(OpenAiChatClient).GetMethod(
            "ParseToolArguments",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        parseMethod.Should().NotBeNull();

        return (IReadOnlyDictionary<string, object?>)
            parseMethod!.Invoke(null, new object[] { argumentsJson })!;
    }

    private sealed class CapturingChatCompletionInvoker : IOpenAiChatCompletionInvoker
    {
        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }

        public ChatCompletion CompleteChat(
            ChatClient client,
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options
        )
        {
            CapturedMessages = messages.ToList();
            throw new InvalidOperationException("Capture complete");
        }
    }
}
