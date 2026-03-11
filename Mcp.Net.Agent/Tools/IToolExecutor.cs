using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Executes runtime tool invocations for the agent loop.
/// </summary>
public interface IToolExecutor
{
    Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default
    );
}
