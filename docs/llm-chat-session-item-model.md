# ChatSession Transcript And Event Model

## Status

This document replaces the earlier compatibility-oriented five-kind item proposal.

The previous proposal fixed real problems in the current `ChatSession` API, but it kept the wrong top-level shape:

- it flattened one assistant turn into multiple sibling transcript items
- it treated tool execution progress as transcript state
- it deferred the replay/history transform problem even though provider behavior already requires it
- it preserved `IChatSessionEvents`, `LlmResponse`, and `MessageType` longer than they deserve

Breaking current consumers is acceptable here. The goal is the cleanest long-term `Mcp.Net.LLM` surface.

Obsolete `Mcp.Net.LLM` files, types, and adapters should be deleted when replacement is ready. Do not keep dead compatibility shells just to avoid removing code.

## Design Decisions

1. The public transcript is not a five-kind flat list.
2. The public transcript is a list of typed entries:
   - `User`
   - `Assistant`
   - `ToolResult`
   - `Error`
3. `Assistant` entries contain ordered typed blocks:
   - `Text`
   - `Reasoning`
   - `ToolCall`
4. `System` is not a public transcript entry.
5. `ToolCall` and `ToolResult` are distinct public concepts.
6. `Reasoning` is public, including visible, redacted, and opaque forms.
7. `Error` is both transcript-visible and event-visible.
8. `IChatSessionEvents` should be replaced, not extended with compatibility shims.
9. `LlmResponse` and `MessageType` should be replaced with a typed provider-output model.
10. A replay/history transform layer is required architecture, not optional cleanup.
11. When an old `Mcp.Net.LLM` file or abstraction no longer fits the new model, prefer deleting it over wrapping it.

## Why The Current Surface Fails

Today `Mcp.Net.LLM` collapses too many different cases into string messages and side-channel events:

- `ChatSession` routes provider `System` and `Error` responses through assistant text.
- `MessageType.Tool` conflates model-issued tool calls with host-issued tool results.
- `LlmResponse` cannot express a provider turn containing ordered reasoning, text, and tool calls.
- `ToolExecutionUpdated` describes execution progress, but the transcript has no first-class tool result entry.
- `StoredChatMessage` and `ChatMessageDto` are too flat to persist or render reasoning and tool semantics correctly.

That loses structure already present in the provider surfaces we care about:

- OpenAI Responses can return a reasoning item and an assistant message item in one turn.
- Anthropic can return thinking blocks, text blocks, and tool-use blocks in one turn.
- Provider failures are exceptions or failed responses, not transcript `System` messages.

## Goals

- Represent one assistant turn as one transcript entry with ordered typed content.
- Preserve reasoning as a first-class public concept without leaking raw SDK types.
- Separate transcript semantics from ephemeral execution activity.
- Make replay/history transformations explicit and testable.
- Allow `Mcp.Net.WebUi` and storage to map directly from the `Mcp.Net.LLM` transcript model.

## Non-Goals

- Preserving the current `IChatSessionEvents` contract.
- Preserving `LlmResponse`, `LlmMessage`, or `MessageType` as compatibility layers.
- Preserving the current Web UI DTO or stored message shape.
- Designing the final streaming transport protocol in this document.

## Provider Findings That Drive The Model

### OpenAI

- Chat completions produce assistant text and tool calls.
- Provider failures surface as exceptions, not transcript content.
- Responses API can emit a separate reasoning item plus a message item in the same turn.
- Reasoning may be opaque: replay metadata can exist even when there is no useful user-visible reasoning text.

### Anthropic

- Messages can contain text, visible thinking, redacted thinking, and tool use in one turn.
- Redacted thinking is real provider output and must be representable without pretending it is assistant text.
- Provider failures surface as exceptions, not transcript content.

### Consequence

The public model must preserve:

- ordered assistant output blocks
- reasoning with visible, redacted, and opaque forms
- tool calls as assistant output
- tool results as distinct host output
- errors as first-class entries, not fake assistant text

