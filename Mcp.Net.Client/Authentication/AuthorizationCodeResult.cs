using System;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Represents the outcome of user interaction for an authorization-code flow.
/// </summary>
public sealed record AuthorizationCodeResult(
    string Code,
    string State,
    Uri? CallbackUri = null
);
