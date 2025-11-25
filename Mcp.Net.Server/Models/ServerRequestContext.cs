using System.Collections.Generic;
using System.Threading;
using Mcp.Net.Core.JsonRpc;

namespace Mcp.Net.Server.Models;

/// <summary>
/// Carries the contextual information for a server-handled JSON-RPC request.
/// </summary>
public sealed record ServerRequestContext(
    string SessionId,
    string TransportId,
    JsonRpcRequestMessage Request,
    CancellationToken CancellationToken = default,
    IReadOnlyDictionary<string, string>? Metadata = null
);
