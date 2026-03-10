using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Events;

public enum ChatTranscriptChangeKind
{
    Added,
    Updated,
}

public enum ChatSessionActivity
{
    Idle,
    WaitingForProvider,
    ExecutingTool,
}

public enum ToolCallExecutionState
{
    Queued,
    Running,
    Completed,
    Failed,
}

public sealed class ChatTranscriptChangedEventArgs : EventArgs
{
    public ChatTranscriptChangedEventArgs(
        ChatTranscriptEntry entry,
        ChatTranscriptChangeKind changeKind
    )
    {
        Entry = entry;
        ChangeKind = changeKind;
    }

    public ChatTranscriptEntry Entry { get; }

    public ChatTranscriptChangeKind ChangeKind { get; }
}

public sealed class ChatSessionActivityChangedEventArgs : EventArgs
{
    public ChatSessionActivityChangedEventArgs(
        ChatSessionActivity activity,
        string? turnId = null,
        string? sessionId = null
    )
    {
        Activity = activity;
        TurnId = turnId;
        SessionId = sessionId;
    }

    public ChatSessionActivity Activity { get; }

    public string? TurnId { get; }

    public string? SessionId { get; }
}

public sealed class ToolCallActivityChangedEventArgs : EventArgs
{
    public ToolCallActivityChangedEventArgs(
        string toolCallId,
        string toolName,
        ToolCallExecutionState executionState,
        IReadOnlyDictionary<string, object?>? arguments = null,
        ToolInvocationResult? result = null,
        string? errorMessage = null
    )
    {
        ToolCallId = toolCallId;
        ToolName = toolName;
        ExecutionState = executionState;
        Arguments = arguments;
        Result = result;
        ErrorMessage = errorMessage;
    }

    public string ToolCallId { get; }

    public string ToolName { get; }

    public ToolCallExecutionState ExecutionState { get; }

    public IReadOnlyDictionary<string, object?>? Arguments { get; }

    public ToolInvocationResult? Result { get; }

    public string? ErrorMessage { get; }
}
