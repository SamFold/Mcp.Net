using Mcp.Net.Client.Interfaces;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// MCP-backed tool executor for runtime tool invocations.
/// </summary>
public sealed class McpToolExecutor : IToolExecutor
{
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<McpToolExecutor> _logger;

    public McpToolExecutor(
        IMcpClient mcpClient,
        ILogger<McpToolExecutor> logger
    )
    {
        _mcpClient = mcpClient;
        _logger = logger;
    }

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var mcpResult = await _mcpClient.CallTool(invocation.ToolName, invocation.Arguments);
            return ToolResultConverter.FromMcpResult(
                invocation.ToolCallId,
                invocation.ToolName,
                mcpResult
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing tool {ToolName}: {ErrorMessage}",
                invocation.ToolName,
                ex.Message
            );

            return ToolInvocationResultFactory.CreateError(
                invocation.ToolCallId,
                invocation.ToolName,
                ex.Message
            );
        }
    }
}
