using System;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Information returned by an authorization server's device code endpoint.
/// </summary>
public sealed record DeviceCodeInfo
{
    public string DeviceCode { get; init; } = string.Empty;
    public string UserCode { get; init; } = string.Empty;
    public Uri VerificationUri { get; init; } = null!;
    public Uri? VerificationUriComplete { get; init; }
    public int ExpiresInSeconds { get; init; }
    public int IntervalSeconds { get; init; } = 5;
}
