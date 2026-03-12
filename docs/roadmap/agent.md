# Roadmap: Mcp.Net.Agent

## Current focus

- Harden the `ChatSession` turn loop before adding broader filesystem, write, or shell tools.
- Keep the first read-only filesystem tools stable while abort/continue transcript correctness and runaway-loop guardrails land.
- Preserve the now-completed continue/resume, per-turn summary, guarded event-dispatch, async compaction, and transcript lifecycle-notification surfaces while loop safety improves.

## What

- Synthesize cancelled placeholder results for unfinished parallel tool calls after abort so the transcript remains structurally valid for `ContinueAsync(...)`.
- Decide whether a separate non-tool iteration cap is still needed beyond the landed max tool-round guard.
- Later, replace the entry-count-only compaction trigger with token-aware context budgeting that can target provider max-context limits and reserve output budget explicitly.

## Why

- The runtime and factory seams are now in place and the dead model layer is gone.
- Continue/resume, per-turn summaries, transcript lifecycle notifications, and the dead session-start seam are now in place.
- The next highest-value gap is not another runtime seam; it is proving the library with concrete tools that real consumers can use.
- The first built-in tools should establish a public authoring pattern for outside consumers instead of relying on internal-only helpers.
- `Mcp.Net.WebUi` is an older adapter layer and should not drive `Mcp.Net.Agent` design; the runtime can move first and Web UI can be rebuilt around it if necessary.
- Bounded read-only filesystem tools are the narrowest useful validation slice before broader search, write, or shell behavior.
- The current entry-count compactor is a good MVP, but it does not track real context-window pressure or leave deliberate room for model output.

## How

### First built-in tools

- Add a shared filesystem policy object and centralized path canonicalization/containment checks.
- Add public result helpers and typed local-tool argument binding, then build the first tools on those seams.
- Add `ReadFileTool` with bounded reads plus explicit truncation metadata.
- Add `ListFilesTool` with deterministic ordering and bounded entry counts.

### Verification

- Add focused tool coverage for containment checks, truncation, typed argument binding, and error paths.
- Add executor/session coverage as needed to prove the tools flow through the current runtime seams.
- Keep the completed `ChatSession` lifecycle tests and broader agent/runtime coverage green.

## Near-term sequence

1. Add loop-safety guards before any write/shell tools:
   - synthetic cancelled tool results for unfinished parallel tool calls so abort-and-continue leaves a structurally valid transcript
   - decide whether a separate non-tool iteration cap is needed beyond the landed max tool-round guard
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
- Local tools can now create results through public `ToolInvocation` / `ToolInvocationResults` helpers instead of relying on the raw `ToolInvocationResult` constructor.
- Local tools can now bind invocation arguments through `ToolInvocation.BindArguments<TArgs>()` or derive from `LocalToolBase<TArgs>` for typed authoring plus generated input schema from a transport-neutral local-tool generator.
- `AddChatRuntimeServices()` no longer registers the disconnected tool-registry seam; `ToolRegistry` is now explicit opt-in through `AddToolRegistry()`.
- `Mcp.Net.Agent` now includes `FileSystemToolPolicy`, `ReadFileTool`, and `ListFilesTool` as the first bounded built-in local filesystem tools, including containment, truncation, and missing-path coverage.
- `Mcp.Net.Examples.LLMConsole` now uses `ChatSession` in non-MCP mode and can optionally enable the built-in local filesystem tools in direct or mixed MCP sessions.
- `ChatSession` now enforces a max tool-round guard so runaway tool loops stop with a session error instead of executing indefinitely.
- `Mcp.Net.LLM.OpenAI` now reconstructs streamed tool-call arguments by streaming update index and raw bytes, matching the OpenAI SDK streaming function-calling model.
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
- Abort can still leave unmatched tool calls in the transcript after partial parallel completion; that safety fix should land before any write/shell tool expansion.
- The first local tools still need disciplined scope when they land. If they expand into shell/write behavior too early, the slice will mix seam validation with policy decisions.
- Legacy Web UI composition and registry usage should adapt to the runtime; they are not reasons to keep weaker or older `Mcp.Net.Agent` seams alive.
- The current compaction trigger is intentionally simple; it does not account for provider context-window limits or reserved output budget, so future pressure should move the runtime toward token-aware compaction and possibly stronger summarization.

## Open questions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project as soon as the contracts land?
- Should local tools always be app-owned reusable registrations, or should the runtime explicitly support session-owned local tool instances from the start?
- Should the library commit to `LocalToolBase<TArgs>` as the primary public authoring pattern, or keep only low-level helpers and let consumers build their own typed wrappers?
- When context-window pressure grows further, how should token-aware compaction get provider context-window and output-budget information without reopening provider-owned session state?
- Should stronger summarization land in the same slice as token-aware budgeting, or only after budget-based trimming proves insufficient?
