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
- The obsolete `AgentDefinition` / manager / registry / store stack has been removed; `Mcp.Net.Agent` is now centered on runtime/session concerns only.
- `ChatSession` now supports `ContinueAsync(...)` with explicit transcript-tail validation for resumed turns.
- `SendUserMessageAsync(...)` and `ContinueAsync(...)` now return `ChatTurnSummary`, including per-turn transcript additions/updates plus completed/cancelled status.
- `ChatSession` no longer exposes `StartSession()` / `SessionStarted`; session-start notification now lives in the Web UI adapter that actually broadcasts it.
- `ChatSession` now guards `TranscriptChanged`, `ActivityChanged`, and `ToolCallActivityChanged` per subscriber so observer exceptions are logged and swallowed instead of faulting turns.
- Transcript compaction now uses `CompactAsync(...)`, and `ChatSession` awaits it with turn-cancellation propagation before provider requests.
- Transcript lifecycle events now cover `Reset` and `Loaded`, including whole-transcript snapshots for reset/load operations.

## Goal

- Validate the agent tool surface with typed local-tool argument binding and the first bounded built-in/local filesystem tools.

## What

- Add a shared read-only filesystem policy for bounded path resolution, traversal limits, and truncation.
- Add the remaining public local-tool authoring seam needed for cleaner tool implementations:
  - a typed `LocalToolBase<TArgs>` or equivalent typed argument-binding path for local tools
- Ship `ReadFileTool` and `ListFilesTool` on top of the existing local-tool/executor/runtime seams and the now-public result helpers.

## Why

- The runtime and factory seams are now in place and the obsolete model layer is gone.
- Continue/resume, awaited turn summaries, guarded event dispatch, async compaction, and transcript lifecycle notifications are now in place.
- The obvious consumer friction around result creation and disconnected DI registration is now removed.
- The next meaningful validation step is to prove the runtime with real tools that consumers can use immediately.
- The first built-in tools should build on the public authoring seam that now exists instead of adding one-off argument parsing.
- `Mcp.Net.WebUi` is a legacy adapter layer and should not influence `Mcp.Net.Agent` design decisions; if needed, Web UI can be rebuilt around the runtime that the library actually wants.

## How

### 1. Add shared filesystem policy

- Add a shared `FileSystemToolPolicy` or equivalent bounded policy object.
- Canonicalize paths against a configured root and reject traversal outside the allowed scope.
- Define clear limits for file bytes, line counts, and directory entry counts so tool outputs stay bounded.

### 2. Add typed local-tool binding

- Add a typed local-tool base such as `LocalToolBase<TArgs>` that binds invocation arguments to a POCO once, rather than forcing every tool to parse `IReadOnlyDictionary<string, object?>` manually.
- Use the typed path together with the already-public result helpers so the built-in tools prove the intended authoring model.

### 3. Add the first concrete tools

- Add `ReadFileTool` with partial-read/truncation metadata.
- Add `ListFilesTool` with deterministic ordering and bounded output.
- Keep the tools read-only; do not mix in shell/process/write behavior.

### 4. Prove the contract

- Add focused tool tests for containment checks, truncation, typed argument binding, and error cases.
- Add executor/session coverage as needed to prove the new tools work through the existing runtime surface.
- Keep the completed lifecycle, factory, executor, and provider-boundary tests green.

## Scope

- In scope:
  - add a bounded filesystem policy for built-in local tools
  - add typed local-tool argument binding
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

1. Add the shared bounded filesystem policy.
2. Add typed `LocalToolBase<TArgs>` or equivalent argument binding.
3. Add read-only `ReadFileTool` and `ListFilesTool`.
4. Keep the current runtime, executor, and provider-boundary contracts stable.

## Next slices

1. Add loop-safety guards before any write/shell tools:
   - max iteration / max tool-round guard for `RunTurnLoopAsync`
   - synthesize cancelled tool results for unfinished tool calls so abort-and-continue leaves a structurally valid transcript
2. Add `GlobTool` or equivalent bounded file-discovery support once the first read-only tools and authoring seams prove the surface.
3. Revisit `IMcpClient` ergonomics around `CallTool` cancellation and async disposal once a real caller needs it.
4. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
5. Consider hook/extension or branching surfaces only after the core loop is more robust.
6. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added cancellation-token flow through `SendUserMessageAsync(...)`, provider requests, and tool execution in `ChatSession`.
- Defined deterministic abort behavior for provider waits and tool execution, including partial tool-result persistence for work that finished before cancellation.
- Removed `IToolRegistry` from `ChatSession` and made the session-owned tool catalog the sole validation source inside the loop.
- Added `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor` under `Mcp.Net.Agent.Tools`.
- Added public local-tool result helpers through `ToolInvocation` and `ToolInvocationResults` so local tools no longer need to call the raw `ToolInvocationResult` constructor directly.
- Removed `ToolRegistry` from `AddChatRuntimeServices()` and made it an explicit `AddToolRegistry()` opt-in.
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
- Should the public tool-authoring surface be minimal helper methods on `ToolInvocation`, or should the library go straight to a typed `LocalToolBase<TArgs>` and use that as the primary pattern for built-in tools?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Keep the completed `ChatSession` lifecycle contract stable while adding typed binding and built-in read-only tools.
- Verify containment rules, truncation behavior, typed argument binding, and executor/runtime integration without regressing provider-boundary behavior.
- Run broader `Mcp.Net.Tests.Agent` coverage after the focused pass is green.
