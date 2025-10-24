using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Elicitation;

namespace Mcp.Net.Client.Interfaces;

/// <summary>
/// Contract for handling server-initiated elicitation requests.
/// </summary>
public interface IElicitationRequestHandler
{
    /// <summary>
    /// Handles an elicitation request initiated by the server.
    /// </summary>
    /// <param name="context">Request context describing the prompt and schema.</param>
    /// <param name="cancellationToken">Token that is cancelled if the connection closes.</param>
    /// <returns>The response to send back to the server.</returns>
    Task<ElicitationClientResponse> HandleAsync(
        ElicitationRequestContext context,
        CancellationToken cancellationToken = default
    );
}
