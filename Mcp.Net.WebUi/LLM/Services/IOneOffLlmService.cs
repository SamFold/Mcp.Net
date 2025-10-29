using Mcp.Net.LLM.Models;
using System.Threading.Tasks;

namespace Mcp.Net.WebUi.LLM.Services;

/// <summary>
/// Service for making one-off LLM requests without maintaining conversation state
/// </summary>
public interface IOneOffLlmService
{
    /// <summary>
    /// Gets a completion from an LLM without maintaining conversation state
    /// </summary>
    /// <param name="systemPrompt">System prompt to guide the model's behavior</param>
    /// <param name="userPrompt">User prompt/query to send to the model</param>
    /// <param name="model">The model to use (defaults to gpt-5 for faster, cheaper completions)</param>
    /// <param name="provider">Optional provider override (otherwise determined from model name)</param>
    /// <returns>The completion text from the LLM</returns>
    Task<string> GetCompletionAsync(string systemPrompt, string userPrompt, string model = "gpt-5", LlmProvider? provider = null);
}
