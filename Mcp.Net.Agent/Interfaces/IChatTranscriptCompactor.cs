using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Interfaces;

public interface IChatTranscriptCompactor
{
    Task<IReadOnlyList<ChatTranscriptEntry>> CompactAsync(
        IReadOnlyList<ChatTranscriptEntry> transcript,
        CancellationToken cancellationToken = default
    );
}
