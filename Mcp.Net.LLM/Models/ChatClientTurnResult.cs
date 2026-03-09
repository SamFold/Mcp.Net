namespace Mcp.Net.LLM.Models;

public enum ChatErrorSource
{
    Provider,
    Tool,
    Session,
}

public abstract record ChatClientTurnResult;

public sealed record ChatClientAssistantTurn(
    string Id,
    string Provider,
    string Model,
    IReadOnlyList<AssistantContentBlock> Blocks
) : ChatClientTurnResult;

public sealed record ChatClientFailure(
    ChatErrorSource Source,
    string Message,
    string? Code = null,
    string? Details = null,
    bool IsRetryable = false,
    string? Provider = null,
    string? Model = null
) : ChatClientTurnResult;
