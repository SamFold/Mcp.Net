namespace Mcp.Net.LLM.Models;

public class ChatClientOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public float Temperature { get; set; } = 0.7f;
    public int? MaxOutputTokens { get; set; }
}
