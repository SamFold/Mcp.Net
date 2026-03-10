using Mcp.Net.Core.Models.Tools;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Interface for registry managing tool collection and availability
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets all tools registered in the registry
    /// </summary>
    IReadOnlyList<Tool> AllTools { get; }

    /// <summary>
    /// Gets only the tools that are currently enabled
    /// </summary>
    IReadOnlyList<Tool> EnabledTools { get; }

    /// <summary>
    /// Registers a collection of tools to the registry
    /// </summary>
    /// <param name="tools">The tools to register</param>
    void RegisterTools(IEnumerable<Tool> tools);

    /// <summary>
    /// Sets which tools are enabled by name
    /// </summary>
    /// <param name="enabledToolNames">Names of tools that should be enabled</param>
    void SetEnabledTools(IEnumerable<string> enabledToolNames);

    /// <summary>
    /// Gets a tool by its name if it exists and is enabled
    /// </summary>
    /// <param name="name">Name of the tool</param>
    /// <returns>The tool or null if not found or disabled</returns>
    Tool? GetToolByName(string name);

    /// <summary>
    /// Gets all enabled tools with the specified prefix
    /// </summary>
    /// <param name="prefix">Tool name prefix</param>
    /// <returns>List of enabled tools with the prefix</returns>
    IReadOnlyList<Tool> GetToolsByPrefix(string prefix);

    /// <summary>
    /// Extracts the prefix from a tool name
    /// </summary>
    /// <param name="name">Tool name</param>
    /// <returns>Prefix of the tool</returns>
    string GetToolPrefix(string name);

    /// <summary>
    /// Checks if a tool is currently enabled
    /// </summary>
    /// <param name="name">Name of the tool</param>
    /// <returns>True if the tool is enabled</returns>
    bool IsToolEnabled(string name);

    /// <summary>
    /// Gets all available tool categories
    /// </summary>
    /// <returns>A list of tool category names</returns>
    Task<IEnumerable<string>> GetToolCategoriesAsync();

    /// <summary>
    /// Gets all tool IDs within a specific category
    /// </summary>
    /// <param name="category">The tool category to get tools from</param>
    /// <returns>A list of tool IDs in the specified category</returns>
    Task<IEnumerable<string>> GetToolsByCategoryAsync(string category);

    /// <summary>
    /// Validates that all specified tool IDs exist
    /// </summary>
    /// <param name="toolIds">The tool IDs to validate</param>
    /// <returns>A list of tool IDs that are missing (not found in the registry)</returns>
    IReadOnlyList<string> ValidateToolIds(IEnumerable<string> toolIds);
}
