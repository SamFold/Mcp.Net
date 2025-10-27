using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;

namespace Mcp.Net.LLM.Interfaces;

/// <summary>
/// Provides cached access to MCP prompts and resources, automatically refreshing when the server
/// notifies the client about catalog changes.
/// </summary>
public interface IPromptResourceCatalog : IDisposable
{
    /// <summary>
    /// Raised when the prompt inventory is refreshed.
    /// </summary>
    event EventHandler<IReadOnlyList<Prompt>>? PromptsUpdated;

    /// <summary>
    /// Raised when the resource inventory is refreshed.
    /// </summary>
    event EventHandler<IReadOnlyList<Resource>>? ResourcesUpdated;

    /// <summary>
    /// Ensures the catalog has loaded the current prompts and resources from the server.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached prompts, refreshing from the server when necessary.
    /// </summary>
    Task<IReadOnlyList<Prompt>> GetPromptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached resources, refreshing from the server when necessary.
    /// </summary>
    Task<IReadOnlyList<Resource>> GetResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the full prompt payload for the specified prompt name.
    /// </summary>
    Task<object[]> GetPromptMessagesAsync(
        string name,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the content of a resource, returning the server's payload verbatim.
    /// </summary>
    Task<ResourceContent[]> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Forces a prompt refresh.
    /// </summary>
    Task RefreshPromptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a resource refresh.
    /// </summary>
    Task RefreshResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an MCP notification to invalidate cached data when list-changed events arrive.
    /// </summary>
    /// <param name="notification">The notification raised by the MCP transport.</param>
    void HandleNotification(JsonRpcNotificationMessage notification);
}
