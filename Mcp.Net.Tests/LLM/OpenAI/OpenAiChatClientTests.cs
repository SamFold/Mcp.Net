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
