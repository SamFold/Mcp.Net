using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatClient
{
    void RegisterTools(IEnumerable<Tool> tools);

    Task<ChatClientTurnResult> SendMessageAsync(string userMessage);

    Task<ChatClientTurnResult> SendToolResultsAsync(IEnumerable<ToolInvocationResult> toolResults);

    void ResetConversation() =>
        throw new NotImplementedException("Not supported by this client type");

    void SetSystemPrompt(string systemPrompt) =>
        throw new NotImplementedException("Not supported by this client type");

    string GetSystemPrompt();
}
