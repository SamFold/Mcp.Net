using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;

namespace Mcp.Net.Tests.LLM.Replay;

public class ChatTranscriptReplayTransformerTests
{
    [Fact]
    public void Transform_SameProviderAndModel_ShouldPreserveOpaqueReasoningReplayToken()
    {
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", Timestamp(0), "hello", "turn-1"),
            new AssistantChatEntry(
                "assistant-1",
                Timestamp(1),
                new AssistantContentBlock[]
                {
                    new ReasoningAssistantBlock(
                        "reasoning-1",
                        null,
                        ReasoningVisibility.Opaque,
                        "opaque-token"
                    ),
                    new TextAssistantBlock("text-1", "Hello"),
                },
                "turn-1",
                "openai",
                "gpt-5"
            ),
        };

        var replay = transformer.Transform(transcript, new ReplayTarget("openai", "gpt-5"));

        var assistant = replay.Entries.OfType<AssistantChatEntry>().Single();
        assistant.Blocks.Should().HaveCount(2);
        assistant
            .Blocks[0]
            .Should()
            .BeEquivalentTo(
                new ReasoningAssistantBlock(
                    "reasoning-1",
                    null,
                    ReasoningVisibility.Opaque,
                    "opaque-token"
                )
            );
    }

    [Fact]
    public void Transform_SameProviderDifferentModel_ShouldConvertVisibleReasoningToTextAndDropOpaqueReasoning()
    {
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new AssistantChatEntry(
                "assistant-1",
                Timestamp(0),
                new AssistantContentBlock[]
                {
                    new ReasoningAssistantBlock(
                        "reasoning-visible",
                        "Reason through the problem first.",
                        ReasoningVisibility.Visible
                    ),
                    new ReasoningAssistantBlock(
                        "reasoning-opaque",
                        null,
                        ReasoningVisibility.Opaque,
                        "opaque-token"
                    ),
                    new TextAssistantBlock("text-1", "Final answer"),
                },
                "turn-1",
                "openai",
                "gpt-5"
            ),
        };

        var replay = transformer.Transform(transcript, new ReplayTarget("openai", "gpt-5-mini"));

        var assistant = replay.Entries.OfType<AssistantChatEntry>().Single();
        assistant.Blocks.Should().HaveCount(2);
        assistant.Blocks.Should().AllSatisfy(block => block.Should().BeOfType<TextAssistantBlock>());
        assistant
            .Blocks[0]
            .As<TextAssistantBlock>()
            .Text.Should()
            .Be("Reason through the problem first.");
        assistant.Blocks[1].As<TextAssistantBlock>().Text.Should().Be("Final answer");
    }

    [Fact]
    public void Transform_CrossProvider_ShouldDropReasoningBlocksByDefault()
    {
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new AssistantChatEntry(
                "assistant-1",
                Timestamp(0),
                new AssistantContentBlock[]
                {
                    new ReasoningAssistantBlock(
                        "reasoning-visible",
                        "Visible thinking",
                        ReasoningVisibility.Visible
                    ),
                    new ReasoningAssistantBlock(
                        "reasoning-redacted",
                        null,
                        ReasoningVisibility.Redacted,
                        "redacted-token"
                    ),
                    new TextAssistantBlock("text-1", "Portable answer"),
                },
                "turn-1",
                "anthropic",
                "claude-sonnet-4-5-20250929"
            ),
        };

        var replay = transformer.Transform(transcript, new ReplayTarget("openai", "gpt-5"));

        var assistant = replay.Entries.OfType<AssistantChatEntry>().Single();
        assistant.Blocks.Should().ContainSingle();
        assistant.Blocks[0].Should().BeOfType<TextAssistantBlock>().Which.Text.Should().Be("Portable answer");
    }

    [Fact]
    public void Transform_AnthropicProbeReasoning_ToDifferentAnthropicModel_ShouldConvertThinkingToText()
    {
        var fixture = AnthropicReasoningFixture.Load();
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new AssistantChatEntry(
                "assistant-1",
                Timestamp(0),
                new AssistantContentBlock[]
                {
                    new ReasoningAssistantBlock(
                        "reasoning-1",
                        fixture.Thinking,
                        ReasoningVisibility.Visible,
                        fixture.Signature
                    ),
                    new TextAssistantBlock("text-1", fixture.Text),
                },
                "turn-1",
                "anthropic",
                "claude-sonnet-4-6"
            ),
        };

        var replay = transformer.Transform(
            transcript,
            new ReplayTarget("anthropic", "claude-sonnet-4-5-20250929")
        );

        var assistant = replay.Entries.OfType<AssistantChatEntry>().Single();
        assistant.Blocks.Should().HaveCount(2);
        assistant.Blocks.Should().AllSatisfy(block => block.Should().BeOfType<TextAssistantBlock>());
        assistant
            .Blocks.Select(block => ((TextAssistantBlock)block).Text)
            .Should()
            .ContainInOrder(fixture.Thinking, fixture.Text);
    }

    [Fact]
    public void Transform_AnthropicProbeReasoning_ToOpenAi_ShouldDropThinkingAndKeepText()
    {
        var fixture = AnthropicReasoningFixture.Load();
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new AssistantChatEntry(
                "assistant-1",
                Timestamp(0),
                new AssistantContentBlock[]
                {
                    new ReasoningAssistantBlock(
                        "reasoning-1",
                        fixture.Thinking,
                        ReasoningVisibility.Visible,
                        fixture.Signature
                    ),
                    new TextAssistantBlock("text-1", fixture.Text),
                },
                "turn-1",
                "anthropic",
                "claude-sonnet-4-6"
            ),
        };

        var replay = transformer.Transform(transcript, new ReplayTarget("openai", "gpt-5"));

        var assistant = replay.Entries.OfType<AssistantChatEntry>().Single();
        assistant.Blocks.Should().ContainSingle();
        assistant.Blocks[0].Should().BeOfType<TextAssistantBlock>().Which.Text.Should().Be(fixture.Text);
    }

    [Fact]
    public void Transform_UnmatchedAssistantToolCall_ShouldSynthesizeErrorToolResult()
    {
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", Timestamp(0), "search for weather", "turn-1"),
            new AssistantChatEntry(
                "assistant-1",
                Timestamp(1),
                new AssistantContentBlock[]
                {
                    new ToolCallAssistantBlock(
                        "tool-block-1",
                        "tool-call-1",
                        "search",
                        new Dictionary<string, object?> { ["query"] = "weather" }
                    ),
                },
                "turn-1",
                "openai",
                "gpt-5"
            ),
            new UserChatEntry("user-2", Timestamp(2), "please continue", "turn-2"),
        };

        var replay = transformer.Transform(transcript, new ReplayTarget("openai", "gpt-5"));

        replay.Entries.Should().HaveCount(4);
        replay.Entries[2].Should().BeOfType<ToolResultChatEntry>();

        var toolResult = (ToolResultChatEntry)replay.Entries[2];
        toolResult.ToolCallId.Should().Be("tool-call-1");
        toolResult.ToolName.Should().Be("search");
        toolResult.IsError.Should().BeTrue();
        toolResult.Result.IsError.Should().BeTrue();
        toolResult.Result.Text.Should().ContainSingle();
        toolResult.Result.Text[0].Should().Contain("Missing tool result");
    }

    [Fact]
    public void Transform_UserWithMultimodalContent_ShouldCloneContentParts()
    {
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry(
                "user-1",
                Timestamp(0),
                new UserContentPart[]
                {
                    new TextUserContentPart("describe"),
                    new InlineImageUserContentPart(BinaryData.FromBytes([1, 2, 3]), "image/png"),
                },
                "turn-1"
            ),
        };

        var replay = transformer.Transform(transcript, new ReplayTarget("openai", "gpt-5"));

        var user = replay.Entries.OfType<UserChatEntry>().Single();
        user.ContentParts.Should().HaveCount(2);
        user.ContentParts.Should().NotBeSameAs(((UserChatEntry)transcript[0]).ContentParts);
        user.ContentParts[0].Should().BeEquivalentTo(new TextUserContentPart("describe"));
        user.ContentParts[0].Should().NotBeSameAs(((UserChatEntry)transcript[0]).ContentParts[0]);
        var imagePart = user.ContentParts[1].Should().BeOfType<InlineImageUserContentPart>().Subject;
        imagePart.MediaType.Should().Be("image/png");
        imagePart.Data.ToArray().Should().Equal([1, 2, 3]);
        imagePart.Should().NotBeSameAs(((UserChatEntry)transcript[0]).ContentParts[1]);
    }

    [Fact]
    public void Transform_AssistantImageBlock_ShouldDropImageBlockFromReplay()
    {
        var transformer = new ChatTranscriptReplayTransformer();
        var transcript = new ChatTranscriptEntry[]
        {
            new AssistantChatEntry(
                "assistant-1",
                Timestamp(0),
                new AssistantContentBlock[]
                {
                    new TextAssistantBlock("text-1", "Here is the image."),
                    new ImageAssistantBlock(
                        "image-1",
                        BinaryData.FromBytes([7, 8, 9]),
                        "image/png"
                    ),
                },
                "turn-1",
                "openai",
                "gpt-5"
            ),
        };

        var replay = transformer.Transform(transcript, new ReplayTarget("openai", "gpt-5"));

        var assistant = replay.Entries.OfType<AssistantChatEntry>().Single();
        assistant.Blocks.Should().ContainSingle();
        assistant.Blocks[0].Should().BeOfType<TextAssistantBlock>().Which.Text.Should().Be("Here is the image.");
    }

    private static DateTimeOffset Timestamp(int minutes) =>
        new(2026, 3, 9, 10, minutes, 0, TimeSpan.Zero);

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
}
