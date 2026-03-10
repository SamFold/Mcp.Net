# Roadmap: Mcp.Net.LLM

## Current focus

- Complete provider capability parity on top of the new block-based transcript, replay, and streaming-update architecture.
- Land cancellation, tool-registration idempotency, and the remaining LLM review follow-ons now that Anthropic streaming parity, result metadata propagation, and shared option cleanup are in place, without reopening another broad message-model rewrite.
- The immediate target is session cancellation through provider requests and MCP tool execution seams.

## Near-term sequence

1. Add session-level cancellation through provider requests and MCP tool execution seams, coordinating the required `IMcpClient` cancellation seam with `Mcp.Net.Client`.
2. Make provider `RegisterTools` behavior idempotent so refreshes cannot duplicate model-facing tool definitions.
3. Resolve the remaining 2026-03-08 LLM review follow-ons around agent registry startup behavior, persisted agent settings application, and clone-persistence truthfulness once the provider-parity lane is stable, or earlier if those become release blockers.
4. Revisit the long-term streaming and client-state APIs only after provider parity, metadata, and cancellation work have landed.

## Post-parity milestone

5. **Extract `Mcp.Net.Agent` from `Mcp.Net.LLM`.** Once options, cancellation, idempotent tool registration, and review follow-ons are stable, split the project along the provider-abstraction / agent-orchestration boundary (inspired by the pi-mono `pi-ai` / `pi-agent-core` separation):
   - `Mcp.Net.LLM` becomes a pure, reusable LLM provider library: `IChatClient`, provider implementations, options, turn results, content blocks, usage, transcript entry types, replay transformer, provider factory.
   - `Mcp.Net.Agent` (new project) owns orchestration: `ChatSession`, agent definitions and management, session events, history management, tool registry and categorization, tool execution coordination.
   - `Mcp.Net.Agent` depends on `Mcp.Net.LLM`; the reverse dependency does not exist.
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

## Dependencies and risks

- Session-level cancellation is cross-project work because `IMcpClient.CallTool` currently exposes no `CancellationToken`.
- The completed metadata and option-cleanup slices already touched `Mcp.Net.WebUi`; the next cancellation slice should stay local unless the MCP client seam broadens further than `CancellationToken` threading.
- The active lane should not reopen another transcript or message-model rewrite before metadata, option cleanup, and cancellation work land.
- The March 8 review findings around agent startup and persisted settings remain open even though they are not the first milestones in the provider-parity lane.

## Open questions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?
- Where should `ChatTranscriptEntry` live after the agent extraction — in `Mcp.Net.LLM` (current plan, since replay owns the types) or in a shared contracts package?
