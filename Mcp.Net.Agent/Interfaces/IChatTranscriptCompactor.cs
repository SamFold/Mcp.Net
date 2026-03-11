using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Interfaces;

public interface IChatTranscriptCompactor
{
    IReadOnlyList<ChatTranscriptEntry> Compact(IReadOnlyList<ChatTranscriptEntry> transcript);
}
