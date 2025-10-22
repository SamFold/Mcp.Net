using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.Core.Models.Tools;

namespace Mcp.Net.Client.Interfaces;

/// <summary>
/// Represents a client for communicating with an MCP server.
/// </summary>
public interface IMcpClient : IDisposable
{
    /// <summary>
    /// Event raised when a response is received from the server.
    /// </summary>
    event Action<JsonRpcResponseMessage>? OnResponse;

    /// <summary>
    /// Event raised when an error occurs in the client.
    /// </summary>
    event Action<Exception>? OnError;

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    event Action? OnClose;

    /// <summary>
    /// The protocol version negotiated with the server during initialization.
    /// </summary>
    string? NegotiatedProtocolVersion { get; }

    /// <summary>
    /// Instructions returned by the server during initialization.
    /// </summary>
    string? Instructions { get; }

    /// <summary>
    /// Capabilities advertised by the connected server.
    /// </summary>
    ServerCapabilities? ServerCapabilities { get; }

    /// <summary>
    /// Information about the connected server.
    /// </summary>
    ServerInfo? ServerInfo { get; }

    /// <summary>
    /// Initializes the connection to the server.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Initialize();

    /// <summary>
    /// Lists all available tools from the server.
    /// </summary>
    /// <returns>An array of available tools.</returns>
    Task<Tool[]> ListTools();

    /// <summary>
    /// Calls a tool on the server.
    /// </summary>
    /// <param name="name">The name of the tool to call.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <returns>The result of the tool call.</returns>
    Task<ToolCallResult> CallTool(string name, object? arguments = null);

    /// <summary>
    /// Lists all available resources from the server.
    /// </summary>
    /// <returns>An array of available resources.</returns>
    Task<Resource[]> ListResources();

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uri">The URI of the resource to read.</param>
    /// <returns>The content of the resource.</returns>
    Task<ResourceContent[]> ReadResource(string uri);

    /// <summary>
    /// Lists all available prompts from the server.
    /// </summary>
    /// <returns>An array of available prompts.</returns>
    Task<Prompt[]> ListPrompts();

    /// <summary>
    /// Gets a prompt from the server.
    /// </summary>
    /// <param name="name">The name of the prompt to get.</param>
    /// <returns>The prompt messages.</returns>
    Task<object[]> GetPrompt(string name);
}
