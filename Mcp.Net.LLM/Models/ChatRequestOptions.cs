namespace Mcp.Net.LLM.Models;

public sealed record ChatRequestOptions
{
    public float? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }
}
