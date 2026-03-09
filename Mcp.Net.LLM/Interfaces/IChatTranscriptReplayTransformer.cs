using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatTranscriptReplayTransformer
{
    ProviderReplayTranscript Transform(
        IReadOnlyList<ChatTranscriptEntry> transcript,
        ReplayTarget target
    );
}
