using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Mcp.Net.Client;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Examples.Shared;
using Mcp.Net.Examples.Shared.Authorization;
using Microsoft.AspNetCore.WebUtilities;

namespace Mcp.Net.WebUi.Authentication;

/// <summary>
/// Configures <see cref="McpClientBuilder"/> instances to authenticate against the demo OAuth server.
/// </summary>
public sealed class McpAuthenticationService : IMcpClientBuilderConfigurator, IDisposable
{
    private enum McpAuthMode
    {
        None,
        ClientCredentials,
        AuthorizationCodePkce,
    }

    private readonly IConfiguration _configuration;
    private readonly ILogger<McpAuthenticationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string[] _args;

    private readonly SemaphoreSlim _pkceInitializationLock = new(1, 1);
    private PkceAuthContext? _pkceContext;

    public McpAuthenticationService(
        IConfiguration configuration,
        ILogger<McpAuthenticationService> logger,
        IHttpClientFactory httpClientFactory,
        IEnumerable<string> args
    )
    {
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _args = args.ToArray();
    }

    public async Task ConfigureAsync(
        McpClientBuilder builder,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (IsAuthDisabled())
        {
            _logger.LogDebug("MCP authentication disabled via configuration or command-line flag.");
            return;
        }

        var mode = ResolveAuthMode();
        if (mode == McpAuthMode.None)
        {
            _logger.LogDebug("MCP authentication mode set to 'None'; proceeding without tokens.");
            return;
        }

        var resourceUri = GetServerUri();
        var baseUri = NormalizeBaseUri(resourceUri);

        switch (mode)
        {
            case McpAuthMode.ClientCredentials:
                ConfigureClientCredentials(builder, baseUri);
                break;

            case McpAuthMode.AuthorizationCodePkce:
                await ConfigureAuthorizationCodePkceAsync(builder, baseUri, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private void ConfigureClientCredentials(McpClientBuilder builder, Uri baseUri)
    {
        var options = DemoOAuthDefaults.CreateClientOptions(baseUri);
        options.AuthorizationServerMetadataAddress = DemoOAuthDefaults.BuildMetadataUri(baseUri);
        options.ResourceMetadataAddress = DemoOAuthDefaults.BuildResourceMetadataUri(baseUri);

        options.ClientId =
            _configuration["McpServer:ClientCredentials:ClientId"]
            ?? DemoOAuthDefaults.ClientId;
        options.ClientSecret =
            _configuration["McpServer:ClientCredentials:ClientSecret"]
            ?? DemoOAuthDefaults.ClientSecret;

        var httpClient = _httpClientFactory.CreateClient("McpOAuthTokens");
        _logger.LogInformation(
            "Configured OAuth client credentials authentication for MCP server at {BaseUri}.",
            baseUri
        );
        builder.WithClientCredentialsAuth(options, httpClient);
    }

    private async Task ConfigureAuthorizationCodePkceAsync(
        McpClientBuilder builder,
        Uri baseUri,
        CancellationToken cancellationToken
    )
    {
        var context = await EnsurePkceContextAsync(baseUri, cancellationToken).ConfigureAwait(false);

        builder.WithAuthorizationCodeAuth(
            context.Options,
            context.InteractionHandler,
            context.TokenHttpClient
        );

        _logger.LogInformation(
            "Configured OAuth authorization-code (PKCE) authentication for MCP server at {BaseUri}.",
            baseUri
        );
    }

    private async Task<PkceAuthContext> EnsurePkceContextAsync(
        Uri baseUri,
        CancellationToken cancellationToken
    )
    {
        if (_pkceContext != null)
        {
            return _pkceContext;
        }

        await _pkceInitializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pkceContext != null)
            {
                return _pkceContext;
            }

        var options = DemoOAuthDefaults.CreateClientOptions(baseUri);
        options.AuthorizationServerMetadataAddress = DemoOAuthDefaults.BuildMetadataUri(baseUri);
        options.ResourceMetadataAddress = DemoOAuthDefaults.BuildResourceMetadataUri(baseUri);

        var redirectOverride = _configuration["McpServer:Pkce:RedirectUri"];
        if (
            !string.IsNullOrWhiteSpace(redirectOverride)
            && Uri.TryCreate(redirectOverride, UriKind.Absolute, out var configuredRedirect)
        )
        {
            options.RedirectUri = configuredRedirect;
        }
        else
        {
            options.RedirectUri = new Uri(baseUri, "/oauth/callback");
        }

            var useDynamicRegistration = _configuration.GetValue<bool?>(
                "McpServer:Pkce:UseDynamicRegistration"
            ) ?? true;

            var registrarClient = _httpClientFactory.CreateClient("McpOAuthRegistration");
            var tokenClient = _httpClientFactory.CreateClient("McpOAuthTokens");
            var interactionClient = _httpClientFactory.CreateClient("McpOAuthInteraction");

            if (useDynamicRegistration)
            {
                var clientName =
                    _configuration["McpServer:Pkce:ClientName"] ?? "WebUI PKCE Client";

            var registration = await DemoDynamicClientRegistrar.RegisterAsync(
                baseUri,
                clientName,
                options.RedirectUri!,
                DemoOAuthDefaults.Scopes,
                registrarClient,
                cancellationToken
            ).ConfigureAwait(false);

                options.ClientId = registration.ClientId;
                options.ClientSecret = registration.ClientSecret;
                options.RedirectUri =
                    registration.RedirectUris.FirstOrDefault()
                    ?? DemoOAuthDefaults.DefaultRedirectUri;

                _logger.LogInformation(
                    "Dynamic client registration succeeded for '{ClientName}' (client_id: {ClientId}).",
                    clientName,
                    options.ClientId
                );
            }
            else
            {
                options.ClientId =
                    _configuration["McpServer:Pkce:ClientId"] ?? DemoOAuthDefaults.ClientId;
                options.ClientSecret = _configuration["McpServer:Pkce:ClientSecret"];

                var redirectConfig = _configuration["McpServer:Pkce:RedirectUri"];
                if (
                    !string.IsNullOrWhiteSpace(redirectConfig)
                    && Uri.TryCreate(redirectConfig, UriKind.Absolute, out var redirectUri)
                )
                {
                    options.RedirectUri = redirectUri;
                }

                _logger.LogInformation(
                    "Using configured PKCE client_id '{ClientId}' for MCP authentication.",
                    options.ClientId
                );
            }

            var handler = CreatePkceInteractionHandler(
                interactionClient,
                options.RedirectUri ?? DemoOAuthDefaults.DefaultRedirectUri
            );

            _pkceContext = new PkceAuthContext(options, tokenClient, handler);
            return _pkceContext;
        }
        finally
        {
            _pkceInitializationLock.Release();
        }
    }

    private McpAuthMode ResolveAuthMode()
    {
        var configured = _configuration["McpServer:AuthMode"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return McpAuthMode.AuthorizationCodePkce;
        }

        return configured.Trim().ToLowerInvariant() switch
        {
            "none" => McpAuthMode.None,
            "clientcredentials" => McpAuthMode.ClientCredentials,
            "client_credentials" => McpAuthMode.ClientCredentials,
            "client-credentials" => McpAuthMode.ClientCredentials,
            "pkce" => McpAuthMode.AuthorizationCodePkce,
            "authorizationcode" => McpAuthMode.AuthorizationCodePkce,
            "authorization_code" => McpAuthMode.AuthorizationCodePkce,
            _ => McpAuthMode.AuthorizationCodePkce,
        };
    }

    private bool IsAuthDisabled()
    {
        if (_configuration.GetValue<bool>("McpServer:NoAuth"))
        {
            return true;
        }

        return _args.Any(arg => string.Equals(arg, "--no-auth", StringComparison.OrdinalIgnoreCase));
    }

    private Uri GetServerUri()
    {
        var url = _configuration["McpServer:Url"] ?? "http://localhost:5000/";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"The configured MCP server URL '{url}' is not a valid absolute URI."
            );
        }

