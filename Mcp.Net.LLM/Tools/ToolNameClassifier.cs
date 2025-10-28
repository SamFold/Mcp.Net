using System.Globalization;

namespace Mcp.Net.LLM.Tools;

/// <summary>
/// Provides helper methods for deriving prefixes and fallback categories from tool names.
/// </summary>
internal static class ToolNameClassifier
{
    public static string GetPrefix(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var underscorePos = name.IndexOf('_');
        return underscorePos > 0 ? name.Substring(0, underscorePos + 1) : name;
    }

    public static ToolCategoryMetadata CreateFallbackCategory(string toolName)
    {
        var prefixWithDelimiter = GetPrefix(toolName);
        var slug = prefixWithDelimiter.TrimEnd('_');

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "general";
        }

        var display = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            slug.Replace('_', ' ').Replace('-', ' ')
        );

        if (string.IsNullOrWhiteSpace(display))
        {
            display = "General";
        }

        return ToolCategoryMetadata.Create(slug, display, null);
    }
}
