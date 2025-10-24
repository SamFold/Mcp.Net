using System;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Interfaces;

namespace Mcp.Net.Client.Elicitation;

/// <summary>
/// Wraps a delegate so callers can register inline elicitation handlers without implementing <see cref="IElicitationRequestHandler"/>.
/// </summary>
internal sealed class DelegateElicitationHandler : IElicitationRequestHandler
{
    private readonly Func<ElicitationRequestContext, CancellationToken, Task<ElicitationClientResponse>> _handler;

    public DelegateElicitationHandler(
        Func<ElicitationRequestContext, CancellationToken, Task<ElicitationClientResponse>> handler
    )
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public Task<ElicitationClientResponse> HandleAsync(
        ElicitationRequestContext context,
        CancellationToken cancellationToken = default
    )
    {
        return _handler(context, cancellationToken);
    }
}
