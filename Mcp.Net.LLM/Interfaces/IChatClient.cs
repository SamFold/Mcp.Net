using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatClient
{
    void RegisterTools(IEnumerable<Tool> tools);

    Task<IEnumerable<LlmResponse>> SendMessageAsync(LlmMessage message);

    Task<IEnumerable<LlmResponse>> SendToolResultsAsync(IEnumerable<ToolInvocationResult> toolResults);

    // Reset the conversation history
    void ResetConversation() =>
        throw new NotImplementedException("Not supported by this client type");

    // Set or update the system prompt
    void SetSystemPrompt(string systemPrompt) =>
        throw new NotImplementedException("Not supported by this client type");

    // Get the current system prompt
    string GetSystemPrompt();

    void AddToolResultToHistory(ToolInvocationResult result) =>
        throw new NotImplementedException("Not supported by this client type");

    Task<List<LlmResponse>> GetLlmResponse() =>
        throw new NotImplementedException("Not supported by this client type");
}
