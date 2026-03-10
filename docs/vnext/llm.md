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
- `ChatClientOptions` now carries `MaxOutputTokens`, existing agent/Web UI `max_tokens` intent reaches OpenAI and Anthropic request builders, Anthropic honors the shared `Temperature`, and blank `SystemPrompt` no longer injects adapter-owned demo text.
- Provider `RegisterTools` is now idempotent (clear-and-replace) on both OpenAI and Anthropic, with regressions for repeated and replacement registration scenarios.
- `AgentRegistry` now awaits initialization before public cache access, preserves recovery via replaceable reload tasks, and rejects invalid register/update user IDs before initialization blocking.
- Persisted agent settings now round-trip through `FileSystemAgentStore` into `AgentFactory` `ChatClientOptions`, with regression coverage for `JsonElement` coercion of `temperature` and `max_tokens`.
- The 2026-03-08 LLM review follow-ons are now resolved end to end.
- The latest planning review for this lane shifts the next work from review closure to post-parity API-shape decisions on top of the completed provider-streaming, metadata, option-cleanup, idempotent-registration, clone-persistence, agent-registry, and persisted-settings seams; cancellation remains explicitly deferred because the current MCP client/tool-execution path does not support it cleanly.

## Goal

- Complete provider capability parity on top of the new block-based transcript, replay, and streaming-update architecture before revisiting larger API-shape changes.
- Keep the completed provider-parity and review-follow-on work stable while settling the `IChatClient`/session API shape needed ahead of `Mcp.Net.Agent` extraction, and continue deferring cancellation until the MCP client path warrants the extra seam.

## Scope

- In scope:
  - decide whether `IChatClient` should remain conversation-stateful or move to an explicit context-driven request API
  - map the implications of that decision onto `ChatSession`, replay/history loading, provider factories, and the planned `Mcp.Net.Agent` extraction
  - keep the completed OpenAI and Anthropic streaming paths, metadata propagation, idempotent tool registration, clone-persistence fix, agent-registry initialization fix, and persisted-settings regression stable while the API-shape decision lands
  - keep the replay/history transformer and block-based transcript architecture stable while the post-parity API-shape work lands
- Out of scope:
  - session-level cancellation until the MCP client/tool-execution path needs it and supports a cleaner seam
  - an immediate breaking implementation rewrite of `IChatClient` or `ChatSession`
  - another full transcript or message-model rewrite
  - low-level memory or serialization optimization work
  - broad provider-helper extraction beyond changes needed for the active slices

## Current slice

Settle the next post-parity API-shape decision before the `Mcp.Net.Agent` extraction:

1. **`IChatClient` statefulness decision**: decide whether provider clients should remain conversation-stateful or move to an explicit context-driven request API now that replay, transcript persistence, streaming updates, and the review follow-ons are stable.
   - Files: `docs/vnext/llm.md`, `docs/roadmap/llm.md`
   - Code references: `Mcp.Net.LLM/Interfaces/IChatClient.cs`, `Mcp.Net.LLM/ChatSession.cs`, `Mcp.Net.LLM/Replay/ChatTranscriptReplayTransformer.cs`

## Next slices

1. Revisit whether `IChatClient` should expose a breaking `IAsyncEnumerable<T>` stream surface once the client-state decision is settled.
2. Revisit session cancellation only when the MCP client/tool-execution path actually needs it and can support a clean contract without widening unrelated seams prematurely.
3. Revisit a typed activity DTO family only if the existing ephemeral `ToolExecutionUpdated` and `ThinkingStateChanged` events prove too weak once provider streaming is live.
4. **Extract `Mcp.Net.Agent` from `Mcp.Net.LLM`** once the provider-parity lane (options, idempotent tools, deferred-cancellation decision, review follow-ons, and API-shape decisions) is stable. The split follows the pi-mono `pi-ai` / `pi-agent-core` boundary:
   - `Mcp.Net.LLM` becomes a pure provider abstraction: `IChatClient`, provider implementations, `ChatClientOptions`, `ChatClientTurnResult`, `AssistantContentBlock`, `ChatUsage`, `ChatTranscriptEntry` (shared contract for replay), replay transformer, provider factory.
   - `Mcp.Net.Agent` owns orchestration and session management: `ChatSession`, `AgentDefinition`, `IAgentManager`, `IAgentFactory`, `IAgentRegistry`, `IAgentStore`, `IChatSessionEvents`, event args, `IChatHistoryManager`, session metadata, `IToolRegistry`, tool categorization, tool execution coordination.
   - `Mcp.Net.Agent` depends on `Mcp.Net.LLM`, not the reverse.
   - Do this as a single focused PR once API surfaces are settled, not piecemeal during active slices.

## Open decisions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- If and when cancellation moves back into scope, what seam should the MCP client expose so `ChatSession` can cancel tool execution without inventing provider-specific escape hatches? Note: `IMcpClient.CallTool` currently accepts no `CancellationToken`, so session cancellation still requires an MCP client contract change, not just a `ChatSession` parameter addition.
- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?
- Where should `ChatTranscriptEntry` live after the `Mcp.Net.Agent` extraction — it is the shared contract between replay (provider layer) and session persistence (agent layer)? Current plan keeps it in `Mcp.Net.LLM` since replay owns the type definitions.

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Capture the reasons the current stateful `IChatClient` shape exists today (provider history, replay, streaming updates, tool registration) before proposing a replacement.
- Capture what an explicit context-driven request API would require from `ChatSession`, replay/history hydration, and provider implementations.
- Decide whether the `Mcp.Net.Agent` extraction should wait on the statefulness decision or can proceed with the current `IChatClient` surface.
- Add regressions for Anthropic ordered assistant blocks containing reasoning, text, and tool calls under streaming updates.
- Add regressions for stable transcript and block identifiers across partial and final assistant updates.
- Add regressions for tool execution appending `ToolResult` entries instead of mutating transcript state.
- Add regressions for provider failures surfacing as `Error` entries instead of assistant text.
- Add replay/history transform coverage for same-model preservation and degraded cross-model/provider replay.
