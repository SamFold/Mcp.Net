# Roadmap: Mcp.Net.Agent

## Current focus

- Keep the cleaned request/session split stable while `Mcp.Net.Agent` extracts the remaining MCP-specific runtime dependency from `ChatSession`.
- The next immediate value is an `IToolExecutor` seam: it simplifies the core loop, makes abort work cleaner, and opens the door to non-MCP tool execution later.

## What

- Add a runtime `IToolExecutor` abstraction and a small agent-owned `ToolInvocation` model.
- Move MCP-backed tool execution into `McpToolExecutor`.
- Leave tool registry/discovery policy in `ChatSession`; only dispatch moves out.

## Why

- `ChatSession` is already runtime-first for prompt, transcript, and request defaults; direct MCP tool execution is now the main remaining transport-specific coupling.
- Abort plumbing is easier to introduce after tool execution sits behind a cancellable runtime seam.
- The same split is the prerequisite for future local/built-in tools and composite routing without reopening the session loop contract.

## How

- Implement `McpToolExecutor` over `IMcpClient.CallTool(...)` and `ToolResultConverter`.
- Change `ChatSession` to depend on `IToolExecutor` plus `IToolRegistry`.
- Update DI and session-creation callers so each session builds the new executor from its own `IMcpClient`.

## Near-term sequence

1. Extract `IToolExecutor` and remove direct `IMcpClient` tool dispatch from `ChatSession`.
2. Add session-level abort plumbing; full MCP tool-call cancellation can only complete after `IMcpClient.CallTool` accepts a `CancellationToken`.
3. Revisit agent-owned transcript persistence when non-Web UI consumers need durable session state.
4. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
5. Revisit context-window management with a stronger trigger or summarizer path once real conversation pressure justifies it.
6. Revisit parallel execution limits or orchestration controls only if real tool fan-out makes them necessary.

## Recently completed

- `Mcp.Net.Agent` now owns agent definitions, stores, session orchestration, tool inventory, MCP-backed prompt/resource helpers, and tool-result conversion.
- `ChatSession` now builds request snapshots from session-owned prompt, tool, transcript, and execution-default state.
- Shared execution defaults now flow from agent configuration into `ChatRequestOptions`, and `ToolChoice` is available end to end through the cleaned provider seam.
- The Web UI session seam now works through `ChatSession` / adapter operations rather than mutating raw provider client state.
- `ChatSession` now compacts oversized outbound provider transcripts through an agent-owned compaction seam, preserving recent turns and collapsing older context into a deterministic summary entry.
- `ChatSession` now executes independent tool calls concurrently while preserving deterministic transcript ordering for `ToolResult` entries.
- `ChatSession` now uses runtime-owned `ChatSessionConfiguration` / `ChatRequestOptions`, and agent-based session creation is translation glue outside the runtime type.

## Dependencies and risks

- Full session cancellation still depends partly on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- `IToolExecutor` needs to preserve the current split between registry-based policy in `ChatSession` and transport-specific dispatch in the executor so responsibilities do not blur.
- The first executor cut should keep the invocation contract aligned with current provider-emitted tool-call arguments (`IReadOnlyDictionary<string, object?>`) rather than widening it speculatively.
- The current compaction trigger is intentionally simple; future pressure may require token-aware estimation or a stronger summarizer path.
- Session abort work needs to make partial completion behavior explicit so callers do not get ambiguous transcript or activity state.

## Open questions

- Should `Mcp.Net.Agent` grow a built-in/local executor immediately after `IToolExecutor`, or wait until abort plumbing lands?
- Should transcript persistence remain a Web UI concern until another consumer needs it, or should it move into `Mcp.Net.Agent` once compaction lands?
- What should the first abort surface look like for consumers, given that provider cancellation and tool cancellation do not yet have the same capabilities?
- When context-window pressure grows further, should the next compaction improvement be token-aware estimation or provider-backed summarization?
