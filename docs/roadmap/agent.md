# Roadmap: Mcp.Net.Agent

## Current focus

- Add a bounded `WriteFileTool` now that the first local process-execution seam has landed.
- Keep the bounded local filesystem and process surfaces coherent now that `ReadFileTool` metadata, `GrepTool`, `GlobTool`, `EditFileTool`, and `run_shell_command` are in place.
- Preserve the now-completed continue/resume, per-turn summary, guarded event-dispatch, async compaction, and transcript lifecycle-notification surfaces while tool coverage expands.

## What

- Add `WriteFileTool` as the bounded whole-file creation and overwrite primitive for paths inside `FileSystemToolPolicy.RootPath`.
- Keep `run_shell_command` as the bounded local process tool for host CLI workflows, with host-shell resolution, root-bounded working directories, timeout caps, output truncation, and concurrency limits.
- Later, replace the entry-count-only compaction trigger with token-aware context budgeting that can target provider max-context limits and reserve output budget explicitly.

## Why

- The runtime and factory seams are now in place and the dead model layer is gone.
- Continue/resume, per-turn summaries, transcript lifecycle notifications, and the dead session-start seam are now in place.
- The next highest-value gap is not another runtime seam; it is proving the library with concrete tools that real consumers can use.
- The first built-in tools should establish a public authoring pattern for outside consumers instead of relying on internal-only helpers.
- `Mcp.Net.WebUi` is an older adapter layer and should not drive `Mcp.Net.Agent` design; the runtime can move first and Web UI can be rebuilt around it if necessary.
- Bounded filesystem discovery, content search, and surgical mutation tools are now in place.
- `ReadFileTool` now exposes mutation-oriented metadata, `GlobTool` enables deterministic candidate discovery, `GrepTool` enables bounded content search, and `EditFileTool` enables bounded edits to existing text files.
- The first bounded process seam is now in place for real CLI workflows (`git`, `dotnet`, `npm`, `cargo`).
- Agents still cannot create new files or intentionally overwrite whole files without a bounded write primitive, so that is now the next highest-value gap.
- The current entry-count compactor is a good MVP, but it does not track real context-window pressure or leave deliberate room for model output.

## How

### Next bounded mutation seam

- Add `WriteFileTool` for bounded text creation and explicit overwrite inside `FileSystemToolPolicy.RootPath`.
- Keep the landed `ReadFileTool` / `EditFileTool` / `GrepTool` / `GlobTool` / `ListFilesTool` / `RunShellCommandTool` surface stable while validating the next file-mutation step.
- Preserve the snapshot-based provider boundary and avoid inventing provider-specific escape hatches while the local-tool surface grows.

### Verification

- Add focused tool coverage for containment checks, truncation, typed argument binding, and error paths.
- Add executor/session coverage as needed to prove the tools flow through the current runtime seams.
- Keep the completed `ChatSession` lifecycle tests and broader agent/runtime coverage green.

## Near-term sequence

1. Add `WriteFileTool` for bounded new-file creation and explicit overwrite.
2. Revisit `IMcpClient` ergonomics when a real caller needs `CallTool` cancellation or async disposal.
3. Revisit session-owned transcript persistence when non-Web UI consumers need durable session state.
4. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
5. Revisit context-window management with token-aware compaction driven by provider context limits, reserved output budget, and a stronger summarizer path once real conversation pressure justifies it.

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
- `ReadFileTool` now returns mutation-oriented metadata including `contentHash`, encoding/BOM, and newline-style information so later mutation tools can use optimistic concurrency and preserve file shape.
- `Mcp.Net.Agent` now includes `GlobTool` with compiled segment matching, literal-prefix search-root narrowing, deterministic bounded traversal, and policy-owned skip/depth/result limits on top of the same bounded filesystem seam.
- `Mcp.Net.Agent` now includes `EditFileTool` as the first bounded filesystem mutation primitive for existing text files, including optimistic concurrency, one-snapshot batch planning, newline-normalized fallback matching, and atomic temp-file-plus-replace commits.
- `Mcp.Net.Agent` now includes `GrepTool` as a bounded local content-search tool backed by ripgrep when the host can provide it, including deterministic root-relative output and policy-owned match/output limits.
- `Mcp.Net.Agent` now includes `ProcessToolPolicy` plus `RunShellCommandTool` as the first bounded local process-execution seam, including deterministic shell resolution, root-bounded working-directory overrides, timeout/process-tree-kill behavior, and bounded head+tail output capture.
- `Mcp.Net.Examples.LLMConsole` now uses `ChatSession` in non-MCP mode and can optionally enable the built-in local filesystem tools in direct or mixed MCP sessions.
- `ChatSession` now appends synthetic cancelled tool results for unfinished parallel calls after abort, preserving completed results so `ContinueAsync(...)` sees a structurally valid transcript tail.
- `ChatSession` no longer enforces the temporary max tool-round guard or exposes a `MaxToolCallRounds` configuration knob, so normal coding-agent exploration is not capped by an artificially small round budget.
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
- The first local tools still need disciplined scope when they land. If they expand into shell/write behavior too early, the slice will mix seam validation with policy decisions.
- Legacy Web UI composition and registry usage should adapt to the runtime; they are not reasons to keep weaker or older `Mcp.Net.Agent` seams alive.
- The current compaction trigger is intentionally simple; it does not account for provider context-window limits or reserved output budget, so future pressure should move the runtime toward token-aware compaction and possibly stronger summarization.

## Open questions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project as soon as the contracts land?
- Should local tools always be app-owned reusable registrations, or should the runtime explicitly support session-owned local tool instances from the start?
- Should the library commit to `LocalToolBase<TArgs>` as the primary public authoring pattern, or keep only low-level helpers and let consumers build their own typed wrappers?
- When context-window pressure grows further, how should token-aware compaction get provider context-window and output-budget information without reopening provider-owned session state?
- Should stronger summarization land in the same slice as token-aware budgeting, or only after budget-based trimming proves insufficient?
