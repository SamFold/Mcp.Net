using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Represents an in-process tool that exposes its provider-facing descriptor together with
/// the logic required to execute it.
/// </summary>
public interface ILocalTool
{
    Tool Descriptor { get; }

    Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default
    );
}
