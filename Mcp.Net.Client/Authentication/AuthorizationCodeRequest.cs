using System;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Describes an authorization-code request that the host application must complete.
/// </summary>
public sealed record AuthorizationCodeRequest(
    Uri AuthorizationUri,
    string State,
    Uri RedirectUri
);
