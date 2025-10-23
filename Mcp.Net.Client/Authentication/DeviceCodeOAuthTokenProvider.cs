using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Implements the OAuth 2.1 device authorization grant (RFC 8628).
/// </summary>
public sealed class DeviceCodeOAuthTokenProvider : IOAuthTokenProvider
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OAuthClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly OAuthDiscoveryService _discoveryService;
    private readonly Func<DeviceCodeInfo, CancellationToken, Task>? _onUserInteraction;

    public DeviceCodeOAuthTokenProvider(
        OAuthClientOptions options,
        HttpClient httpClient,
        Func<DeviceCodeInfo, CancellationToken, Task>? onUserInteraction = null
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _discoveryService = new OAuthDiscoveryService(_httpClient);
        _onUserInteraction = onUserInteraction;
    }

    public async Task<OAuthTokenResponse?> AcquireTokenAsync(
        OAuthTokenRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var metadata = await _discoveryService.GetMetadataAsync(_options, cancellationToken);
        if (metadata.DeviceAuthorizationEndpoint == null)
        {
            throw new InvalidOperationException("Authorization server did not advertise a device authorization endpoint.");
        }

        var deviceRequest = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["scope"] = string.Join(" ", _options.Scopes ?? Array.Empty<string>()),
            ["resource"] = context.Resource.ToString(),
        };

        using var deviceAuthResponse = await _httpClient.PostAsync(
            metadata.DeviceAuthorizationEndpoint,
            new FormUrlEncodedContent(deviceRequest),
            cancellationToken
        );
        deviceAuthResponse.EnsureSuccessStatusCode();
        var devicePayload = await ReadJsonAsync(deviceAuthResponse, cancellationToken);

        var info = new DeviceCodeInfo
        {
            DeviceCode = devicePayload.GetProperty("device_code").GetString()!,
            UserCode = devicePayload.GetProperty("user_code").GetString()!,
            VerificationUri = new Uri(devicePayload.GetProperty("verification_uri").GetString()!, UriKind.Absolute),
            VerificationUriComplete = devicePayload.TryGetProperty("verification_uri_complete", out var verificationCompleteProp)
                ? new Uri(verificationCompleteProp.GetString()!, UriKind.Absolute)
                : null,
            ExpiresInSeconds = devicePayload.GetProperty("expires_in").GetInt32(),
            IntervalSeconds = devicePayload.TryGetProperty("interval", out var intervalProp)
                ? Math.Max(0, intervalProp.GetInt32())
                : 5,
        };

        if (_onUserInteraction != null)
        {
            await _onUserInteraction(info, cancellationToken);
        }

        var expiry = DateTimeOffset.UtcNow.AddSeconds(info.ExpiresInSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(info.IntervalSeconds), cancellationToken);

            var tokenRequest = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = info.DeviceCode,
                ["client_id"] = _options.ClientId,
            };

            if (!string.IsNullOrEmpty(_options.ClientSecret))
            {
                tokenRequest["client_secret"] = _options.ClientSecret;
            }

            using var response = await _httpClient.PostAsync(
                metadata.TokenEndpoint,
                new FormUrlEncodedContent(tokenRequest),
                cancellationToken
            );

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(content);
                return OAuthTokenResponseFactory.Create(json.RootElement);
            }

            using var errorJson = JsonDocument.Parse(content);
            if (!errorJson.RootElement.TryGetProperty("error", out var errorCodeProp))
            {
                throw new InvalidOperationException($"Device code token request failed: {content}");
            }

            var errorCode = errorCodeProp.GetString();
            switch (errorCode)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    info = info with { IntervalSeconds = info.IntervalSeconds + 5 };
                    continue;
                case "expired_token":
                    throw new InvalidOperationException("Device code expired before authorization was granted.");
                default:
                    throw new InvalidOperationException($"Device code flow failed: {errorCode}");
            }
        }

        throw new OperationCanceledException("Device code acquisition cancelled.");
    }

    public Task<OAuthTokenResponse?> RefreshTokenAsync(
        OAuthTokenRequestContext context,
        OAuthTokenResponse currentToken,
        CancellationToken cancellationToken
    ) => AcquireTokenAsync(context, cancellationToken);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

}