## Public Transcript Model

### Transcript entry kinds

```csharp
public enum ChatTranscriptEntryKind
{
    User,
    Assistant,
    ToolResult,
    Error,
}
```

### Assistant block kinds

```csharp
public enum AssistantContentBlockKind
{
    Text,
    Reasoning,
    ToolCall,
}

public enum ReasoningVisibility
{
    Visible,
    Redacted,
    Opaque,
}

public enum ChatErrorSource
{
    Provider,
    Tool,
    Session,
}
```

### Base transcript entry

```csharp
public abstract record ChatTranscriptEntry(
    string Id,
    ChatTranscriptEntryKind Kind,
    DateTimeOffset Timestamp,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null
);
```

`TurnId` groups entries produced while handling one user turn. It is metadata, not a replacement for the ordered transcript.

### Concrete transcript entries

```csharp
public sealed record UserChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    string Content,
    string? TurnId = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.User, Timestamp, TurnId);

public sealed record AssistantChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    IReadOnlyList<AssistantContentBlock> Blocks,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.Assistant, Timestamp, TurnId, Provider, Model);

public sealed record ToolResultChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    string ToolCallId,
    string ToolName,
    ToolInvocationResult Result,
    bool IsError,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.ToolResult, Timestamp, TurnId, Provider, Model);

public sealed record ErrorChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    ChatErrorSource Source,
    string Message,
    string? Code = null,
    string? Details = null,
    string? RelatedEntryId = null,
    bool IsRetryable = false,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.Error, Timestamp, TurnId, Provider, Model);
```

### Assistant content blocks

```csharp
public abstract record AssistantContentBlock(
    string Id,
    AssistantContentBlockKind Kind
);

public sealed record TextAssistantBlock(
    string Id,
    string Text
) : AssistantContentBlock(Id, AssistantContentBlockKind.Text);

public sealed record ReasoningAssistantBlock(
    string Id,
    string? Text,
    ReasoningVisibility Visibility,
    string? ReplayToken = null
) : AssistantContentBlock(Id, AssistantContentBlockKind.Reasoning);

public sealed record ToolCallAssistantBlock(
    string Id,
    string ToolCallId,
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments
) : AssistantContentBlock(Id, AssistantContentBlockKind.ToolCall);
```

### Why This Shape

- `Assistant` is the transcript atom produced by the provider.
- `Text`, `Reasoning`, and `ToolCall` are ordered parts of one assistant turn.
- `ToolResult` is a separate transcript entry because it is not provider output. It is host output sent back to the provider later.
- `Error` is a transcript entry because users and UIs often need to see failures in the conversation timeline.
- `System` stays outside the transcript as session configuration or request context.

## Event Surface

The current `IChatSessionEvents` contract should be replaced.

The canonical public event surface should separate durable transcript changes from transient execution activity.

```csharp
public interface IChatSessionEvents
{
    event EventHandler? SessionStarted;
    event EventHandler<ChatTranscriptChangedEventArgs>? TranscriptChanged;
    event EventHandler<ChatSessionActivityChangedEventArgs>? ActivityChanged;
    event EventHandler<ToolCallActivityChangedEventArgs>? ToolCallActivityChanged;
}

public enum ChatTranscriptChangeKind
{
    Added,
    Updated,
}

public sealed class ChatTranscriptChangedEventArgs : EventArgs
{
    public ChatTranscriptChangedEventArgs(
        ChatTranscriptEntry entry,
        ChatTranscriptChangeKind changeKind)
    {
        Entry = entry;
        ChangeKind = changeKind;
    }

    public ChatTranscriptEntry Entry { get; }
    public ChatTranscriptChangeKind ChangeKind { get; }
}

public enum ChatSessionActivity
{
    Idle,
    WaitingForProvider,
    ExecutingTool,
}

public sealed class ChatSessionActivityChangedEventArgs : EventArgs
{
    public ChatSessionActivityChangedEventArgs(
        ChatSessionActivity activity,
        string? turnId = null,
        string? sessionId = null)
    {
        Activity = activity;
        TurnId = turnId;
        SessionId = sessionId;
    }

    public ChatSessionActivity Activity { get; }
    public string? TurnId { get; }
    public string? SessionId { get; }
}

public enum ToolCallExecutionState
{
    Queued,
    Running,
    Completed,
    Failed,
}

public sealed class ToolCallActivityChangedEventArgs : EventArgs
{
    public ToolCallActivityChangedEventArgs(
        string toolCallId,
        string toolName,
        ToolCallExecutionState executionState,
        ToolInvocationResult? result = null,
        string? errorMessage = null)
    {
        ToolCallId = toolCallId;
        ToolName = toolName;
        ExecutionState = executionState;
        Result = result;
        ErrorMessage = errorMessage;
    }

    public string ToolCallId { get; }
    public string ToolName { get; }
    public ToolCallExecutionState ExecutionState { get; }
    public ToolInvocationResult? Result { get; }
    public string? ErrorMessage { get; }
}
```

