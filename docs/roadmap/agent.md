# Roadmap: Mcp.Net.Agent

## Current focus

- Finish the remaining runtime-hygiene work identified in the readiness review before expanding the built-in tool surface.
- Keep the `ChatSession` lifecycle and factory seams stable while event-fault hardening and transcript lifecycle cleanup land.
- Preserve the now-completed continue/resume and per-turn summary surface while tightening the observer/compaction contract.

## What

- Guard runtime event dispatch so subscriber exceptions cannot fault otherwise healthy turns.
- Change transcript compaction to `CompactAsync(...)` before more consumers depend on the synchronous contract.
- Add explicit transcript change notifications for reset/load flows so observers stop missing whole-state mutations.

## Why

- The runtime and factory seams are now in place and the dead model layer is gone.
- Continue/resume, per-turn summaries, and the dead session-start seam are now in place.
- The remaining high-value gaps are correctness and lifecycle hygiene, not more tool breadth.
- This keeps the consumer runtime stable before the first concrete built-in tools widen adoption.

## How

### Runtime hygiene

- Wrap `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` dispatch so observer bugs are logged and swallowed.
- Change compaction to `CompactAsync(...)` and flow that through the request-build path.
- Add reset/load change kinds or equivalent transcript notifications so whole-transcript mutations become observable.

### Verification

- Add focused regressions proving event-handler faults do not break a turn.
- Add lifecycle coverage for reset/load transcript notifications and async compaction flow.
- Keep the completed `ChatSession` lifecycle tests and broader agent/runtime coverage green.
- Re-run impacted `Mcp.Net.WebUi` adapter coverage because the web path still consumes the same transcript/activity events.

## Near-term sequence

1. Guard runtime event dispatch so observer faults cannot break otherwise healthy turns.
2. Change transcript compaction to `CompactAsync(...)` and close the reset/load transcript event gaps before more consumers depend on the current shape.
3. Add the first concrete built-in/local tools once the core consumer loop is easier to drive directly and the remaining hygiene work is in place.
4. Revisit `IMcpClient` ergonomics when a real caller needs `CallTool` cancellation or async disposal.
5. Revisit session-owned transcript persistence when non-Web UI consumers need durable session state.
6. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
7. Revisit context-window management with a stronger trigger or summarizer path once real conversation pressure justifies it.

## Recently completed

- Removed the obsolete `AgentDefinition` / manager / store / registry model and agent-oriented DI/extensions from `Mcp.Net.Agent`.
- Removed the corresponding agent-driven controllers, DTOs, startup hooks, and chat-factory branches from `Mcp.Net.WebUi`.
- Narrowed the remaining registration story to `AddChatRuntimeServices()` plus `AddChatSessionFactory()`.
- `ChatSession` now flows caller cancellation through provider requests and tool execution.
- Abort behavior is now deterministic for provider waits and tool execution, including partial tool-result persistence when some tool work finished before cancellation.
- `ChatSession` now validates tool execution against its own configured tool catalog and no longer depends on `IToolRegistry` at runtime.
- `Mcp.Net.Agent.Tools` now includes `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor`.
- Focused tests now cover mixed local+MCP turns plus missing-session-tool failure semantics through the shared executor seam.
- `ChatSession` now rejects overlapping turns, exposes `IsProcessing` plus abort/wait lifecycle APIs, and blocks mutable state changes while a turn is active.
- `Mcp.Net.Agent` now includes `IChatSessionFactory`, `ChatSessionFactoryOptions`, and `ChatSessionFactory` for library-first session composition with caller-owned MCP clients.
- `ChatSession` now supports `ContinueAsync(...)` with explicit transcript-tail rules.
- `SendUserMessageAsync(...)` and `ContinueAsync(...)` now return `ChatTurnSummary` so awaited callers can inspect per-turn changes directly.
- `ChatSession` no longer exposes `StartSession()` / `SessionStarted`; session-start notification is now owned by the Web UI adapter where it is actually consumed.

## Dependencies and risks

- Full MCP tool-call cancellation still depends on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- The provider boundary should remain snapshot-based; the runtime should not reintroduce provider-owned conversation state.
- The current event surface is still participation-shaped in practice because subscriber exceptions can escape the turn loop; the next hygiene slice should close that before more consumers attach observers.
- The current compactor shape is still synchronous, which will become a breaking change later if compaction ever needs async summarization work.
- `ResetConversation()` and `LoadTranscriptAsync(...)` still mutate transcript state without change notifications today, so the next lifecycle-hygiene slice should close that observer gap before the tool surface grows.
- The first local tools still need disciplined scope when they land. If they expand into shell/write behavior too early, the slice will mix seam validation with policy decisions.
- The current compaction trigger is intentionally simple; future pressure may require token-aware estimation or a stronger summarizer path.

## Open questions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project as soon as the contracts land?
- Should local tools always be app-owned reusable registrations, or should the runtime explicitly support session-owned local tool instances from the start?
- When context-window pressure grows further, should the next compaction improvement be token-aware estimation or provider-backed summarization?
