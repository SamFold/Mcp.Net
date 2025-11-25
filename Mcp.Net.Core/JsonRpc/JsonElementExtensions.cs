using System;
using System.Text.Json;

namespace Mcp.Net.Core.JsonRpc;

/// <summary>
/// Helpers for working with <see cref="JsonElement"/> instances.
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// Attempts to retrieve a property value using a case-insensitive match.
    /// </summary>
    public static bool TryGetPropertyIgnoreCase(
        this JsonElement element,
        string propertyName,
        out JsonElement value
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out value))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
