# VNext: Mcp.Net.Agent

## Current status

- `ChatSession` now owns prompt, transcript, request-default, and provider-facing tool state for the agent loop.
- The provider boundary is clean: `ChatSession` builds `ChatClientRequest` snapshots and `Mcp.Net.LLM` executes them through `IChatClient.SendAsync(...)`.
- `ChatSession` now validates tool execution against its own session-owned tool catalog; `IToolRegistry` is no longer a runtime dependency inside the loop.
- Tool execution now sits behind `IToolExecutor`, with `McpToolExecutor`, `LocalToolExecutor`, and `CompositeToolExecutor` available under `Mcp.Net.Agent.Tools`.
- Mixed local+MCP turns are now covered through the shared executor seam, and missing-session-tool failures are enforced from the session-owned catalog.
- `SendUserMessageAsync(...)` now accepts a `CancellationToken` and flows it through provider requests plus tool execution.
- Provider-wait cancellation and tool-execution cancellation are now deterministic, including partial tool-result persistence when some tool work finished before the turn was canceled.
- `ChatSession` is now explicitly single-active-turn: overlapping sends are rejected, mutators are guarded while a turn is active, and the session exposes `IsProcessing`, `AbortCurrentTurn()`, and `WaitForIdleAsync(...)`.
- The library now has a session-composition seam: `IChatSessionFactory`, `ChatSessionFactoryOptions`, and `ChatSessionFactory` can build sessions from local tools plus an optional caller-owned `IMcpClient`.
- The obsolete `AgentDefinition` / manager / registry / store stack has been removed; `Mcp.Net.Agent` is now centered on runtime/session concerns only.
- `ChatSession` now supports `ContinueAsync(...)` with explicit transcript-tail validation for resumed turns.
- `SendUserMessageAsync(...)` and `ContinueAsync(...)` now return `ChatTurnSummary`, including per-turn transcript additions/updates plus completed/cancelled status.
- `ChatSession` no longer exposes `StartSession()` / `SessionStarted`; session-start notification now lives in the Web UI adapter that actually broadcasts it.
- `ChatSession` now guards `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` per subscriber so observer exceptions are logged and swallowed instead of faulting turns.
- Transcript compaction now uses `CompactAsync(...)`, and `ChatSession` awaits it with turn-cancellation propagation before provider requests.

## Goal

- Close the remaining transcript-lifecycle hygiene gap before shipping the first concrete built-in/local tools.

## What

- Add explicit reset/load transcript notifications so observer-visible transcript state stays coherent.

## Why

- The runtime and factory seams are now in place and the obsolete model layer is gone.
- Continue/resume, awaited turn summaries, guarded event dispatch, and async compaction are now in place, so the next highest-value issue is transcript lifecycle hygiene.
- Whole-transcript mutations are still invisible to observers.
- This is the last narrow runtime cleanup before the first bounded built-in tools.

## How

### 1. Make transcript lifecycle observable

- Add reset/load transcript notifications via new change kinds or an equivalent explicit event shape.
- Keep transcript mutation semantics stable while making whole-state changes visible to observers.
- Re-run the Web UI adapter/event tests because they depend on the same event stream.

### 2. Prove the contract

- Add coverage for reset/load transcript notifications and async compaction flow.
- Keep the completed lifecycle, factory, executor, and provider-boundary tests green.
- Re-run the impacted `Mcp.Net.WebUi` adapter coverage because it already depends on transcript/activity event behavior.

## Scope

- In scope:
  - add explicit reset/load transcript notifications
  - preserve the completed async compaction contract and `ChatSession` lifecycle, executor, factory, and provider-boundary behavior
- Out of scope:
  - `BashTool`, write/edit tools, or broad shell/process-policy work
  - new consumer-facing runtime APIs beyond the already-landed continue/turn-summary slice
  - concrete built-in/local tools in this slice
  - full MCP tool-call cancellation while `IMcpClient.CallTool(...)` lacks `CancellationToken`
  - transcript persistence redesign
  - changes to the provider request/stream contract beyond the current lifecycle surface

## Current slice

1. Add explicit reset/load transcript notifications.
2. Keep the current lifecycle contract, factory seam, mixed local+MCP routing, and Web UI adapter event behavior stable.

## Next slices

1. Add the first concrete built-in/local tools such as bounded `ReadFileTool` and `ListFilesTool` on top of the clarified consumer runtime surface.
2. Revisit `IMcpClient` ergonomics around `CallTool` cancellation and async disposal once a real caller needs it.
3. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
4. Consider hook/extension or branching surfaces only after the core loop is more robust.
5. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added cancellation-token flow through `SendUserMessageAsync(...)`, provider requests, and tool execution in `ChatSession`.
- Defined deterministic abort behavior for provider waits and tool execution, including partial tool-result persistence for work that finished before cancellation.
- Removed `IToolRegistry` from `ChatSession` and made the session-owned tool catalog the sole validation source inside the loop.
- Added `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor` under `Mcp.Net.Agent.Tools`.
- Added focused executor tests and `ChatSession` regressions covering missing-session-tool failures plus mixed local+MCP turns through one executor graph.
- Added a single-active-turn lifecycle contract to `ChatSession`, including overlap rejection, busy-state lifecycle APIs, and mutation guards while a turn is active.
- Added `IChatSessionFactory`, `ChatSessionFactoryOptions`, and `ChatSessionFactory` so callers can compose sessions from local tools plus an optional caller-owned `IMcpClient`.
- Removed the obsolete agent-definition / manager / registry / store layer from `Mcp.Net.Agent` and the corresponding agent-driven paths from `Mcp.Net.WebUi`.
- Added `ContinueAsync(...)` with explicit transcript-tail validation.
- Added `ChatTurnSummary` return values for awaited turn inspection, including completed/cancelled status.
- Removed `StartSession()` / `SessionStarted` from `ChatSession` and moved session-start notification ownership into the Web UI adapter.
- Guarded `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` dispatch so observer exceptions no longer break otherwise healthy turns.
- Changed `IChatTranscriptCompactor` to `CompactAsync(...)` and updated `ChatSession` to await compaction with cancellation propagation before provider requests.

## Open decisions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project immediately?
- When `IMcpClient` evolves, should the session factory stay consumer-owned only or grow an owning-handle path later?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Keep the completed `ChatSession` lifecycle contract stable while changing transcript lifecycle behavior.
- Verify whole-transcript mutations become visible to subscribers without regressing async compaction or provider-boundary behavior.
- Run broader `Mcp.Net.Tests.Agent` and relevant `Mcp.Net.Tests.WebUi` coverage after the focused pass is green.
