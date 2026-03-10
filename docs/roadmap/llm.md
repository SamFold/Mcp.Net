# Roadmap: Mcp.Net.LLM

## Current focus

- Complete provider capability parity on top of the new block-based transcript, replay, and streaming-update architecture.
- Extract `Mcp.Net.Agent` from `Mcp.Net.LLM` now that idempotent tool registration, clone-persistence truthfulness, agent-registry startup ordering, persisted-settings coverage, Anthropic streaming parity, result metadata propagation, and shared option cleanup are in place, without reopening another broad message-model rewrite.
- Cancellation is explicitly delayed until after the `Mcp.Net.Agent` extraction because the current MCP client/tool-execution path does not support it cleanly and it is not the highest-value pre-extraction slice.

## Near-term sequence

1. Extract `Mcp.Net.Agent` from `Mcp.Net.LLM` while preserving the current stateful `IChatClient` contract.
2. Revisit whether `IChatClient` should remain conversation-stateful or move to an explicit context-driven request API after the split.
3. Revisit whether snapshot-based `IProgress<ChatClientAssistantTurn>` updates should remain or move to a breaking `IAsyncEnumerable<T>` surface after the client-state decision.
4. Revisit session cancellation only after the `Mcp.Net.Agent` extraction and only if the MCP client/tool-execution path then needs a clean contract.

## Extraction slice breakdown

1. Bootstrap `Mcp.Net.Agent` as a new project, add it to `Mcp.Net.sln`, and add references from `Mcp.Net.WebUi` and `Mcp.Net.Tests`.
2. Move the agent domain/service layer into `Mcp.Net.Agent`: `AgentDefinition`, `AgentCategory`, `IAgentFactory`, `IAgentManager`, `IAgentRegistry`, `IAgentStore`, `AgentFactory`, `AgentManager`, `AgentRegistry`, `DefaultAgentManager`, store implementations, and the DI registration extension.
3. Move the session/runtime layer into `Mcp.Net.Agent`: `ChatSession`, `IChatSessionEvents`, `ChatSessionEventArgs`, `IChatHistoryManager`, `ChatSessionMetadata`, and the service-provider helpers that create sessions from agents.
4. Move tool inventory/orchestration into `Mcp.Net.Agent`: `IToolRegistry`, `ToolRegistry`, category metadata helpers, and enablement/catalog support types.
5. Keep `IChatClient`, provider implementations, replay/history transform types, transcript entry contracts, API-key providers, prompt/resource catalog services, and completion services in `Mcp.Net.LLM`.
6. Preserve the current Web UI raw-client pass-through (`GetLlmClient`, `GetSystemPrompt`, `SetSystemPrompt`, `ResetConversation`, `RegisterTools`) during the split, then revisit whether to replace it with an agent-facing session API in a later slice.
7. Re-home the agent/session/tool unit tests to the new boundary and keep the existing Web UI adapter/controller/hub regressions green across the project move.

## Post-parity milestone

5. **Extract `Mcp.Net.Agent` from `Mcp.Net.LLM`.** Now that provider parity, shared option cleanup, idempotent tool registration, persisted-settings coverage, and the review follow-ons are stable, split the project along the provider-abstraction / agent-orchestration boundary (inspired by the pi-mono `pi-ai` / `pi-agent-core` separation) while preserving the current `IChatClient` behavior:
   - `Mcp.Net.LLM` becomes a pure, reusable LLM provider library: `IChatClient`, provider implementations, options, turn results, content blocks, usage, transcript entry types, replay transformer, provider factory.
   - `Mcp.Net.Agent` (new project) owns orchestration: `ChatSession`, agent definitions and management, session events, history management, tool registry and categorization, tool execution coordination.
   - `Mcp.Net.Agent` depends on `Mcp.Net.LLM`; the reverse dependency does not exist.
   - Handle the current Web UI raw-client pass-through explicitly during the split instead of bundling a separate statefulness redesign into the same slice.
   - Execute as a single focused PR, not piecemeal.

## Recently completed

- Provider clients now honor `ChatClientOptions.SystemPrompt`, nested tool arguments now preserve structured values through both provider adapters, and provider `System` and `Error` responses now surface through `ChatSession`.
- The transcript architecture has shifted to typed `ChatTranscriptEntry` records with block-based assistant content, provider-agnostic replay/history transforms, transcript persistence rehydration, and Web UI discriminated transcript DTOs.
- Anthropic reasoning capture and replay are in place, including visibility-aware degradation for missing signatures and captured probe-based regression coverage for same-provider and cross-provider replay cases.
- `IChatClient`, `ChatSession`, SignalR delivery, and transcript persistence now support durable in-flight assistant updates keyed by stable transcript `Id`.
- OpenAI streaming is now wired into that update seam, including partial text and tool-call reconstruction with stable assistant and block identifiers.
- Anthropic streaming is now wired into that update seam too, including progressive reasoning, text, and tool-use snapshots plus regression coverage proving post-stream tool follow-ups still replay the final assistant turn correctly.
- `Usage` and `StopReason` now propagate through `ChatClientAssistantTurn`, assistant transcript entries, persistence, and Web UI assistant DTOs for both OpenAI and Anthropic, including streaming final-snapshot metadata updates.
- `ChatClientOptions` now carries shared `MaxOutputTokens`, existing agent/Web UI `max_tokens` intent reaches provider request builders, Anthropic honors shared `Temperature`, and blank `SystemPrompt` no longer injects adapter-owned prompt copy.
- Provider `RegisterTools` is now idempotent (clear-and-replace) on both OpenAI and Anthropic, with regressions for repeated and replacement registration scenarios.
- `AgentManager.CloneAgentAsync` now surfaces persistence failures instead of returning a phantom clone, with a regression covering the failed `RegisterAgentAsync` path.
- `AgentRegistry` now blocks public cache access on initialization, can recover from a failed first reload via manual `ReloadAgentsAsync`, and preserves immediate `userId` validation on register/update paths.
- Persisted agent settings now round-trip through the real `FileSystemAgentStore` path into `AgentFactory` `ChatClientOptions`, with regression coverage for deserialized `JsonElement` temperature and `max_tokens` values.
- The latest extraction review concluded the provider clients can stay stateful for the split because `ChatSession` already treats `IChatClient` as opaque behind replay/load and send methods.
- That same review identified one caveat: Web UI currently reaches through `ChatSession.GetLlmClient()` to call `GetSystemPrompt`, `SetSystemPrompt`, `ResetConversation`, and `RegisterTools`, so the extraction must preserve or replace that pass-through explicitly.

## Dependencies and risks

- Session-level cancellation remains cross-project work because `IMcpClient.CallTool` currently exposes no `CancellationToken`; it stays out of scope until after the `Mcp.Net.Agent` extraction and only comes back when the seam is both needed and worth widening.
- The extraction touches `ChatSession`, agent management/store types, replay/history loading, and Web UI session adapters; it should preserve the current `IChatClient` behavior instead of bundling a statefulness redesign into the same slice.
- The active lane should not reopen another transcript or message-model rewrite while the extraction is still being settled.

## Open questions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?
- Where should `ChatTranscriptEntry` live after the agent extraction — in `Mcp.Net.LLM` (current plan, since replay owns the types) or in a shared contracts package?
