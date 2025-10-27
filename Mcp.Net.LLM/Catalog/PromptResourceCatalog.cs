using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.LLM.Interfaces;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Catalog;

/// <summary>
/// Tracks prompts and resources exposed by the connected MCP server, keeping the cached copies in
/// sync with <c>list_changed</c> notifications so UI layers always have fresh metadata.
/// </summary>
public sealed class PromptResourceCatalog : IPromptResourceCatalog
{
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<PromptResourceCatalog> _logger;
    private readonly SemaphoreSlim _promptLock = new(1, 1);
    private readonly SemaphoreSlim _resourceLock = new(1, 1);
    private readonly Action<JsonRpcNotificationMessage> _notificationHandler;
    private volatile Prompt[] _prompts = Array.Empty<Prompt>();
    private volatile Resource[] _resources = Array.Empty<Resource>();
    private bool _promptsLoaded;
    private bool _resourcesLoaded;
    private bool _initialized;
    private bool _disposed;

    public event EventHandler<IReadOnlyList<Prompt>>? PromptsUpdated;
    public event EventHandler<IReadOnlyList<Resource>>? ResourcesUpdated;

    public PromptResourceCatalog(
        IMcpClient mcpClient,
        ILogger<PromptResourceCatalog> logger
    )
    {
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _notificationHandler = HandleNotification;
        _mcpClient.OnNotification += _notificationHandler;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await Task.WhenAll(
            RefreshPromptsAsync(cancellationToken),
            RefreshResourcesAsync(cancellationToken)
        ).ConfigureAwait(false);

        _initialized = true;
    }

    public async Task<IReadOnlyList<Prompt>> GetPromptsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_promptsLoaded)
        {
            await RefreshPromptsAsync(cancellationToken).ConfigureAwait(false);
        }

        return Array.AsReadOnly(_prompts);
    }

    public async Task<IReadOnlyList<Resource>> GetResourcesAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_resourcesLoaded)
        {
            await RefreshResourcesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Array.AsReadOnly(_resources);
    }

    public Task<object[]> GetPromptMessagesAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        return _mcpClient.GetPrompt(name);
    }

    public Task<ResourceContent[]> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        cancellationToken.ThrowIfCancellationRequested();
        return _mcpClient.ReadResource(uri);
    }

    public async Task RefreshPromptsAsync(CancellationToken cancellationToken = default)
    {
        await _promptLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Refreshing MCP prompt catalog");
            var prompts = await _mcpClient.ListPrompts().ConfigureAwait(false);
            _prompts = prompts;
            _promptsLoaded = true;

            PromptsUpdated?.Invoke(this, Array.AsReadOnly(_prompts));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh prompts from MCP server");
            throw;
        }
        finally
        {
            _promptLock.Release();
        }
    }

    public async Task RefreshResourcesAsync(CancellationToken cancellationToken = default)
    {
        await _resourceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Refreshing MCP resource catalog");
            var resources = await _mcpClient.ListResources().ConfigureAwait(false);
            _resources = resources;
            _resourcesLoaded = true;

            ResourcesUpdated?.Invoke(this, Array.AsReadOnly(_resources));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh resources from MCP server");
            throw;
        }
        finally
        {
            _resourceLock.Release();
        }
    }

    public void HandleNotification(JsonRpcNotificationMessage notification)
    {
        if (notification == null)
        {
            return;
        }

        switch (notification.Method)
        {
            case "prompts/list_changed":
                FireAndForgetRefresh(RefreshPromptsAsync, "prompt list changed");
                break;
            case "resources/list_changed":
                FireAndForgetRefresh(RefreshResourcesAsync, "resource list changed");
                break;
        }
    }

    private void FireAndForgetRefresh(
        Func<CancellationToken, Task> refresh,
        string reason
    )
    {
        _ = Task.Run(
            async () =>
            {
                try
                {
                    _logger.LogDebug("Refreshing catalog because {Reason}", reason);
                    await refresh(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh catalog after {Reason}", reason);
                }
            }
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mcpClient.OnNotification -= _notificationHandler;
        _promptLock.Dispose();
        _resourceLock.Dispose();
    }
}
