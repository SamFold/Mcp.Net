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
- `ToolInvocation` now exposes public result helpers, and `ToolInvocationResults` provides a shared public construction path for local tool authors and runtime error paths.
- `AddChatRuntimeServices()` now registers only the shared runtime surface, while `ToolRegistry` moved to an explicit `AddToolRegistry()` opt-in.
- `ToolInvocation` now supports typed argument binding, and `LocalToolBase<TArgs>` provides a reusable typed local-tool authoring path with generated input schema from a transport-neutral local-tool generator rather than MCP discovery attributes.
- The obsolete `AgentDefinition` / manager / registry / store stack has been removed; `Mcp.Net.Agent` is now centered on runtime/session concerns only.
- `ChatSession` now supports `ContinueAsync(...)` with explicit transcript-tail validation for resumed turns.
- `SendUserMessageAsync(...)` and `ContinueAsync(...)` now return `ChatTurnSummary`, including per-turn transcript additions/updates plus completed/cancelled status.
- `ChatSession` no longer exposes `StartSession()` / `SessionStarted`; session-start notification now lives in the Web UI adapter that actually broadcasts it.
- `ChatSession` now guards `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` per subscriber so observer exceptions are logged and swallowed instead of faulting turns.
- Transcript compaction now uses `CompactAsync(...)`, and `ChatSession` awaits it with turn-cancellation propagation before provider requests.
- Transcript lifecycle events now cover `Reset` and `Loaded`, including whole-transcript snapshots for reset/load operations.

## Goal

- Finish the remaining loop-safety correctness work before expanding beyond the first read-only filesystem tools.

## What

- Synthesize cancelled placeholder results for unfinished parallel tool calls after abort so the transcript remains structurally valid for `ContinueAsync(...)`.
- Decide whether `RunTurnLoopAsync` still needs a separate non-tool iteration cap beyond the now-landed max tool-round guard.

## Why

- The runtime and factory seams are now in place and the obsolete model layer is gone.
- Continue/resume, awaited turn summaries, guarded event dispatch, async compaction, and transcript lifecycle notifications are now in place.
- The first built-in read-only filesystem tools are now in place on top of the public local-tool authoring seam.
- `Mcp.Net.Examples.LLMConsole` now exercises `ChatSession` in both MCP and non-MCP modes, including optional built-in local filesystem tools.
- The max tool-round guard is now in place, but abort can still leave unfinished parallel tool calls without transcript results.
- The OpenAI provider path now matches the SDK's streaming tool-call assembly model, so the next correctness gap is back in the runtime loop rather than provider-specific parsing.
- `Mcp.Net.WebUi` is a legacy adapter layer and should not influence `Mcp.Net.Agent` design decisions; if needed, Web UI can be rebuilt around the runtime that the library actually wants.

## How

### 1. Finish abort transcript safety

- Detect unfinished parallel local-tool calls after an abort.
- Append synthetic cancelled tool results for any calls that never produced a transcript result.
- Preserve the existing completed-result behavior for tool work that did finish before cancellation.

### 2. Re-check the turn-loop guard shape

- Keep the landed max tool-round guard as the first runaway protection.
- Decide whether a separate non-tool iteration cap is still needed once abort transcript correctness is finished.

### 3. Keep the tool surface stable

- Do not change the public local-tool authoring seam while finishing loop safety.
- Keep `ReadFileTool`, `ListFilesTool`, and the `LLMConsole` sample green as regression coverage.

## Scope

- In scope:
  - add a bounded filesystem policy for built-in local tools
  - add read-only `ReadFileTool` and `ListFilesTool`
  - preserve the completed `ChatSession` lifecycle, executor, factory, and provider-boundary behavior
- Out of scope:
  - further DI cleanup beyond the already-landed `AddToolRegistry()` split
  - `GlobTool`
  - write/edit tools, shell/process tools, or broad shell/process-policy work
  - new consumer-facing runtime APIs beyond the already-landed continue/turn-summary slice
  - full MCP tool-call cancellation while `IMcpClient.CallTool(...)` lacks `CancellationToken`
  - transcript persistence redesign
  - changes to the provider request/stream contract beyond the current lifecycle surface
  - preserving legacy Web UI composition or DI shapes when they conflict with the cleaner runtime design

