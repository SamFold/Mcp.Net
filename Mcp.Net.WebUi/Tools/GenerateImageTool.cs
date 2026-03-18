using System.Text.Json;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Images;

namespace Mcp.Net.WebUi.Tools;

public sealed class GenerateImageTool : LocalToolBase<GenerateImageTool.Arguments>
{
    private readonly IImageGenerationService _imageGenerationService;
    private readonly GeneratedImageArtifactStore _artifactStore;

    public GenerateImageTool(
        IImageGenerationService imageGenerationService,
        GeneratedImageArtifactStore artifactStore
    )
        : base(CreateDescriptor())
    {
        _imageGenerationService = imageGenerationService;
        _artifactStore = artifactStore;
    }

    protected override async Task<ToolInvocationResult> ExecuteAsync(
        Mcp.Net.Agent.Tools.ToolInvocation invocation,
        Arguments arguments,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(arguments.Prompt))
        {
            return invocation.CreateErrorResult("Image generation requires a prompt.");
        }

        try
        {
            var generatedImage = await _imageGenerationService.GenerateAsync(
                new ImageGenerationRequest(arguments.Prompt, arguments.Size),
                cancellationToken
            );
            var artifact = _artifactStore.Store(generatedImage.Data, generatedImage.MediaType);
            var uri = GeneratedImageArtifactRoutes.GetPath(artifact.Id);
            var structured = JsonSerializer.SerializeToElement(
                new
                {
                    prompt = arguments.Prompt,
                    size = generatedImage.Size,
                    model = generatedImage.Model,
                    mediaType = generatedImage.MediaType,
                    artifactId = artifact.Id,
                    uri,
                }
            );

            return invocation.CreateResult(
                text: ["Generated image ready. The image is attached as a tool resource."],
                structured: structured,
                resourceLinks:
                [
                    new ToolResultResourceLink(
                        uri,
                        "Generated image",
                        arguments.Prompt,
                        generatedImage.MediaType
                    ),
                ]
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return invocation.CreateErrorResult(ex.Message);
        }
    }

    public sealed record Arguments(string Prompt, string? Size = null);

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "generate_image",
            Title = "Generate Image",
            Description =
                "Generates an image from a prompt. Use this when the user wants a new image, illustration, icon, diagram, or photorealistic scene. Optional size accepts square, landscape, or portrait.",
            InputSchema = LocalToolSchemaGenerator.GenerateJsonSchema(typeof(Arguments)),
            Annotations = new Dictionary<string, object?> { ["category"] = "media" },
        };
}
