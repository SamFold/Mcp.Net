using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Executes only the local tools explicitly registered with it.
/// </summary>
public sealed class LocalToolExecutor : IToolExecutor
{
    private readonly Dictionary<string, ILocalTool> _localTools = new(StringComparer.OrdinalIgnoreCase);

    public LocalToolExecutor(IEnumerable<ILocalTool> localTools)
    {
        ArgumentNullException.ThrowIfNull(localTools);

        foreach (var localTool in localTools)
        {
            ArgumentNullException.ThrowIfNull(localTool);

            if (!_localTools.TryAdd(localTool.Descriptor.Name, localTool))
            {
                throw new ArgumentException(
                    $"A local tool named '{localTool.Descriptor.Name}' is already registered.",
                    nameof(localTools)
                );
            }
        }
    }

    public bool HasTool(string toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && _localTools.ContainsKey(toolName);

    public Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_localTools.TryGetValue(invocation.ToolName, out var localTool))
        {
            throw new KeyNotFoundException(
                $"Local tool '{invocation.ToolName}' is not registered."
            );
        }

        return localTool.ExecuteAsync(
            new ToolInvocation(invocation.ToolCallId, localTool.Descriptor.Name, invocation.Arguments),
            cancellationToken
        );
    }
}