## Current slice

1. Synthesize cancelled results for aborted unfinished tool calls.
2. Decide whether to add a separate non-tool iteration cap on top of the landed max tool-round guard.
3. Keep the current tool, lifecycle, executor, and provider-boundary contracts stable.

## Next slices

1. Add `GlobTool` or equivalent bounded file-discovery support once the first read-only tools and loop-safety fixes prove the surface.
2. Revisit `IMcpClient` ergonomics around `CallTool` cancellation and async disposal once a real caller needs it.
3. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
4. Consider hook/extension or branching surfaces only after the core loop is more robust.
5. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added cancellation-token flow through `SendUserMessageAsync(...)`, provider requests, and tool execution in `ChatSession`.
- Defined deterministic abort behavior for provider waits and tool execution, including partial tool-result persistence for work that finished before cancellation.
- Removed `IToolRegistry` from `ChatSession` and made the session-owned tool catalog the sole validation source inside the loop.
- Added `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor` under `Mcp.Net.Agent.Tools`.
- Added public local-tool result helpers through `ToolInvocation` and `ToolInvocationResults` so local tools no longer need to call the raw `ToolInvocationResult` constructor directly.
- Removed `ToolRegistry` from `AddChatRuntimeServices()` and made it an explicit `AddToolRegistry()` opt-in.
- Added typed local-tool argument binding through `ToolInvocation.BindArguments<TArgs>()` and `LocalToolBase<TArgs>`, including schema generation for nullable primitive arguments.
- Added `FileSystemToolPolicy`, `ReadFileTool`, and `ListFilesTool` as the first bounded built-in local filesystem tools, including containment, truncation, and missing-path coverage.
- Updated `Mcp.Net.Examples.LLMConsole` so non-MCP mode now runs through `ChatSession` and can optionally enable the built-in local filesystem tools.
- Added a max tool-round guard to `RunTurnLoopAsync(...)` so runaway tool loops now stop with a session-visible error entry instead of continuing indefinitely.
- Fixed the OpenAI provider path to assemble streamed tool-call arguments by `StreamingChatToolCallUpdate.Index` and raw argument bytes, matching the SDK's streaming function-calling model.
- Added focused executor tests and `ChatSession` regressions covering missing-session-tool failures plus mixed local+MCP turns through one executor graph.
- Added a single-active-turn lifecycle contract to `ChatSession`, including overlap rejection, busy-state lifecycle APIs, and mutation guards while a turn is active.
- Added `IChatSessionFactory`, `ChatSessionFactoryOptions`, and `ChatSessionFactory` so callers can compose sessions from local tools plus an optional caller-owned `IMcpClient`.
- Removed the obsolete agent-definition / manager / registry / store layer from `Mcp.Net.Agent` and the corresponding agent-driven paths from `Mcp.Net.WebUi`.
- Added `ContinueAsync(...)` with explicit transcript-tail validation.
- Added `ChatTurnSummary` return values for awaited turn inspection, including completed/cancelled status.
- Removed `StartSession()` / `SessionStarted` from `ChatSession` and moved session-start notification ownership into the Web UI adapter.
- Guarded `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` dispatch so observer exceptions no longer break otherwise healthy turns.
- Changed `IChatTranscriptCompactor` to `CompactAsync(...)` and updated `ChatSession` to await compaction with cancellation propagation before provider requests.
- Added `Reset`/`Loaded` transcript notifications with whole-transcript snapshots so reset/load operations are observable to consumers.

## Open decisions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project immediately?
- When `IMcpClient` evolves, should the session factory stay consumer-owned only or grow an owning-handle path later?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Keep the completed `ChatSession` lifecycle contract stable while adding built-in read-only tools.
- Verify containment rules, truncation behavior, and executor/runtime integration without regressing provider-boundary behavior.
- Run broader `Mcp.Net.Tests.Agent` coverage after the focused pass is green.
