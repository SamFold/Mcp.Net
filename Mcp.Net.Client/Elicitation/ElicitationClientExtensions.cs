using System;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Interfaces;

namespace Mcp.Net.Client.Elicitation;

/// <summary>
/// Convenience helpers for registering elicitation handlers.
/// </summary>
public static class ElicitationClientExtensions
{
    /// <summary>
    /// Registers an elicitation handler using a delegate rather than an interface implementation.
    /// </summary>
    /// <param name="client">Client receiving the handler.</param>
    /// <param name="handler">
    /// Delegate that processes elicitation prompts. Pass <c>null</c> to remove the current handler.
    /// </param>
    public static void SetElicitationHandler(
        this IMcpClient client,
        Func<ElicitationRequestContext, CancellationToken, Task<ElicitationClientResponse>>? handler
    )
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (handler is null)
        {
            client.SetElicitationHandler((IElicitationRequestHandler?)null);
            return;
        }

        client.SetElicitationHandler(new DelegateElicitationHandler(handler));
    }
}
