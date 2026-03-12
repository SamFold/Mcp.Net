using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.LLM.Factories;

/// <summary>
/// Creates LLM clients for a given provider and model combination.
/// </summary>
public interface ILlmClientProvider
{
    IChatClient Create(LlmProvider provider, string model);
}
