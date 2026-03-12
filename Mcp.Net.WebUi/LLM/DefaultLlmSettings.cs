using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.LLM;

/// <summary>
/// Application-wide default LLM settings.
/// </summary>
public class DefaultLlmSettings
{
    public LlmProvider Provider { get; set; } = LlmProvider.Anthropic;
    public string ModelName { get; set; } = "claude-sonnet-4-5-20250929";
    public string DefaultSystemPrompt { get; set; } = "You are a helpful assistant.";
}
