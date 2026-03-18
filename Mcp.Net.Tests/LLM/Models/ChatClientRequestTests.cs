using FluentAssertions;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Tests.LLM.Models;

public class ChatClientRequestTests
{
    [Fact]
    public void Constructor_WithoutOptions_ShouldLeaveOptionsNull()
    {
        var request = new ChatClientRequest("Be concise.", Array.Empty<ChatTranscriptEntry>());

        request.Options.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithOptions_ShouldCaptureOptionsAsSnapshot()
    {
        var options = new ChatRequestOptions
        {
            Temperature = 0.3f,
            MaxOutputTokens = 512,
            ToolChoice = ChatToolChoice.ForTool("search"),
            ImageGeneration = new ChatImageGenerationOptions
            {
                Model = "gpt-image-1.5",
                OutputFormat = ChatImageOutputFormat.Webp,
            },
        };

        var request = new ChatClientRequest(
            "Be concise.",
            Array.Empty<ChatTranscriptEntry>(),
            options: options
        );

        request.Options.Should().NotBeNull();
        request.Options.Should().BeEquivalentTo(options);
        request.Options.Should().NotBeSameAs(options);
        request.Options!.ToolChoice.Should().Be(options.ToolChoice);
        request.Options.ImageGeneration.Should().NotBeNull();
        request.Options.ImageGeneration.Should().NotBeSameAs(options.ImageGeneration);
        request.Options.ImageGeneration.Should().BeEquivalentTo(options.ImageGeneration);
    }
}
