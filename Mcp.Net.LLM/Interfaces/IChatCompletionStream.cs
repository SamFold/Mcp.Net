using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatCompletionStream : IAsyncEnumerable<ChatClientAssistantTurn>
{
    ValueTask<ChatClientTurnResult> GetResultAsync(CancellationToken cancellationToken = default);
}
