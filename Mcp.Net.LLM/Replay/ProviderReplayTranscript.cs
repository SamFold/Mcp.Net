using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Replay;

public sealed record ProviderReplayTranscript(
    ReplayTarget Target,
    IReadOnlyList<ChatTranscriptEntry> Entries,
    bool IsTruncated = false,
    string? TruncatedAfterEntryId = null
);
