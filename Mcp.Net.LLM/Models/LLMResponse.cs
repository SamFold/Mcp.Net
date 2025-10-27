namespace Mcp.Net.LLM.Models;

public class LlmResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MessageType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<ToolInvocation> ToolCalls { get; set; } = new();
    public bool RequiresToolExecution => ToolCalls.Count > 0;
}
