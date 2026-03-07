using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Server.Models;

namespace Mcp.Net.Server.Completions;

/// <summary>
/// Provides contextual information to registered completion handlers.
/// </summary>
public sealed class CompletionRequestContext
{
    public CompletionRequestContext(CompletionCompleteParams parameters)
        : this(parameters, null) { }

    public CompletionRequestContext(
        CompletionCompleteParams parameters,
        HandlerRequestContext? requestContext
    )
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        RequestContext = requestContext;
    }

    /// <summary>
    /// Strongly typed parameters supplied by the client.
    /// </summary>
    public CompletionCompleteParams Parameters { get; }

    /// <summary>
    /// Session and transport metadata captured for the current request when available.
    /// </summary>
    public HandlerRequestContext? RequestContext { get; }

    /// <summary>
    /// Returns a read-only view over argument context values.
    /// </summary>
    public IReadOnlyDictionary<string, string> ContextArguments =>
        Parameters.Context?.Arguments ?? s_emptyArguments;

    private static readonly IReadOnlyDictionary<string, string> s_emptyArguments =
        new Dictionary<string, string>();
}

/// <summary>
/// Delegate signature for completion handlers registered with <see cref="McpServer"/>.
/// </summary>
/// <param name="context">The completion context describing the request.</param>
/// <param name="cancellationToken">Cancellation token forwarded from the request pipeline.</param>
public delegate Task<CompletionValues> CompletionHandler(
    CompletionRequestContext context,
    CancellationToken cancellationToken
);
