using System.Text.Json;

namespace Mcp.Net.LLM.Models;

public sealed class ChatClientTool
{
    public string Name { get; }

    public string Description { get; }

    public JsonElement InputSchema { get; }

    public ChatClientTool(string name, string? description, JsonElement inputSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Description = description ?? string.Empty;
        InputSchema = inputSchema.Clone();
    }
}
