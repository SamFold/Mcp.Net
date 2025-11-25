using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Core.Models.Content;

namespace Mcp.Net.Server.Services;

/// <summary>
/// Provides operations for registering and invoking server-hosted tools.
/// </summary>
public interface IToolService
{
    /// <summary>
    /// Registers a tool using the supplied metadata and handler.
    /// </summary>
    void RegisterTool(
        string name,
        string? description,
        JsonElement inputSchema,
        Func<JsonElement?, Task<ToolCallResult>> handler,
        IDictionary<string, object?>? annotations = null
    );

    /// <summary>
    /// Returns the tools exposed by the server.
    /// </summary>
    IReadOnlyCollection<Tool> GetTools();

    /// <summary>
    /// Executes a tool call request and returns the result.
    /// </summary>
    Task<ToolCallResult> ExecuteAsync(string toolName, JsonElement? arguments, string sessionId);
}
