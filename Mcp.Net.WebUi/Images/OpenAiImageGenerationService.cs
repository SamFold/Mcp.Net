#pragma warning disable OPENAI001
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using OpenAI.Images;

namespace Mcp.Net.WebUi.Images;

public sealed class OpenAiImageGenerationService : IImageGenerationService
{
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly ILogger<OpenAiImageGenerationService> _logger;

    public OpenAiImageGenerationService(
        IApiKeyProvider apiKeyProvider,
        ILogger<OpenAiImageGenerationService> logger
    )
    {
        _apiKeyProvider = apiKeyProvider;
        _logger = logger;
    }

    public async Task<GeneratedImageResult> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Image generation requires a prompt.", nameof(request));
        }

        var apiKey = await _apiKeyProvider.GetApiKeyAsync(LlmProvider.OpenAI);
        var normalizedSize = NormalizeSize(request.Size);
        var client = new ImageClient(ProviderModelDefaults.OpenAiImageGeneration, apiKey);
        var options = CreateOptions(normalizedSize);

        _logger.LogInformation(
            "Generating image with OpenAI model {Model} and size {Size}",
            ProviderModelDefaults.OpenAiImageGeneration,
            normalizedSize
        );

        var generatedImage = (await client.GenerateImageAsync(
            request.Prompt,
            options,
            cancellationToken
        )).Value;

        if (generatedImage?.ImageBytes == null)
        {
            throw new InvalidOperationException(
                "OpenAI image generation returned no inline image bytes."
            );
        }

        return new GeneratedImageResult(
            generatedImage.ImageBytes,
            "image/png",
            ProviderModelDefaults.OpenAiImageGeneration,
            normalizedSize
        );
    }

    internal static ImageGenerationOptions CreateOptions(string normalizedSize) =>
        new()
        {
            // GPT image models return base64 image data by default; response_format is only for DALL-E.
            OutputFileFormat = GeneratedImageFileFormat.Png,
            Size = ToGeneratedImageSize(normalizedSize),
        };

    internal static string NormalizeSize(string? size) =>
        string.IsNullOrWhiteSpace(size)
            ? "square"
            : size.Trim().ToLowerInvariant();

    internal static GeneratedImageSize ToGeneratedImageSize(string size) =>
        size switch
        {
            "square" => GeneratedImageSize.W1024xH1024,
            "landscape" => GeneratedImageSize.W1536xH1024,
            "portrait" => GeneratedImageSize.W1024xH1536,
            _ => throw new ArgumentException(
                "Image size must be one of: square, landscape, portrait.",
                nameof(size)
            ),
        };
}
#pragma warning restore OPENAI001