        return uri;
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri;
    }

    private static Func<
        AuthorizationCodeRequest,
        CancellationToken,
        Task<AuthorizationCodeResult>
    > CreatePkceInteractionHandler(HttpClient httpClient, Uri fallbackRedirectUri)
    {
        return async (request, cancellationToken) =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, request.AuthorizationUri);
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            ).ConfigureAwait(false);

            if (!IsRedirectStatus(response.StatusCode))
            {
                throw new InvalidOperationException(
                    $"Authorization endpoint responded with status {response.StatusCode}."
                );
            }

            var location = response.Headers.Location
                ?? throw new InvalidOperationException(
                    "Authorization server did not provide a redirect location."
                );

            if (!location.IsAbsoluteUri)
            {
                var redirectBase = request.RedirectUri ?? fallbackRedirectUri;
                location = new Uri(redirectBase, location);
            }

            var query = QueryHelpers.ParseQuery(location.Query);
            if (!query.TryGetValue("code", out var codeValues))
            {
                throw new InvalidOperationException(
                    "Authorization server did not return an authorization code."
                );
            }

            var code = codeValues.ToString();
            var returnedState = query.TryGetValue("state", out var stateValues)
                ? stateValues.ToString()
                : string.Empty;

            return new AuthorizationCodeResult(code, returnedState);
        };
    }

    private static bool IsRedirectStatus(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or (HttpStatusCode)308;
    }

    public void Dispose()
    {
        _pkceInitializationLock.Dispose();
    }

    private sealed record PkceAuthContext(
        OAuthClientOptions Options,
        HttpClient TokenHttpClient,
        Func<
            AuthorizationCodeRequest,
            CancellationToken,
            Task<AuthorizationCodeResult>
        > InteractionHandler
    );
}
