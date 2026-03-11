using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatClientFactory
{
    IChatClient Create(LlmProvider provider, ChatClientOptions options);
}
