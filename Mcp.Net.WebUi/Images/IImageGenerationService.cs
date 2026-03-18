namespace Mcp.Net.WebUi.Images;

public interface IImageGenerationService
{
    Task<GeneratedImageResult> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed record ImageGenerationRequest(string Prompt, string? Size = null);

public sealed record GeneratedImageResult(
    BinaryData Data,
    string MediaType,
    string Model,
    string? Size = null
);
