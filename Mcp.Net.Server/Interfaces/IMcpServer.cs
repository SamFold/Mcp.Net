using System.Text.Json;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Tools;

namespace Mcp.Net.Server.Interfaces;

/// <summary>
/// Represents the core functionality of an MCP server.
/// </summary>
public interface IMcpServer
{
    /// <summary>
    /// Connects the server to a transport.
    /// </summary>
    /// <param name="transport">The transport to connect to.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ConnectAsync(ITransport transport);

    /// <summary>
    /// Registers a tool with the server.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <param name="description">The description of the tool.</param>
    /// <param name="inputSchema">The JSON schema for the tool's input.</param>
    /// <param name="handler">The function that handles tool invocations.</param>
    void RegisterTool(
        string name,
        string? description,
        JsonElement inputSchema,
        Func<JsonElement?, Task<ToolCallResult>> handler
    );

    /// <summary>
    /// Processes a JSON-RPC request message.
    /// </summary>
    /// <param name="request">The request message to process.</param>
    /// <returns>A task that returns the response message.</returns>
    Task<JsonRpcResponseMessage> ProcessJsonRpcRequest(JsonRpcRequestMessage request);
}
