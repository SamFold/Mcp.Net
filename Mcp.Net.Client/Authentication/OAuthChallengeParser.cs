using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Parses WWW-Authenticate headers for OAuth 2.1 Bearer challenges.
/// </summary>
public static class OAuthChallengeParser
{
    private static readonly Regex s_parameterRegex = new(
        "\\s*(?<name>[a-zA-Z0-9_\\-]+)\\s*=\\s*(\"(?<value>[^\"]*)\"|(?<value>[^,\\s]+))\\s*",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Parses a collection of WWW-Authenticate header values and returns the first Bearer challenge discovered.
    /// </summary>
    /// <param name="headerValues">Header values to parse.</param>
    /// <returns>The parsed <see cref="OAuthChallenge"/> when available; otherwise <c>null</c>.</returns>
    public static OAuthChallenge? Parse(IEnumerable<string>? headerValues)
    {
        if (headerValues == null)
        {
            return null;
        }

        foreach (var value in headerValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (AuthenticationHeaderValue.TryParse(value, out var headerValue))
            {
                if (!"Bearer".Equals(headerValue.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parameters = ParseParameters(headerValue.Parameter ?? string.Empty);
                return new OAuthChallenge(headerValue.Scheme, parameters, value);
            }

            // Fallback for non-standard formatting
            var trimmed = value.Trim();
            if (!trimmed.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameterText = trimmed.Substring("Bearer".Length).Trim();
            var parsedParameters = ParseParameters(parameterText);
            return new OAuthChallenge("Bearer", parsedParameters, value);
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ParseParameters(string input)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input))
        {
            return parameters;
        }

        foreach (Match match in s_parameterRegex.Matches(input))
        {
            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrEmpty(name))
            {
                parameters[name] = value;
            }
        }

        return parameters;
    }
}
