# VNext: Mcp.Net.LLM

## Current status

- Provider clients now honor `ChatClientOptions.SystemPrompt` during construction.
- The library-path agent system-prompt regression is covered at both the agent factory layer and the provider-request layer.
- Nested tool arguments now survive the OpenAI and Anthropic adapter paths as structured values, with server-binding compatibility coverage in place.
- `ChatSession` now surfaces provider `System` and `Error` responses through the existing assistant-message event flow on both initial and post-tool turns.
- `Mcp.Net.LLM` now builds and passes the focused LLM/Web UI slice on `OpenAI` `2.9.1` and `Anthropic.SDK` `5.10.0`.
- The standalone SDK probe now captures live OpenAI and Anthropic text, tool, reasoning, and error shapes under `artifacts/llm-probe/`.
- The `ChatSession` spec has been revised away from the earlier flat five-kind item proposal to a block-based transcript model:
  - transcript entries: `User`, `Assistant`, `ToolResult`, `Error`
  - assistant blocks: `Text`, `Reasoning`, `ToolCall`
  - typed replay/history transforms are now considered required architecture
- `Mcp.Net.LLM` now has a provider-agnostic transcript replay transformer with coverage for:
  - same-model opaque reasoning token preservation
  - same-provider cross-model reasoning degradation
  - cross-provider reasoning safety defaults
  - synthetic tool-result repair for unmatched assistant tool calls
- Persisted chat history now stores typed `ChatTranscriptEntry` records instead of flat `StoredChatMessage` rows.
- `ChatSession` resume now rehydrates both its in-memory transcript and provider client history through the replay transformer and provider-specific replay loaders.
- The Web UI adapter bootstrap path now loads persisted transcript before session start, stores all runtime transcript entries including `User`, and no longer injects a fake `system` history message on prompt updates.
- Anthropic assistant turns now capture `ThinkingContent` and `RedactedThinkingContent` into transcript reasoning blocks, and Anthropic replay uses visibility-aware fallbacks so missing signatures degrade to portable text instead of emitting invalid thinking payloads.
- OpenAI replay now rebuilds mixed assistant text-plus-tool-call history as a single assistant message via the SDK `ChatCompletion` mock factory instead of splitting it into two assistant messages.
- Replay/provider tests now use the captured Anthropic reasoning probe fixture for same-provider cross-model degradation, cross-provider handoff safety, and Anthropic thinking round-trips.
- The remaining Web UI chat transport now uses discriminated transcript-entry DTOs for REST history and `ReceiveMessage`, and the controller send-message route now takes a dedicated user-message request DTO instead of the old flat `ChatMessageDto`.
- `IChatClient` and `ChatSession` now expose a typed assistant-turn update seam for streaming: one in-flight assistant transcript entry is updated in place by transcript `Id`, SignalR emits `UpdateMessage` for durable transcript updates, and persisted transcript storage now upserts updated entries instead of appending duplicates.
- The OpenAI chat adapter now uses the SDK streaming chat-completions API when assistant-turn updates are requested, emitting progressive text and tool-call snapshots while preserving stable transcript and block identifiers across partial and final turns.
- The streaming slice keeps `ToolExecutionUpdated` and `ThinkingStateChanged` as separate ephemeral UI events for now; only durable assistant content flows through transcript `Added` and `Updated` events.
- The 2026-03-08 LLM review still has unresolved issues around tool re-registration, agent registry startup behavior, and persisted agent settings.

## Goal

- Replace the current text-first `ChatSession`/`LlmResponse` model with a contract-breaking block-based transcript and event model that matches modern provider behavior.
- Delete superseded `Mcp.Net.LLM` files and abstractions when the cleaner replacement exists instead of carrying tech-debt shims.

## Scope

- In scope:
  - add transcript entry records for `User`, `Assistant`, `ToolResult`, and `Error`
  - add assistant content blocks for `Text`, `Reasoning`, and `ToolCall`
  - replace `IChatSessionEvents` with typed transcript/activity events
  - replace `LlmResponse` and `MessageType` with a typed provider-output model
  - add an explicit replay/history transform seam for provider/model-safe transcript replay
  - migrate the Web UI DTO/adapter/storage slice to the new discriminated model as part of the same vertical slice
  - delete obsolete `Mcp.Net.LLM` files and adapters once their replacements are in place
- Out of scope:
  - preserving compatibility with the current Web UI contracts
  - preserving `AssistantMessageReceived`, `ToolExecutionUpdated`, or `ThinkingStateChanged`
  - a fully designed streaming transport protocol
  - provider `RegisterTools` idempotency

## Current slice

1. Wire Anthropic streaming into the new assistant-turn update seam so Claude can emit real in-flight assistant snapshots with thinking, text, and tool-use blocks.
2. Extend the probe corpus when real mixed reasoning-plus-tool-call streaming payloads become available so coverage can move from synthetic ordering cases to captured provider outputs.
3. Decide whether any provider-specific partial-block metadata needs to be public or should stay internal to provider adapters.

## Next slices

1. Make provider `RegisterTools` behavior idempotent so refreshes cannot duplicate model-facing tool definitions.
2. Revisit whether `IChatClient` should remain stateful or move to an explicit context-driven request API once replay/streaming behavior has settled.
3. Revisit a typed activity DTO family only if the existing ephemeral `ToolExecutionUpdated` and `ThinkingStateChanged` events prove too weak once provider streaming is live.

## Open decisions

- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Add regressions for ordered assistant blocks containing reasoning, text, and tool calls.
- Add regressions for tool execution appending `ToolResult` entries instead of mutating transcript state.
- Add regressions for provider failures surfacing as `Error` entries instead of assistant text.
- Add replay/history transform coverage for same-model preservation and degraded cross-model/provider replay.
