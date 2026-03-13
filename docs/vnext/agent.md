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

- Revisit `IMcpClient` ergonomics around tool-call cancellation and async disposal now that the local file/process tool stack is in place.

## What

- Revisit the `IMcpClient` seam so callers can eventually get cleaner tool-call cancellation and ownership/disposal behavior.

## Why

- The runtime and factory seams are now in place and the obsolete model layer is gone.
- Continue/resume, awaited turn summaries, guarded event dispatch, async compaction, and transcript lifecycle notifications are now in place.
- The first built-in read-only filesystem tools now include bounded file discovery on top of the public local-tool authoring seam.
- `Mcp.Net.Examples.LLMConsole` now exercises `ChatSession` in both MCP and non-MCP modes, including optional built-in local filesystem tools.
- Abort now appends synthetic cancelled tool results for unfinished parallel tool calls, so `ContinueAsync(...)` can resume from an aborted mixed-result turn with a structurally complete transcript tail.
- The OpenAI provider path now matches the SDK's streaming tool-call assembly model, and the temporary tool-round guard has been removed so normal coding-agent exploration is no longer artificially capped.
- `ReadFileTool` now returns mutation-oriented metadata including `contentHash`, encoding/BOM, and newline-style information.
- `WriteFileTool` now enables bounded or unbounded whole-file creation with explicit overwrite intent on top of the redesigned filesystem scope seam.
- `EditFileTool` now enables bounded edits to existing text files, and `run_shell_command` now enables bounded host CLI execution for real coding-agent flows.
- `Mcp.Net.WebUi` is a legacy adapter layer and should not influence `Mcp.Net.Agent` design decisions; if needed, Web UI can be rebuilt around the runtime that the library actually wants.

## How

### 1. Revisit `IMcpClient`

- Decide how the client seam should expose cancellation for remote tool calls without reopening provider-owned conversation state.
- Decide whether async disposal or ownership helpers belong on the session factory side, the client seam itself, or only on concrete host composition paths.

### 2. Keep the mutation and process seams coherent

- Keep `ReadFileTool`, `EditFileTool`, `GrepTool`, `GlobTool`, `ListFilesTool`, `RunShellCommandTool`, and the `LLMConsole` sample green as regression coverage while adding the next bounded write primitive.
- Avoid widening the process surface into persistent sessions or PTY work while the bounded file-mutation seam is still being validated.

## Scope

- In scope:
  - revisit `IMcpClient` ergonomics around tool-call cancellation and async disposal
  - preserve the completed `ChatSession` lifecycle, executor, factory, provider-boundary, and bounded process-tool behavior
- Out of scope:
  - persistent shell sessions, PTY support, background jobs, or arbitrary process-session management
  - a public structured `run_process` tool surface until a real host needs it
  - broader fuzzy edit heuristics beyond the landed newline-normalized `EditFileTool` matching
  - new consumer-facing runtime APIs beyond the already-landed continue/turn-summary slice
  - transcript persistence redesign
  - changes to the provider request/stream contract beyond the current lifecycle surface
  - preserving legacy Web UI composition or DI shapes when they conflict with the cleaner runtime design

## Current slice

1. Revisit `IMcpClient` ergonomics around tool-call cancellation and async disposal.
2. Keep the current tool, lifecycle, executor, and provider-boundary contracts stable.

## Next slices

1. Revisit `IMcpClient` ergonomics around tool-call cancellation and async disposal.
2. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
3. Consider hook/extension or branching surfaces only after the core loop is more robust.
4. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added cancellation-token flow through `SendUserMessageAsync(...)`, provider requests, and tool execution in `ChatSession`.
- Defined deterministic abort behavior for provider waits and tool execution, including partial tool-result persistence for work that finished before cancellation.
- Removed `IToolRegistry` from `ChatSession` and made the session-owned tool catalog the sole validation source inside the loop.
- Added `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor` under `Mcp.Net.Agent.Tools`.
- Added public local-tool result helpers through `ToolInvocation` and `ToolInvocationResults` so local tools no longer need to call the raw `ToolInvocationResult` constructor directly.
- Removed `ToolRegistry` from `AddChatRuntimeServices()` and made it an explicit `AddToolRegistry()` opt-in.
- Added typed local-tool argument binding through `ToolInvocation.BindArguments<TArgs>()` and `LocalToolBase<TArgs>`, including schema generation for nullable primitive arguments.
- Added `FileSystemToolPolicy`, `ReadFileTool`, and `ListFilesTool` as the first bounded built-in local filesystem tools, including containment, truncation, and missing-path coverage.
- Redesigned `FileSystemToolPolicy` so the built-in file tools can run either bounded to the configured base path or unbounded from that same base path while keeping traversal defaults anchored to the base path.
- Extended `ReadFileTool` metadata with `contentHash`, encoding/BOM, and newline-style information so later mutation tools can use optimistic concurrency and preserve file shape.
- Added `WriteFileTool` as the bounded-or-unbounded whole-file creation primitive, including explicit overwrite intent, parent-directory creation, encoded byte limits, encoding/BOM preservation on overwrite, and atomic sibling-temp commits.
- Added `GlobTool` with compiled segment matching, literal-prefix search-root narrowing, deterministic bounded traversal, and policy-owned skip/depth/result limits on top of the same filesystem seam.
- Added `EditFileTool` as the first bounded filesystem mutation primitive for existing text files, including optimistic concurrency, one-snapshot batch planning, newline-normalized fallback matching, and atomic temp-file-plus-replace commits.
- Added `GrepTool` as a bounded local content-search tool backed by ripgrep when available on the host, including root-relative deterministic output, policy-owned truncation limits, and conditional sample registration when `rg` is discoverable.
- Added `ProcessToolPolicy` plus `RunShellCommandTool` as the first bounded local process-execution seam, including deterministic host-shell resolution, root-bounded working-directory overrides, timeout/process-tree-kill behavior, and bounded head+tail output capture.
- Updated `Mcp.Net.Examples.LLMConsole` so non-MCP mode now runs through `ChatSession` and can optionally enable the built-in local filesystem tools.
- Added synthetic cancelled tool results for unfinished parallel tool calls after abort so `ContinueAsync(...)` sees a structurally complete transcript alongside any real results that already finished.
- Removed the temporary max tool-round guard and its configuration surface so normal coding-agent exploration is not artificially capped by a low per-turn round limit.
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
- Keep the completed `ChatSession` lifecycle contract stable while revisiting the next client/runtime seam.
- Keep the landed local file/process tool stack stable while revisiting `IMcpClient`.
- Verify containment rules, overwrite semantics, truncation behavior, and executor/runtime integration without regressing provider-boundary behavior or the landed process-tool seam.
- Run broader `Mcp.Net.Tests.Agent` coverage after the focused pass is green.
