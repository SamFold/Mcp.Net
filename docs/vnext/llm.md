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
- The 2026-03-08 LLM review tool re-registration issue is resolved. Three issues remain: agent registry startup race, persisted agent settings round-trip verification, and clone-persistence truthfulness.
- The latest planning review for this lane keeps the next work on the remaining review follow-ons on top of the completed provider-streaming, metadata, option-cleanup, and idempotent-registration seams; it explicitly defers cancellation because the current MCP client/tool-execution path does not support it cleanly.

## Goal

- Complete provider capability parity on top of the new block-based transcript, replay, and streaming-update architecture before revisiting larger API-shape changes.
- Land configurable provider options, idempotent tool registration, and the remaining review follow-ons as focused vertical slices on top of the completed OpenAI and Anthropic streaming and metadata seam, while deferring cancellation until the MCP client path warrants the extra seam.

## Scope

- In scope:
  - resolve the remaining 2026-03-08 LLM review follow-ons (agent registry startup race, persisted agent settings round-trip, clone-persistence truthfulness)
  - keep the completed OpenAI and Anthropic streaming paths, metadata propagation, and idempotent tool registration stable while review follow-ons land
  - keep the replay/history transformer and block-based transcript architecture stable while provider parity lands
- Out of scope:
  - session-level cancellation until the MCP client/tool-execution path needs it and supports a cleaner seam
  - another full transcript or message-model rewrite
  - an immediate breaking replacement of `IProgress<ChatClientAssistantTurn>` with `IAsyncEnumerable<T>`
  - low-level memory or serialization optimization work
  - broad provider-helper extraction beyond changes needed for the active slices

## Current slice

Resolve the three remaining 2026-03-08 LLM review follow-ons:

1. **Agent registry startup race**: `AgentRegistry` constructor fires `ReloadAgentsAsync()` without awaiting it; `DefaultAgentInitializer` immediately reads `GetAllAgentsAsync()` and can observe an empty cache, creating duplicate default agents. Fix: make initialization awaitable so `DefaultAgentInitializer` runs after the registry is loaded.
   - Files: `Mcp.Net.LLM/Agents/AgentRegistry.cs`, `Mcp.Net.WebUi/Infrastructure/DefaultAgentInitializer.cs`

2. **Persisted agent settings round-trip**: Parameter coercion in `AgentFactory` was hardened but needs regression coverage proving that values persisted through `FileSystemAgentStore` → `AgentDefinition.Parameters` → `AgentFactory` → `ChatClientOptions` actually reach provider request builders (temperature, max output tokens).
   - Files: `Mcp.Net.LLM/Agents/AgentFactory.cs`, `Mcp.Net.LLM/Agents/Stores/FileSystemAgentStore.cs`, `Mcp.Net.LLM/Models/AgentDefinition.cs`

3. **Clone-persistence truthfulness**: `AgentManager.CloneAgentAsync` ignores the `bool` return from `RegisterAgentAsync` — if persistence fails, the caller gets a phantom agent that was never saved. Fix: check the return and throw or propagate the failure.
   - Files: `Mcp.Net.LLM/Agents/AgentManager.cs`

## Next slices

1. Revisit session cancellation only when the MCP client/tool-execution path actually needs it and can support a clean contract without widening unrelated seams prematurely.
2. Revisit session cancellation only when the MCP client/tool-execution path actually needs it and can support a clean contract without widening unrelated seams prematurely.
3. Revisit whether `IChatClient` should expose a breaking `IAsyncEnumerable<T>` stream surface once provider parity, metadata, and tool-registration behavior are stable.
4. Revisit whether `IChatClient` should remain stateful or move to an explicit context-driven request API once replay and streaming behavior have settled.
5. Revisit a typed activity DTO family only if the existing ephemeral `ToolExecutionUpdated` and `ThinkingStateChanged` events prove too weak once provider streaming is live.
6. **Extract `Mcp.Net.Agent` from `Mcp.Net.LLM`** once the provider-parity lane (options, idempotent tools, deferred-cancellation decision, review follow-ons) is stable. The split follows the pi-mono `pi-ai` / `pi-agent-core` boundary:
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
- Add regression proving `AgentRegistry` initialization completes before `DefaultAgentInitializer` checks for existing agents.
- Add regression proving `AgentFactory` round-trips persisted temperature and max-output-tokens through to provider request parameters.
- Add regression proving `CloneAgentAsync` surfaces persistence failures instead of returning phantom agents.
- Add regressions for Anthropic ordered assistant blocks containing reasoning, text, and tool calls under streaming updates.
- Add regressions for stable transcript and block identifiers across partial and final assistant updates.
- Add regressions for tool execution appending `ToolResult` entries instead of mutating transcript state.
- Add regressions for provider failures surfacing as `Error` entries instead of assistant text.
- Add replay/history transform coverage for same-model preservation and degraded cross-model/provider replay.
