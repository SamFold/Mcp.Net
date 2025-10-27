using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Events;

/// <summary>
/// Event arguments for tool execution updates
/// </summary>
public class ToolExecutionEventArgs : EventArgs
{
    public ToolExecutionEventArgs(
        ToolInvocation invocation,
        ToolExecutionState executionState,
        bool success,
        string? errorMessage = null,
        ToolInvocationResult? result = null
    )
    {
        Invocation = invocation;
        ExecutionState = executionState;
        Success = success;
        ErrorMessage = errorMessage;
        Result = result;
    }

    /// <summary>
    /// The invocation associated with this event.
    /// </summary>
    public ToolInvocation Invocation { get; }

    /// <summary>
    /// The current state of the tool execution.
    /// </summary>
    public ToolExecutionState ExecutionState { get; }

    /// <summary>
    /// Indicates whether the operation has succeeded so far.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error message populated when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// The final result payload, when available.
    /// </summary>
    public ToolInvocationResult? Result { get; }

    public string ToolName => Invocation.Name;
}
