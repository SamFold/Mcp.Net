using System;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Represents authorization server metadata relevant to token acquisition.
/// </summary>
public sealed class OAuthDiscoveryDocument
{
    public Uri TokenEndpoint { get; init; } = null!;
    public Uri? DeviceAuthorizationEndpoint { get; init; }
    public Uri? AuthorizationEndpoint { get; init; }
    public Uri? RegistrationEndpoint { get; init; }
}
