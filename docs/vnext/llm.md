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
- The Anthropic chat adapter now uses the SDK streaming messages API when assistant-turn updates are requested, emitting progressive reasoning, text, and tool-use snapshots while preserving stable transcript and block identifiers across partial and final turns.
- Stable assistant and block identifiers are now considered required behavior for transcript upserts and incremental Web UI updates, not an optional implementation detail.
- `ChatClientAssistantTurn`, `AssistantChatEntry`, and the Web UI assistant transcript DTOs now carry explicit `Usage` and `StopReason`, and both OpenAI and Anthropic populate that metadata on non-streaming and streaming turns.
- The streaming slice keeps `ToolExecutionUpdated` and `ThinkingStateChanged` as separate ephemeral UI events for now; only durable assistant content flows through transcript `Added` and `Updated` events.
- Agent definitions and extensions already expose a `max_tokens` parameter, but `ChatClientOptions` still drops that knob before it reaches provider request builders.
- The 2026-03-08 LLM review still has unresolved issues around tool re-registration, agent registry startup behavior, and persisted agent settings.
- The latest planning review for this lane keeps the next work on option cleanup, cancellation, and review follow-ons on top of the completed provider-streaming and metadata seam; it explicitly does not reopen a broad message-model rewrite first.

## Goal

- Complete provider capability parity on top of the new block-based transcript, replay, and streaming-update architecture before revisiting larger API-shape changes.
- Land configurable provider options, session cancellation, and the remaining review follow-ons as focused vertical slices on top of the completed OpenAI and Anthropic streaming and metadata seam.

## Scope

- In scope:
  - expand `ChatClientOptions` with a small, typed shared surface, starting with max output tokens, so existing agent-level `max_tokens` intent reaches provider request builders instead of being silently dropped
  - remove adapter-owned development-artifact prompt defaults so blank `SystemPrompt` no longer injects demo copy from the provider clients
  - make Anthropic honor the shared `Temperature` option instead of hardcoding `1.0m`
  - add session-level cancellation through provider calls and MCP tool execution seams
  - make provider `RegisterTools` behavior idempotent so refreshes cannot duplicate model-facing tool definitions
  - keep the completed OpenAI and Anthropic streaming paths and metadata propagation stable while option and cancellation work land
  - keep the replay/history transformer and block-based transcript architecture stable while provider parity lands
- Out of scope:
  - another full transcript or message-model rewrite
  - an immediate breaking replacement of `IProgress<ChatClientAssistantTurn>` with `IAsyncEnumerable<T>`
  - low-level memory or serialization optimization work
  - broad provider-helper extraction beyond changes needed for the active slices

## Current slice

1. Add `MaxOutputTokens` to `ChatClientOptions` and thread existing agent and Web UI `max_tokens` settings into it so OpenAI `ChatCompletionOptions.MaxOutputTokenCount` and Anthropic `MessageParameters.MaxTokens` are driven by the same shared knob.
2. Make Anthropic `CreateMessageParameters` honor `ChatClientOptions.Temperature`, while keeping OpenAI's existing unsupported-model omission behavior intact.
3. Remove the adapter-owned Warhammer/demo fallback prompts so blank `SystemPrompt` stops injecting library-owned copy into outbound provider requests; explicit system prompts must continue to pass through unchanged.
4. Add focused regressions for max-output-token propagation, Anthropic temperature propagation, and absence of injected default prompts without broad provider-helper extraction.

## Next slices

1. Add session-level cancellation through `ChatSession`, including any required MCP client seam changes for tool execution.
2. Make provider `RegisterTools` behavior idempotent so refreshes cannot duplicate model-facing tool definitions.
3. Resolve the remaining 2026-03-08 LLM review follow-ons around agent registry startup behavior, persisted agent settings, and clone-persistence truthfulness once the provider-parity lane is stable, or earlier if those become release blockers.
4. Revisit whether `IChatClient` should expose a breaking `IAsyncEnumerable<T>` stream surface once provider parity and metadata are stable.
5. Revisit whether `IChatClient` should remain stateful or move to an explicit context-driven request API once replay and streaming behavior have settled.
6. Revisit a typed activity DTO family only if the existing ephemeral `ToolExecutionUpdated` and `ThinkingStateChanged` events prove too weak once provider streaming is live.

## Open decisions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- What cancellation seam should the MCP client expose so `ChatSession` can cancel tool execution without inventing provider-specific escape hatches? Note: `IMcpClient.CallTool` currently accepts no `CancellationToken`, so session cancellation requires an MCP client contract change, not just a `ChatSession` parameter addition.
- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Add regressions that existing agent/Web UI `max_tokens` intent reaches `ChatClientOptions` and both provider request builders.
- Add regressions that explicit system prompts still pass through while blank `SystemPrompt` no longer injects adapter-owned demo text.
- Add regressions that Anthropic request construction uses `ChatClientOptions.Temperature` and `MaxOutputTokens`.
- Add regressions for Anthropic ordered assistant blocks containing reasoning, text, and tool calls under streaming updates.
- Add regressions for stable transcript and block identifiers across partial and final assistant updates.
- Add regressions for tool execution appending `ToolResult` entries instead of mutating transcript state.
- Add regressions for provider failures surfacing as `Error` entries instead of assistant text.
- Add regressions for `ChatClientOptions` propagation and provider default cleanup once those fields land.
- Add replay/history transform coverage for same-model preservation and degraded cross-model/provider replay.
