using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mcp.Net.Core.Models.Tools;

namespace Mcp.Net.LLM.Tools;

/// <summary>
/// Aggregates category metadata across tools and exposes lookup helpers.
/// </summary>
internal sealed class ToolCategoryCatalog
{
    private readonly ImmutableDictionary<string, CategoryEntry> _categories;
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _toolToCategories;
    private readonly ImmutableArray<ToolCategoryDescriptor> _orderedDescriptors;
    private readonly ImmutableArray<string> _orderedCategoryKeys;

    private ToolCategoryCatalog(
        ImmutableDictionary<string, CategoryEntry> categories,
        ImmutableDictionary<string, ImmutableArray<string>> toolToCategories,
        ImmutableArray<ToolCategoryDescriptor> orderedDescriptors
    )
    {
        _categories = categories;
        _toolToCategories = toolToCategories;
        _orderedDescriptors = orderedDescriptors;
        _orderedCategoryKeys = orderedDescriptors.Select(d => d.Key).ToImmutableArray();
    }

    public static ToolCategoryCatalog Build(
        IEnumerable<(Tool Tool, IReadOnlyList<ToolCategoryMetadata> Categories)> source
    )
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var categoryAccumulator = new Dictionary<string, CategoryAccumulator>(
            StringComparer.OrdinalIgnoreCase
        );
        var toolCategoryMap = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var (tool, categories) in source)
        {
            if (categories == null || categories.Count == 0)
            {
                continue;
            }

            if (!toolCategoryMap.TryGetValue(tool.Name, out var toolCategories))
            {
                toolCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                toolCategoryMap[tool.Name] = toolCategories;
            }

            foreach (var metadata in categories)
            {
                if (!categoryAccumulator.TryGetValue(metadata.Key, out var accumulator))
                {
                    accumulator = new CategoryAccumulator(metadata.Key);
                    categoryAccumulator[metadata.Key] = accumulator;
                }

                accumulator.Merge(metadata.DisplayName, metadata.Order);
            }

            foreach (var metadata in categories)
            {
                categoryAccumulator[metadata.Key].AddTool(tool.Name);
                toolCategories.Add(metadata.Key);
            }
        }

        var categoryBuilder =
            ImmutableDictionary.CreateBuilder<string, CategoryEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, accumulator) in categoryAccumulator)
        {
            categoryBuilder[key] = accumulator.ToEntry();
        }

        var descriptors = categoryAccumulator.Values
            .Select(accumulator => accumulator.ToDescriptor())
            .OrderBy(descriptor => descriptor.Order ?? double.MaxValue)
            .ThenBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var toolToCategoriesBuilder =
            ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (toolName, categories) in toolCategoryMap)
        {
            toolToCategoriesBuilder[toolName] = categories
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        return new ToolCategoryCatalog(
            categoryBuilder.ToImmutable(),
            toolToCategoriesBuilder.ToImmutable(),
            descriptors
        );
    }

    public IReadOnlyList<string> GetCategoryKeys() => _orderedCategoryKeys;

    public bool TryGetToolNames(string categoryKey, out IReadOnlyList<string> toolNames)
    {
        if (_categories.TryGetValue(categoryKey, out var entry))
        {
            toolNames = entry.ToolNames;
            return true;
        }

        toolNames = Array.Empty<string>();
        return false;
    }

    public IReadOnlyList<string> GetCategoriesForTool(string toolName)
    {
        if (_toolToCategories.TryGetValue(toolName, out var categories))
        {
            return categories;
        }

        return Array.Empty<string>();
    }

    public IReadOnlyList<ToolCategoryDescriptor> GetDescriptors() => _orderedDescriptors;

    private sealed class CategoryAccumulator
    {
        private readonly HashSet<string> _toolNames = new(StringComparer.OrdinalIgnoreCase);
        private string? _displayName;
        private double? _order;

        public CategoryAccumulator(string key)
        {
            Key = key;
            _displayName = key;
        }

        public string Key { get; }

        public void Merge(string? displayName, double? order)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                _displayName = displayName;
            }

            if (order.HasValue && !_order.HasValue)
            {
                _order = order;
            }
        }

        public void AddTool(string toolName) => _toolNames.Add(toolName);

        public CategoryEntry ToEntry() =>
            new(
                _displayName ?? Key,
                _order,
                _toolNames
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray()
            );

        public ToolCategoryDescriptor ToDescriptor() =>
            new(
                Key,
                _displayName ?? Key,
                _order,
                _toolNames
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray()
            );
    }

    private sealed record CategoryEntry(string DisplayName, double? Order, ImmutableArray<string> ToolNames);
}
