using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Interfaces;

namespace Mcp.Net.Agent.Interfaces;

public interface IChatSessionFactory
{
    ChatSession Create(
        IChatClient chatClient,
        IToolExecutor toolExecutor,
        ChatSessionConfiguration configuration,
        IChatTranscriptCompactor? transcriptCompactor = null
    );

    Task<ChatSession> CreateAsync(
        IChatClient chatClient,
        ChatSessionFactoryOptions options,
        CancellationToken cancellationToken = default
    );
}
