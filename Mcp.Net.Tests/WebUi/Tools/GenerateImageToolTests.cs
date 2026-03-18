using FluentAssertions;
using Mcp.Net.Agent.Tools;
using Mcp.Net.WebUi.Images;
using Mcp.Net.WebUi.Tools;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.WebUi.Tools;

public class GenerateImageToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnImageResourceLinkAndStructuredMetadata()
    {
        var service = new StubImageGenerationService(
            new GeneratedImageResult(
                BinaryData.FromBytes([9, 8, 7]),
                "image/png",
                "gpt-image-1.5"
            )
        );
        var store = new GeneratedImageArtifactStore();
        var tool = new GenerateImageTool(service, store);
        var invocation = new RuntimeToolInvocation(
            "call-1",
            "generate_image",
            new Dictionary<string, object?> { ["prompt"] = "A photorealistic orange cat" }
        );

        var result = await tool.ExecuteAsync(invocation);

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Contain("Generated image");
        result.ResourceLinks.Should().ContainSingle();
        result.ResourceLinks[0].ContentType.Should().Be("image/png");
        result.ResourceLinks[0].Uri.Should().StartWith("/api/generated-images/");
        result.Structured.Should().NotBeNull();
        result.Structured!.Value.GetProperty("prompt").GetString().Should().Be("A photorealistic orange cat");
        result.Structured!.Value.GetProperty("model").GetString().Should().Be("gpt-image-1.5");

        var artifactId = result.Structured.Value.GetProperty("artifactId").GetString();
        artifactId.Should().NotBeNullOrWhiteSpace();
        store.TryGet(artifactId!, out var storedArtifact).Should().BeTrue();
        storedArtifact!.Data.ToArray().Should().Equal([9, 8, 7]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGenerationFails_ShouldReturnErrorResult()
    {
        var tool = new GenerateImageTool(
            new FailingImageGenerationService(new InvalidOperationException("OpenAI image generation is unavailable.")),
            new GeneratedImageArtifactStore()
        );
        var invocation = new RuntimeToolInvocation(
            "call-1",
            "generate_image",
            new Dictionary<string, object?> { ["prompt"] = "A photorealistic orange cat" }
        );

        var result = await tool.ExecuteAsync(invocation);

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("unavailable");
        result.ResourceLinks.Should().BeEmpty();
    }

    private sealed class StubImageGenerationService(GeneratedImageResult result) : IImageGenerationService
    {
        public Task<GeneratedImageResult> GenerateAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(result);
    }

    private sealed class FailingImageGenerationService(Exception exception)
        : IImageGenerationService
    {
        public Task<GeneratedImageResult> GenerateAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromException<GeneratedImageResult>(exception);
    }
}
