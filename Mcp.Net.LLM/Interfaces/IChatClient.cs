using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Interfaces;

public interface IChatClient
{
    IChatCompletionStream SendAsync(
        ChatClientRequest request,
        CancellationToken cancellationToken = default
    );
}
