# Mcp.Net.LLM

Stateless provider abstraction for LLM chat completions. Supports Anthropic and OpenAI with a unified request/response contract. No session state, no tool execution, no conversation management — just clean provider I/O.

## Core contract

```csharp
public interface IChatClient
{
    IChatCompletionStream SendAsync(ChatClientRequest request, CancellationToken cancellationToken = default);
}
```

`ChatClientRequest` is an immutable snapshot: system prompt, transcript, tool definitions, and optional request parameters. The provider executes it and returns a stream.

`IChatCompletionStream` implements `IAsyncEnumerable<ChatClientAssistantTurn>` for streaming updates, with `GetResultAsync()` for the final result. Supports both streaming enumeration and result-only usage.

## Providers

| Provider | Package | Models |
|----------|---------|--------|
| Anthropic | `Anthropic.SDK` | Claude Sonnet 4.5, Claude Opus 4, etc. |
| OpenAI | `OpenAI` | GPT-5, o1, etc. |

Create clients through the factory:

```csharp
var factory = new ChatClientFactory(openAiLogger, anthropicLogger);
IChatClient client = factory.Create(LlmProvider.Anthropic, new ChatClientOptions { Model = "claude-sonnet-4-5-20250929" });
```

## Key types

| Type | Purpose |
|------|---------|
| `ChatClientRequest` | Immutable request: system prompt, transcript, tools, options |
| `ChatClientTurnResult` | Base for `ChatClientAssistantTurn` (success) and `ChatClientFailure` (error) |
| `AssistantContentBlock` | `TextAssistantBlock`, `ReasoningAssistantBlock`, `ToolCallAssistantBlock` |
| `ChatTranscriptEntry` | `UserChatEntry`, `AssistantChatEntry`, `ToolResultChatEntry`, `ErrorChatEntry` |
| `ToolInvocationResult` | Provider-agnostic tool result with text, structured data, and resource links |
| `ChatRequestOptions` | Temperature, max output tokens, tool choice |
| `ChatUsage` | Input/output token counts with provider-specific additional counts |

## Design

This library owns no conversation state. The caller (typically `Mcp.Net.Agent`) builds a `ChatClientRequest` from its own transcript, sends it, and processes the result. Provider adapters translate to/from SDK-specific formats at request time.

Cross-provider transcript replay is handled by `ChatTranscriptReplayTransformer`, which manages reasoning block conversion and unmatched tool call recovery when switching between providers mid-conversation.

## Dependencies

- `Anthropic.SDK`
- `OpenAI`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Configuration.Abstractions`

No project references. This library is intentionally standalone.
