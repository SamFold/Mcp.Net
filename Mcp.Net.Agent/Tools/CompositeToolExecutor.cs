using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Routes registered local tools to the local executor and delegates all remaining tool calls
/// to a fallback executor.
/// </summary>
public sealed class CompositeToolExecutor : IToolExecutor
{
    private readonly LocalToolExecutor _localExecutor;
    private readonly IToolExecutor _fallbackExecutor;

    public CompositeToolExecutor(LocalToolExecutor localExecutor, IToolExecutor fallbackExecutor)
    {
        _localExecutor = localExecutor ?? throw new ArgumentNullException(nameof(localExecutor));
        _fallbackExecutor = fallbackExecutor ?? throw new ArgumentNullException(nameof(fallbackExecutor));
    }

    public Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return _localExecutor.HasTool(invocation.ToolName)
            ? _localExecutor.ExecuteAsync(invocation, cancellationToken)
            : _fallbackExecutor.ExecuteAsync(invocation, cancellationToken);
    }
}
