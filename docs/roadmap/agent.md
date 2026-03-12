# Roadmap: Mcp.Net.Agent

## Current focus

- Validate the library with the first bounded built-in/local tools now that the core runtime hygiene slice is complete.
- Keep the `ChatSession` lifecycle and factory seams stable while the first read-only filesystem tools land.
- Preserve the now-completed continue/resume, per-turn summary, guarded event-dispatch, async compaction, and transcript lifecycle-notification surfaces while the tool layer starts to grow.

## What

- Add a shared bounded filesystem policy for root containment, output limits, and truncation behavior.
- Ship read-only `ReadFileTool` and `ListFilesTool` on top of the existing local-tool/runtime seams.
- Later, replace the entry-count-only compaction trigger with token-aware context budgeting that can target provider max-context limits and reserve output budget explicitly.

## Why

- The runtime and factory seams are now in place and the dead model layer is gone.
- Continue/resume, per-turn summaries, transcript lifecycle notifications, and the dead session-start seam are now in place.
- The next highest-value gap is not another runtime seam; it is proving the library with concrete tools that real consumers can use.
- Bounded read-only filesystem tools are the narrowest useful validation slice before broader search, write, or shell behavior.
- The current entry-count compactor is a good MVP, but it does not track real context-window pressure or leave deliberate room for model output.

## How

### First built-in tools

- Add a shared filesystem policy object and centralized path canonicalization/containment checks.
- Add `ReadFileTool` with bounded reads plus explicit truncation metadata.
- Add `ListFilesTool` with deterministic ordering and bounded entry counts.

### Verification

- Add focused tool coverage for containment checks, truncation, and error paths.
- Add executor/session coverage as needed to prove the tools flow through the current runtime seams.
- Keep the completed `ChatSession` lifecycle tests and broader agent/runtime coverage green.

## Near-term sequence

1. Add the shared bounded filesystem policy plus read-only `ReadFileTool` and `ListFilesTool`.
2. Add `GlobTool` or equivalent bounded file discovery once the first read-only tools prove the surface.
3. Revisit `IMcpClient` ergonomics when a real caller needs `CallTool` cancellation or async disposal.
4. Revisit session-owned transcript persistence when non-Web UI consumers need durable session state.
5. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
6. Revisit context-window management with token-aware compaction driven by provider context limits, reserved output budget, and a stronger summarizer path once real conversation pressure justifies it.

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
- `ChatSession` now guards runtime event dispatch so observer exceptions are logged and swallowed instead of breaking turns.
- `IChatTranscriptCompactor` now uses `CompactAsync(...)`, and `ChatSession` awaits compaction with cancellation propagation before provider requests.
- `ChatSession` now raises `Reset` and `Loaded` transcript notifications with whole-transcript snapshots for reset/load operations.

## Dependencies and risks

- Full MCP tool-call cancellation still depends on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- The provider boundary should remain snapshot-based; the runtime should not reintroduce provider-owned conversation state.
- The first local tools still need disciplined scope when they land. If they expand into shell/write behavior too early, the slice will mix seam validation with policy decisions.
- The current compaction trigger is intentionally simple; it does not account for provider context-window limits or reserved output budget, so future pressure should move the runtime toward token-aware compaction and possibly stronger summarization.

## Open questions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project as soon as the contracts land?
- Should local tools always be app-owned reusable registrations, or should the runtime explicitly support session-owned local tool instances from the start?
- When context-window pressure grows further, how should token-aware compaction get provider context-window and output-budget information without reopening provider-owned session state?
- Should stronger summarization land in the same slice as token-aware budgeting, or only after budget-based trimming proves insufficient?