### Event Rules

- `TranscriptChanged` is the canonical source for anything durable, including errors.
- `ActivityChanged` replaces `ThinkingStateChanged`.
- `ToolCallActivityChanged` reports ephemeral execution progress and final execution outcome.
- A tool execution failure should usually produce both:
  - `ToolCallActivityChanged(Failed)`
  - `TranscriptChanged(Added, ToolResultChatEntry { IsError = true })`
- A provider failure should usually produce:
  - `TranscriptChanged(Added, ErrorChatEntry { Source = Provider })`
  - `ActivityChanged(Idle)` after the turn unwinds

No assistant-text fallback event should exist.

## Mapping Rules

### User input

- `SendUserMessageAsync` appends one `UserChatEntry`.

### Provider assistant output

- One provider turn becomes one `AssistantChatEntry`.
- Preserve provider order across blocks.
- Map OpenAI and Anthropic visible text to `TextAssistantBlock`.
- Map Anthropic visible thinking and OpenAI reasoning summary text to `ReasoningAssistantBlock(Text != null, Visibility = Visible)`.
- Map Anthropic redacted thinking to `ReasoningAssistantBlock(Text = null or placeholder text, Visibility = Redacted, ReplayToken = opaque payload)`.
- Map OpenAI opaque reasoning to `ReasoningAssistantBlock(Text = null, Visibility = Opaque, ReplayToken = provider reasoning item payload)`.
- Map model-issued tool calls to `ToolCallAssistantBlock`.

### Tool execution

- Tool execution state is not represented by mutating the `ToolCallAssistantBlock`.
- When a tool starts, raise `ToolCallActivityChanged(Queued|Running)`.
- When a tool completes, append `ToolResultChatEntry`.
- When a tool fails, append `ToolResultChatEntry { IsError = true }`.

### Errors

- Provider failures become `ErrorChatEntry { Source = Provider }`.
- Session orchestration failures become `ErrorChatEntry { Source = Session }`.
- Tool failures that already have a `ToolResultChatEntry { IsError = true }` do not also need a separate `ErrorChatEntry` unless the failure is not attributable to a specific tool call.

## System Prompt Handling

`System` is the wrong abstraction for the public transcript.

System prompts should live on session options, request context, or session metadata. They are input configuration, not user-visible conversation output. Remove `MessageType.System` from the long-term public model.

## Typed Provider Output Model

`LlmResponse` and `MessageType` should be replaced, not extended.

The provider boundary should emit typed outputs that already preserve assistant blocks and failures.

```csharp
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
    bool IsRetryable = false
) : ChatClientTurnResult;
```

At minimum, `IChatClient` should stop returning `IEnumerable<LlmResponse>`. `ChatSession` should consume a typed assistant turn or a typed failure directly.

Whether `IChatClient` remains stateful or moves to an explicit context-driven request API can be decided separately. That is secondary to removing `LlmResponse` and `MessageType`.

## Replay And History Transform Layer

