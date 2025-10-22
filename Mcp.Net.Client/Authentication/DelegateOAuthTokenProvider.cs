using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// An <see cref="IOAuthTokenProvider"/> implementation that delegates token acquisition to supplied callbacks.
/// </summary>
public sealed class DelegateOAuthTokenProvider : IOAuthTokenProvider
{
    private readonly Func<
        OAuthTokenRequestContext,
        CancellationToken,
        Task<OAuthTokenResponse?>
    > _acquireAsync;
    private readonly Func<
        OAuthTokenRequestContext,
        OAuthTokenResponse,
        CancellationToken,
        Task<OAuthTokenResponse?>
    >? _refreshAsync;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateOAuthTokenProvider"/> class.
    /// </summary>
    /// <param name="acquireAsync">Delegate used to acquire new tokens.</param>
    /// <param name="refreshAsync">Optional delegate used to refresh existing tokens.</param>
    public DelegateOAuthTokenProvider(
        Func<OAuthTokenRequestContext, CancellationToken, Task<OAuthTokenResponse?>> acquireAsync,
        Func<
            OAuthTokenRequestContext,
            OAuthTokenResponse,
            CancellationToken,
            Task<OAuthTokenResponse?>
        >? refreshAsync = null
    )
    {
        _acquireAsync = acquireAsync ?? throw new ArgumentNullException(nameof(acquireAsync));
        _refreshAsync = refreshAsync;
    }

    /// <inheritdoc />
    public Task<OAuthTokenResponse?> AcquireTokenAsync(
        OAuthTokenRequestContext context,
        CancellationToken cancellationToken
    ) => _acquireAsync(context, cancellationToken);

    /// <inheritdoc />
    public Task<OAuthTokenResponse?> RefreshTokenAsync(
        OAuthTokenRequestContext context,
        OAuthTokenResponse currentToken,
        CancellationToken cancellationToken
    )
    {
        if (_refreshAsync == null)
        {
            return Task.FromResult<OAuthTokenResponse?>(null);
        }

        return _refreshAsync(context, currentToken, cancellationToken);
    }
}
