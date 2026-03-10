using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatClient
{
    /// <summary>
    /// Replaces the model-facing tool set with the supplied collection.
    /// Repeated calls should be idempotent for the same input set.
    /// </summary>
    /// <param name="tools">The complete tool set that should remain registered.</param>
    void RegisterTools(IEnumerable<Tool> tools);

    Task<ChatClientTurnResult> SendMessageAsync(
        string userMessage,
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    );

    Task<ChatClientTurnResult> SendToolResultsAsync(
        IEnumerable<ToolInvocationResult> toolResults,
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    );

    void ResetConversation() =>
        throw new NotImplementedException("Not supported by this client type");

    void SetSystemPrompt(string systemPrompt) =>
        throw new NotImplementedException("Not supported by this client type");

    string GetSystemPrompt();

    ReplayTarget GetReplayTarget();

    void LoadReplayTranscript(ProviderReplayTranscript replayTranscript);
}