This layer is required.

The stored transcript is not automatically valid provider input. Modern provider APIs impose replay constraints that must be handled explicitly.

Introduce a dedicated transform seam, for example:

```csharp
public interface IChatTranscriptReplayTransformer
{
    ProviderReplayTranscript Transform(
        IReadOnlyList<ChatTranscriptEntry> transcript,
        ReplayTarget target);
}
```

### Required transform rules

1. Never replay `ErrorChatEntry` back to providers.
2. Preserve `ReasoningAssistantBlock.ReplayToken` only when the target model can legally consume it.
3. For same-provider, same-model replay:
   - preserve opaque or redacted reasoning tokens
   - preserve provider-native tool call identifiers
4. For same-provider, different-model replay:
   - degrade visible reasoning to plain text when safe
   - drop opaque-only reasoning tokens that are model-specific
   - normalize or strip provider-specific tool-call identifiers when the target model validates pairing history
5. For cross-provider replay:
   - never forward opaque reasoning tokens
   - convert visible reasoning to plain text only if that is an explicit policy
   - otherwise drop reasoning blocks that are not safely portable
6. If the transcript contains an assistant tool call without a matching tool result, either:
   - synthesize an error tool result for providers that require pairing, or
   - truncate replay at the last valid boundary

This transform layer is where `pi-mono` is directionally right, and `Mcp.Net.LLM` will need the same class of logic.

## Persistence And Web UI Implications

The current `StoredChatMessage` and `ChatMessageDto` shapes are insufficient.

### Persistence

Replace the flat message shape with a discriminated transcript-entry shape that mirrors the public model:

- store transcript entry kind directly
- store assistant blocks as ordered structured data
- store tool results separately from tool calls
- store reasoning replay tokens in structured metadata, not in overloaded assistant text
- keep system prompt in session metadata, not in transcript rows

### Web UI

`SignalRChatAdapter` should subscribe to `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` and map those to typed DTOs.

`ChatMessageDto` should become a discriminated DTO family or a DTO with a typed payload model. A flat `Type` plus `Content` string should not remain the main UI contract.

## Recommended Implementation Order

### Phase 1: Replace the core model

- add transcript entry records and assistant block records
- replace `IChatSessionEvents`
- replace `LlmResponse` and `MessageType`
- update `ChatSession` to emit `User`, `Assistant`, `ToolResult`, and `Error` entries
- delete superseded `Mcp.Net.LLM` files and helper types instead of keeping compatibility scaffolding

### Phase 2: Add replay transforms

- implement the explicit transcript replay transformer
- add same-model and degraded cross-model replay tests
- ensure opaque reasoning is preserved only when legal

### Phase 3: Migrate Web UI and persistence

- replace flat DTOs and stored message shapes
- update `SignalRChatAdapter`
- add persistence and history replay coverage

### Phase 4: Streaming deltas

- add partial assistant entry updates at the block level
- keep transcript semantics unchanged
- use `TranscriptChanged(Updated)` for partial assistant entries only

## Test Expectations

The implementation driven by this spec should cover:

- one assistant turn containing ordered `Reasoning`, `Text`, and `ToolCall` blocks
- tool completion appending `ToolResultChatEntry`
- tool failure appending `ToolResultChatEntry { IsError = true }`
- provider failure appending `ErrorChatEntry`, not assistant text
- opaque reasoning persistence and same-model replay
- degraded replay behavior across different models/providers
- Web UI and persistence handling of the discriminated transcript model

## Summary

The long-term public surface should be:

- transcript entries: `User`, `Assistant`, `ToolResult`, `Error`
- assistant blocks: `Text`, `Reasoning`, `ToolCall`
- transient activity: `ActivityChanged`, `ToolCallActivityChanged`

The long-term surface should not be:

- flat five-kind transcript items
- mutable tool-call transcript state
- `System` transcript messages
- stringly assistant/error fallbacks
- compatibility shims around `IChatSessionEvents`, `LlmResponse`, or `MessageType`
