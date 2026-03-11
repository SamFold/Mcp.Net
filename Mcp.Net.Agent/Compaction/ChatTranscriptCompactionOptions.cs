namespace Mcp.Net.Agent.Compaction;

public sealed record ChatTranscriptCompactionOptions
{
    public int MaxEntryCount { get; init; } = 40;

    public int PreservedRecentEntryCount { get; init; } = 12;

    public int SummaryEntryCount { get; init; } = 8;
}
