using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

internal sealed class NoOpToolExecutor : IToolExecutor
{
    public Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);

        throw new InvalidOperationException(
            $"Tool '{invocation.ToolName}' is not available for this session."
        );
    }
}
