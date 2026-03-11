using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Models;

public enum ChatTurnCompletion
{
    Completed,
    Cancelled,
}

public sealed record ChatTurnSummary(
    string TurnId,
    IReadOnlyList<ChatTranscriptEntry> AddedEntries,
    IReadOnlyList<ChatTranscriptEntry> UpdatedEntries,
    ChatTurnCompletion Completion
);
