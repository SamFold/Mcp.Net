using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Examples.LLM.Models;

namespace Mcp.Net.Examples.LLM.Interfaces;

public interface IChatClient
{
    void RegisterTools(IEnumerable<Tool> tools);

    Task<IEnumerable<LlmResponse>> SendMessageAsync(LlmMessage message);

    Task<IEnumerable<LlmResponse>> SendToolResultsAsync(IEnumerable<Models.ToolCall> toolResults);

    void AddToolResultToHistory(
        string toolCallId,
        string toolName,
        Dictionary<string, object> results
    ) => throw new NotImplementedException("Not supported by this client type");
    Task<List<LlmResponse>> GetLlmResponse() =>
        throw new NotImplementedException("Not supported by this client type");
}
