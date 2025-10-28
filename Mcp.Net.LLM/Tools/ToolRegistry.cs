using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Tools;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Tools;

/// <summary>
/// Tracks the available MCP tools, normalises server metadata, and provides lookup helpers for callers.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private static readonly Action<ILogger<ToolRegistry>, int, int, Exception?> LogEnabledSummary =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(0, nameof(ToolRegistry)),
            "Enabled {Enabled} out of {Total} tools"
        );

    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ILogger<ToolRegistry>? _logger;

    private ToolRegistrySnapshot _snapshot = ToolRegistrySnapshot.Empty;

    /// <summary>
    /// Raised when the registry replaces its inventory (typically after a <c>tools/list_changed</c> notification).
    /// </summary>
    public event EventHandler<IReadOnlyList<Tool>>? ToolsUpdated;

    public ToolRegistry(ILogger<ToolRegistry>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Tool> AllTools => Volatile.Read(ref _snapshot).AllTools;

    /// <inheritdoc/>
    public IReadOnlyList<Tool> EnabledTools => Volatile.Read(ref _snapshot).EnabledTools;

    /// <summary>
    /// Refreshes the registry by issuing <c>tools/list</c> using the supplied MCP client.
    /// </summary>
    /// <param name="mcpClient">Client used to query available tools.</param>
    /// <param name="cancellationToken">Cancellation token for the refresh operation.</param>
    public async Task RefreshAsync(
        IMcpClient mcpClient,
        CancellationToken cancellationToken = default
    )
    {
        if (mcpClient == null)
        {
            throw new ArgumentNullException(nameof(mcpClient));
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tools = await mcpClient.ListTools().ConfigureAwait(false);
            RegisterTools(tools);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc/>
    public void RegisterTools(IEnumerable<Tool> tools)
    {
        if (tools == null)
        {
            throw new ArgumentNullException(nameof(tools));
        }

        var toolList = tools.ToList();
        ToolRegistrySnapshot updatedSnapshot;

        lock (_stateGate)
        {
            var previous = _snapshot;
            updatedSnapshot = ToolRegistrySnapshot.Create(toolList, previous.EnabledToolNames);
            _snapshot = updatedSnapshot;
        }

        LogEnabledCount(updatedSnapshot);
        ToolsUpdated?.Invoke(this, updatedSnapshot.AllTools);
    }

    /// <inheritdoc/>
    public void SetEnabledTools(IEnumerable<string> enabledToolNames)
    {
        if (enabledToolNames == null)
        {
            throw new ArgumentNullException(nameof(enabledToolNames));
        }

        lock (_stateGate)
        {
            var current = _snapshot;
            var updated = current.WithEnabledToolNames(enabledToolNames);
            _snapshot = updated;
            LogEnabledCount(updated);
        }
    }

    /// <inheritdoc/>
    public Tool? GetToolByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var snapshot = Volatile.Read(ref _snapshot);
        if (!snapshot.EnabledToolNames.Contains(name))
        {
            return null;
        }

        return snapshot.Descriptors.TryGetValue(name, out var descriptor) ? descriptor.Tool : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Tool> GetToolsByPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Array.Empty<Tool>();
        }

        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot.EnabledToolsByPrefix.TryGetValue(prefix, out var tools)
            ? tools
            : Array.Empty<Tool>();
    }

    /// <inheritdoc/>
    public string GetToolPrefix(string name) => ToolNameClassifier.GetPrefix(name);

    /// <inheritdoc/>
    public bool IsToolEnabled(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return Volatile.Read(ref _snapshot).EnabledToolNames.Contains(name);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetToolCategoriesAsync()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        var categories = snapshot.CategoryCatalog.GetCategoryKeys();
        return Task.FromResult<IEnumerable<string>>(categories);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetToolsByCategoryAsync(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        var snapshot = Volatile.Read(ref _snapshot);

        if (!snapshot.CategoryCatalog.TryGetToolNames(category, out var toolNames))
        {
            _logger?.LogWarning("Tool category {Category} not found", category);
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        var enabled = toolNames
            .Where(name => snapshot.EnabledToolNames.Contains(name))
            .ToList();

        return Task.FromResult<IEnumerable<string>>(enabled);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateToolIds(IEnumerable<string> toolIds)
    {
        if (toolIds == null)
        {
            throw new ArgumentNullException(nameof(toolIds));
        }

        var snapshot = Volatile.Read(ref _snapshot);
        var missing = toolIds.Where(id => !snapshot.Descriptors.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            _logger?.LogWarning(
                "The following tool IDs were not found: {MissingTools}",
                string.Join(", ", missing)
            );
        }

        return missing;
    }

    /// <summary>
    /// Returns the ordered categories associated with a specific tool.
    /// </summary>
    public IReadOnlyList<string> GetCategoriesForTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return Array.Empty<string>();
        }

        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot.CategoryCatalog.GetCategoriesForTool(toolName);
    }

    /// <summary>
    /// Returns ordered category descriptors including display names and membership.
    /// </summary>
    public IReadOnlyList<ToolCategoryDescriptor> GetCategoryDescriptors()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot.CategoryCatalog.GetDescriptors();
    }

    private void LogEnabledCount(ToolRegistrySnapshot snapshot)
    {
        if (_logger is { } logger)
        {
            LogEnabledSummary(
                logger,
                snapshot.EnabledToolNames.Count,
                snapshot.AllTools.Length,
                null
            );
        }
    }

    private sealed record ToolDescriptor(Tool Tool, string Prefix, ImmutableArray<string> Categories);

    private sealed record ToolRegistrySnapshot(
        ImmutableDictionary<string, ToolDescriptor> Descriptors,
        ImmutableDictionary<string, ImmutableArray<Tool>> ToolsByPrefix,
        ImmutableDictionary<string, ImmutableArray<Tool>> EnabledToolsByPrefix,
        ToolCategoryCatalog CategoryCatalog,
        ImmutableArray<Tool> AllTools,
        ImmutableHashSet<string> EnabledToolNames,
        ImmutableArray<Tool> EnabledTools
    )
    {
        public static ToolRegistrySnapshot Empty { get; } =
            new(
                ImmutableDictionary.Create<string, ToolDescriptor>(StringComparer.OrdinalIgnoreCase),
                ImmutableDictionary.Create<string, ImmutableArray<Tool>>(StringComparer.OrdinalIgnoreCase),
                ImmutableDictionary.Create<string, ImmutableArray<Tool>>(StringComparer.OrdinalIgnoreCase),
                ToolCategoryCatalog.Build(
                    Enumerable.Empty<(Tool Tool, IReadOnlyList<ToolCategoryMetadata> Categories)>()
                ),
                ImmutableArray<Tool>.Empty,
                ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
                ImmutableArray<Tool>.Empty
            );

        public static ToolRegistrySnapshot Create(
            IReadOnlyList<Tool> tools,
            ImmutableHashSet<string> previouslyEnabled
        )
        {
            var descriptors = ImmutableDictionary.CreateBuilder<string, ToolDescriptor>(
                StringComparer.OrdinalIgnoreCase
            );
            var allToolsByPrefix = new Dictionary<string, List<Tool>>(StringComparer.OrdinalIgnoreCase);
            var categorySource = new List<(Tool Tool, IReadOnlyList<ToolCategoryMetadata> Categories)>(tools.Count);

            foreach (var tool in tools)
            {
                var prefix = ToolNameClassifier.GetPrefix(tool.Name);
                var categories = ToolCategoryMetadataParser.Parse(tool);
                descriptors[tool.Name] = new ToolDescriptor(
                    tool,
                    prefix,
                    categories.Select(c => c.Key).ToImmutableArray()
                );

                if (!allToolsByPrefix.TryGetValue(prefix, out var list))
                {
                    list = new List<Tool>();
                    allToolsByPrefix[prefix] = list;
                }
                list.Add(tool);

                categorySource.Add((tool, categories));
            }

            var toolsByPrefixBuilder =
                ImmutableDictionary.CreateBuilder<string, ImmutableArray<Tool>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (prefix, list) in allToolsByPrefix)
            {
                toolsByPrefixBuilder[prefix] = list
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray();
            }

            var categoryCatalog = ToolCategoryCatalog.Build(categorySource);
            var baseSnapshot = new ToolRegistrySnapshot(
                descriptors.ToImmutable(),
                toolsByPrefixBuilder.ToImmutable(),
                ImmutableDictionary.Create<string, ImmutableArray<Tool>>(StringComparer.OrdinalIgnoreCase),
                categoryCatalog,
                tools.ToImmutableArray(),
                ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
                ImmutableArray<Tool>.Empty
            );

            var enabledNames = DetermineEnabledToolNames(baseSnapshot.Descriptors, previouslyEnabled);
            return baseSnapshot.WithEnabledToolNames(enabledNames);
        }

        public ToolRegistrySnapshot WithEnabledToolNames(IEnumerable<string> enabledToolNames)
        {
            var enabledSet = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in enabledToolNames)
            {
                if (Descriptors.ContainsKey(name))
                {
                    enabledSet.Add(name);
                }
            }

            if (enabledSet.Count == 0 && Descriptors.Count > 0)
            {
                enabledSet.UnionWith(Descriptors.Keys);
            }

            return WithEnabledToolNames(enabledSet.ToImmutable());
        }

        public ToolRegistrySnapshot WithEnabledToolNames(ImmutableHashSet<string> enabledNames)
        {
            var enabledTools = AllTools
                .Where(tool => enabledNames.Contains(tool.Name))
                .ToImmutableArray();

            var enabledByPrefixBuilder =
                ImmutableDictionary.CreateBuilder<string, ImmutableArray<Tool>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (prefix, tools) in ToolsByPrefix)
            {
                var enabledForPrefix = tools
                    .Where(tool => enabledNames.Contains(tool.Name))
                    .ToImmutableArray();

                if (enabledForPrefix.Length > 0)
                {
                    enabledByPrefixBuilder[prefix] = enabledForPrefix;
                }
            }

            return new ToolRegistrySnapshot(
                Descriptors,
                ToolsByPrefix,
                enabledByPrefixBuilder.ToImmutable(),
                CategoryCatalog,
                AllTools,
                enabledNames,
                enabledTools
            );
        }

        private static ImmutableHashSet<string> DetermineEnabledToolNames(
            ImmutableDictionary<string, ToolDescriptor> descriptors,
            ImmutableHashSet<string> previouslyEnabled
        )
        {
            if (descriptors.Count == 0)
            {
                return ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (previouslyEnabled == null || previouslyEnabled.Count == 0)
            {
                return descriptors.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in previouslyEnabled)
            {
                if (descriptors.ContainsKey(name))
                {
                    builder.Add(name);
                }
            }

            if (builder.Count == 0)
            {
                builder.UnionWith(descriptors.Keys);
            }

            return builder.ToImmutable();
        }
    }
}
