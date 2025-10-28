using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mcp.Net.Core.Models.Tools;

namespace Mcp.Net.LLM.Tools;

/// <summary>
/// Extracts category metadata for tools from server-provided annotations and meta fields.
/// </summary>
internal static class ToolCategoryMetadataParser
{
    private static readonly string[] CategoryKeys =
    {
        "category",
        "categories",
        "mcp:category",
        "mcp:categories",
        "mcp.category",
        "mcp.categories",
    };

    public static IReadOnlyList<ToolCategoryMetadata> Parse(Tool tool)
    {
        if (tool == null)
        {
            throw new ArgumentNullException(nameof(tool));
        }

        var accumulator = new Dictionary<string, CategoryMergeState>(StringComparer.OrdinalIgnoreCase);

        if (tool.Annotations is not null)
        {
            ExtractCategoryHints(tool.Annotations, accumulator);
        }

        if (tool.Meta is not null)
        {
            ExtractCategoryHints(tool.Meta, accumulator);
        }

        if (accumulator.Count == 0)
        {
            var fallback = ToolNameClassifier.CreateFallbackCategory(tool.Name);
            accumulator[fallback.Key] = new CategoryMergeState(fallback.Key, fallback.DisplayName, fallback.Order);
        }

        return accumulator.Values
            .Select(state => state.ToMetadata())
            .ToList();
    }

    private static void ExtractCategoryHints(
        IDictionary<string, object?> source,
        Dictionary<string, CategoryMergeState> accumulator
    )
    {
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!CategoryKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            AppendCategoryValue(value, accumulator);
        }
    }

    private static void AppendCategoryValue(
        object? value,
        Dictionary<string, CategoryMergeState> accumulator
    )
    {
        switch (value)
        {
            case null:
                return;

            case string str:
                AddCategoryHint(accumulator, str, null, null);
                return;

            case JsonElement element:
                AppendJsonCategoryValue(element, accumulator);
                return;

            case IEnumerable<object?> enumerable:
                foreach (var item in enumerable)
                {
                    AppendCategoryValue(item, accumulator);
                }
                return;

            default:
                if (value is System.Collections.IEnumerable genericEnumerable)
                {
                    foreach (var item in genericEnumerable)
                    {
                        AppendCategoryValue(item, accumulator);
                    }
                    return;
                }

                AddCategoryHint(accumulator, value.ToString(), null, null);
                return;
        }
    }

    private static void AppendJsonCategoryValue(
        JsonElement element,
        Dictionary<string, CategoryMergeState> accumulator
    )
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddCategoryHint(accumulator, element.GetString(), null, null);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AppendJsonCategoryValue(item, accumulator);
                }
                break;

            case JsonValueKind.Object:
                string? name = null;
                string? displayName = null;
                double? order = null;
                string? id = null;

                if (
                    element.TryGetProperty("name", out var nameProp)
                    && nameProp.ValueKind == JsonValueKind.String
                )
                {
                    name = nameProp.GetString();
                }

                if (
                    element.TryGetProperty("displayName", out var displayProp)
                    && displayProp.ValueKind == JsonValueKind.String
                )
                {
                    displayName = displayProp.GetString();
                }

                if (
                    element.TryGetProperty("id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.String
                )
                {
                    id = idProp.GetString();
                }

                if (
                    element.TryGetProperty("order", out var orderProp)
                    && orderProp.ValueKind == JsonValueKind.Number
                    && orderProp.TryGetDouble(out var parsedOrder)
                )
                {
                    order = parsedOrder;
                }

                if (element.TryGetProperty("category", out var nestedCategory))
                {
                    AppendJsonCategoryValue(nestedCategory, accumulator);
                }

                if (element.TryGetProperty("categories", out var nestedCategories))
                {
                    AppendJsonCategoryValue(nestedCategories, accumulator);
                }

                var key = name ?? id;
                if (key is not null)
                {
                    AddCategoryHint(accumulator, key, displayName ?? name, order);
                }

                break;
        }
    }

    private static void AddCategoryHint(
        Dictionary<string, CategoryMergeState> accumulator,
        string? rawName,
        string? displayName,
        double? order
    )
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return;
        }

        var key = rawName.Trim();

        if (!accumulator.TryGetValue(key, out var state))
        {
            accumulator[key] = new CategoryMergeState(key, displayName, order);
        }
        else
        {
            state.Merge(displayName, order);
        }
    }

    private sealed class CategoryMergeState
    {
        private string? _displayName;
        private double? _order;

        public CategoryMergeState(string key, string? displayName, double? order)
        {
            Key = key;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            _order = order;
        }

        public string Key { get; }

        public void Merge(string? displayName, double? order)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                _displayName = displayName!;
            }

            if (order.HasValue && !_order.HasValue)
            {
                _order = order;
            }
        }

        public ToolCategoryMetadata ToMetadata() =>
            ToolCategoryMetadata.Create(Key, _displayName, _order);
    }
}
