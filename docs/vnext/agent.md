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

## Goal

- Close the remaining observer and transcript-lifecycle hygiene gaps before shipping the first concrete built-in/local tools.

## What

- Guard event dispatch so subscriber exceptions cannot break otherwise healthy turns.
- Change transcript compaction to `CompactAsync(...)` before more consumers depend on the synchronous contract.
- Add explicit reset/load transcript notifications so observer-visible transcript state stays coherent.

## Why

- The runtime and factory seams are now in place and the obsolete model layer is gone.
- Continue/resume and awaited turn summaries are now in place, so the next highest-value issues are correctness and lifecycle hygiene.
- Today a throwing event subscriber can still fault a turn, and whole-transcript mutations are still invisible to observers.
- This is the last narrow runtime cleanup before the first bounded built-in tools.

## How

### 1. Harden event dispatch

- Wrap `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` dispatch in log-and-swallow guards.
- Keep synchronous ordering for now; do not add async dispatch complexity until a real consumer needs it.
- Preserve the current activity/transcript ordering that Web UI and tests already consume.

### 2. Make transcript lifecycle observable

- Add reset/load transcript notifications via new change kinds or an equivalent explicit event shape.
- Keep transcript mutation semantics stable while making whole-state changes visible to observers.
- Re-run the Web UI adapter/event tests because they depend on the same event stream.

### 3. Break the compactor contract once, early

- Change `IChatTranscriptCompactor.Compact(...)` to `CompactAsync(...)`.
- Flow the async compactor through request building without weakening the current provider-boundary snapshot model.
- Keep the default entry-count compactor behavior the same while changing the contract.

### 4. Prove the contract

- Add focused regressions proving observer exceptions are logged and swallowed instead of faulting turns.
- Add coverage for reset/load transcript notifications and async compaction flow.
- Keep the completed lifecycle, factory, executor, and provider-boundary tests green.
- Re-run the impacted `Mcp.Net.WebUi` adapter coverage because it already depends on transcript/activity event behavior.

## Scope

- In scope:
  - guard runtime event dispatch against observer faults
  - add explicit reset/load transcript notifications
  - change transcript compaction to an async contract
  - preserve the completed `ChatSession` lifecycle, executor, factory, and provider-boundary behavior
- Out of scope:
  - `BashTool`, write/edit tools, or broad shell/process-policy work
  - new consumer-facing runtime APIs beyond the already-landed continue/turn-summary slice
  - concrete built-in/local tools in this slice
  - full MCP tool-call cancellation while `IMcpClient.CallTool(...)` lacks `CancellationToken`
  - transcript persistence redesign
  - changes to the provider request/stream contract beyond the current lifecycle surface

## Current slice

1. Guard runtime event dispatch so observer exceptions are logged and swallowed instead of breaking turns.
2. Change transcript compaction to `CompactAsync(...)`.
3. Add explicit reset/load transcript notifications.
4. Keep the current lifecycle contract, factory seam, mixed local+MCP routing, and Web UI adapter event behavior stable.

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

## Open decisions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project immediately?
- When `IMcpClient` evolves, should the session factory stay consumer-owned only or grow an owning-handle path later?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Keep the completed `ChatSession` lifecycle contract stable while hardening events and transcript lifecycle behavior.
- Verify observer bugs no longer fault turns and whole-transcript mutations become visible to subscribers.
- Run broader `Mcp.Net.Tests.Agent` and relevant `Mcp.Net.Tests.WebUi` coverage after the focused pass is green.
