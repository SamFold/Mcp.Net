using System;
using System.Text.Json;

namespace Mcp.Net.Client.Authentication;

internal static class OAuthTokenResponseFactory
{
    public static OAuthTokenResponse Create(JsonElement root)
    {
        var accessToken = root.GetProperty("access_token").GetString()!;
        var tokenType = root.TryGetProperty("token_type", out var tokenTypeProp)
            ? tokenTypeProp.GetString() ?? "Bearer"
            : "Bearer";

        DateTimeOffset? expires = null;
        if (root.TryGetProperty("expires_in", out var expiresProp) && expiresProp.TryGetInt32(out var expiresSeconds))
        {
            expires = DateTimeOffset.UtcNow.AddSeconds(expiresSeconds);
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenProp)
            ? refreshTokenProp.GetString()
            : null;

        return new OAuthTokenResponse(accessToken, expires, refreshToken, tokenType);
    }
}
