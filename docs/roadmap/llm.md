# Roadmap: Mcp.Net.LLM

## Current focus

- Complete provider capability parity on top of the new block-based transcript, replay, and streaming-update architecture.
- Land option cleanup, cancellation, and the remaining LLM review follow-ons now that Anthropic streaming parity and result metadata propagation are in place, without reopening another broad message-model rewrite.
- The immediate option-cleanup target is shared max-output-token handling, adapter prompt ownership, and Anthropic temperature propagation.

## Near-term sequence

1. Expand `ChatClientOptions` with `MaxOutputTokens`, thread the existing agent/Web UI `max_tokens` intent into it, make Anthropic honor shared temperature, and remove adapter-owned development-artifact prompt defaults.
2. Add session-level cancellation through provider requests and MCP tool execution seams, coordinating the required `IMcpClient` cancellation seam with `Mcp.Net.Client`.
3. Make provider `RegisterTools` behavior idempotent so refreshes cannot duplicate model-facing tool definitions.
4. Revisit the long-term streaming and client-state APIs only after provider parity, metadata, and cancellation work have landed.
5. Resolve the remaining 2026-03-08 LLM review follow-ons around agent registry startup behavior, persisted agent settings application, and clone-persistence truthfulness once the provider-parity lane is stable, or earlier if those become release blockers.

## Recently completed

- Provider clients now honor `ChatClientOptions.SystemPrompt`, nested tool arguments now preserve structured values through both provider adapters, and provider `System` and `Error` responses now surface through `ChatSession`.
- The transcript architecture has shifted to typed `ChatTranscriptEntry` records with block-based assistant content, provider-agnostic replay/history transforms, transcript persistence rehydration, and Web UI discriminated transcript DTOs.
- Anthropic reasoning capture and replay are in place, including visibility-aware degradation for missing signatures and captured probe-based regression coverage for same-provider and cross-provider replay cases.
- `IChatClient`, `ChatSession`, SignalR delivery, and transcript persistence now support durable in-flight assistant updates keyed by stable transcript `Id`.
- OpenAI streaming is now wired into that update seam, including partial text and tool-call reconstruction with stable assistant and block identifiers.
- Anthropic streaming is now wired into that update seam too, including progressive reasoning, text, and tool-use snapshots plus regression coverage proving post-stream tool follow-ups still replay the final assistant turn correctly.
- `Usage` and `StopReason` now propagate through `ChatClientAssistantTurn`, assistant transcript entries, persistence, and Web UI assistant DTOs for both OpenAI and Anthropic, including streaming final-snapshot metadata updates.

## Dependencies and risks

- Session-level cancellation is cross-project work because `IMcpClient.CallTool` currently exposes no `CancellationToken`.
- The completed metadata slice already touched `Mcp.Net.WebUi`; the next option-cleanup slice should stay local unless shared provider option contracts broaden.
- The option-cleanup slice must reconcile the existing agent-level `max_tokens` contract with `ChatClientOptions`; leaving that mismatch in place would keep agent configuration and provider behavior out of sync.
- Removing adapter-owned fallback prompts may break tests or call sites that accidentally relied on hidden provider defaults, so explicit-prompt and blank-prompt behavior both need coverage.
- The active lane should not reopen another transcript or message-model rewrite before metadata, option cleanup, and cancellation work land.
- The March 8 review findings around agent startup and persisted settings remain open even though they are not the first milestones in the provider-parity lane.

## Open questions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?
