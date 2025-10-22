using System.Collections.Generic;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Represents a parsed OAuth 2.1 WWW-Authenticate challenge.
/// </summary>
public sealed class OAuthChallenge
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthChallenge"/> class.
    /// </summary>
    public OAuthChallenge(
        string scheme,
        IReadOnlyDictionary<string, string> parameters,
        string rawHeaderValue
    )
    {
        Scheme = scheme;
        Parameters = parameters;
        RawHeaderValue = rawHeaderValue;
    }

    /// <summary>
    /// Gets the authentication scheme (typically "Bearer").
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// Gets the dictionary of parameters supplied with the challenge.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>
    /// Gets the resource metadata URL when provided by the challenge.
    /// </summary>
    public string? ResourceMetadata =>
        Parameters.TryGetValue("resource_metadata", out var value) ? value : null;

    /// <summary>
    /// Gets the realm supplied in the challenge, when available.
    /// </summary>
    public string? Realm => Parameters.TryGetValue("realm", out var value) ? value : null;

    /// <summary>
    /// Gets the scopes supplied in the challenge, when available.
    /// </summary>
    public string? Scope => Parameters.TryGetValue("scope", out var value) ? value : null;

    /// <summary>
    /// Gets the raw header value that produced this challenge.
    /// </summary>
    public string RawHeaderValue { get; }
}
