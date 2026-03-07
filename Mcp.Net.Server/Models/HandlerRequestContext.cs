using System.Collections.Generic;

namespace Mcp.Net.Server.Models;

/// <summary>
/// Immutable request-scoped connection metadata exposed to handler delegates.
/// </summary>
public sealed class HandlerRequestContext
{
    private static readonly IReadOnlyDictionary<string, string> s_emptyMetadata =
        new Dictionary<string, string>();

    public HandlerRequestContext(
        string sessionId,
        string? transportId = null,
        IReadOnlyDictionary<string, string>? metadata = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        SessionId = sessionId;
        TransportId = transportId;
        Metadata = metadata == null
            ? s_emptyMetadata
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
    }

    /// <summary>
    /// Logical MCP session identifier for the current request.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Transport identifier associated with the current request when available.
    /// </summary>
    public string? TransportId { get; }

    /// <summary>
    /// Request-scoped metadata captured by the transport layer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
