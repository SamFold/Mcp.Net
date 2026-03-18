namespace Mcp.Net.LLM.Models;

public enum ChatImageOutputFormat
{
    Png,
    Jpeg,
    Webp,
}

public sealed record ChatImageGenerationOptions
{
    public string? Model { get; init; }

    public ChatImageOutputFormat OutputFormat { get; init; } = ChatImageOutputFormat.Png;
}
