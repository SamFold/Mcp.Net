using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.LLM.Interfaces;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Elicitation;

/// <summary>
/// Bridges the MCP client's elicitation requests to a UI-facing prompt provider.
/// </summary>
public sealed class ElicitationCoordinator
{
    private readonly ILogger<ElicitationCoordinator>? _logger;
    private IElicitationPromptProvider? _provider;

    public ElicitationCoordinator(ILogger<ElicitationCoordinator>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers the prompt provider that should be used to satisfy elicitation requests.
    /// </summary>
    /// <param name="provider">The provider to associate with this coordinator.</param>
    public void SetProvider(IElicitationPromptProvider? provider)
    {
        Interlocked.Exchange(ref _provider, provider);
    }

    /// <summary>
    /// Handles an elicitation request originating from the server.
    /// </summary>
    public Task<ElicitationClientResponse> HandleAsync(
        ElicitationRequestContext context,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = Volatile.Read(ref _provider);
        if (provider == null)
        {
            _logger?.LogWarning(
                "Elicitation request {RequestId} received but no provider is registered; declining.",
                context.Request.Id
            );
            return Task.FromResult(ElicitationClientResponse.Decline());
        }

        _logger?.LogInformation(
            "Routing elicitation request {RequestId} to provider.",
            context.Request.Id
        );

        return provider.PromptAsync(context, cancellationToken);
    }
}
