using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatClient
{
    Task<ChatClientTurnResult> SendAsync(
        ChatClientRequest request,
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    );
}
