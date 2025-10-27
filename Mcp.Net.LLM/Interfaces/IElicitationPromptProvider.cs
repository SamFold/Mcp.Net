using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Elicitation;

namespace Mcp.Net.LLM.Interfaces;

/// <summary>
/// Contract for components that can interact with a user to fulfill MCP elicitation prompts.
/// </summary>
public interface IElicitationPromptProvider
{
    /// <summary>
    /// Presents the elicitation request to the user and returns the resulting response.
    /// </summary>
    /// <param name="context">The request context supplied by the server.</param>
    /// <param name="cancellationToken">Cancellation token signaled when the connection closes.</param>
    /// <returns>The response that should be sent back to the server.</returns>
    Task<ElicitationClientResponse> PromptAsync(
        ElicitationRequestContext context,
        CancellationToken cancellationToken
    );
}
