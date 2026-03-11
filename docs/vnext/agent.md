# VNext: Mcp.Net.Agent

## Current status

- `ChatSession` now owns prompt, tool, transcript, and request-default state for the agent loop.
- The provider boundary is clean: `ChatSession` builds `ChatClientRequest` snapshots and `Mcp.Net.LLM` executes them through `IChatClient.SendAsync(...)`.
- `ChatSession` now compacts oversized outbound provider transcripts through an agent-owned compaction seam before building requests, while keeping the in-memory transcript intact.
- Independent tool calls now execute concurrently inside the agent loop, while `ToolResult` transcript entries are still appended in original tool-call order.
- `ChatSession` is now runtime-first: it accepts runtime configuration directly, stores `ChatRequestOptions`, and no longer exposes `AgentDefinition` or agent-based static factory helpers.
- Tool dispatch is still hardcoded inside `ChatSession`: registry lookup happens in the session loop, and execution still goes directly through `IMcpClient.CallTool(...)`.
- Transcript bootstrap exists via `LoadTranscriptAsync(...)`, but persistence remains primarily a Web UI concern rather than an agent-owned seam.

## Goal

- Extract tool execution behind an agent-owned executor seam so the core loop is no longer MCP-specific.

## What

- Introduce an `IToolExecutor` runtime seam that executes a runtime `ToolInvocation` and returns `ToolInvocationResult`.
- Move MCP-specific tool dispatch and result conversion into an `McpToolExecutor`.
- Keep tool discovery and selection in `IToolRegistry` / `ChatSession`; only execution moves behind the new interface.

## Why

- `ChatSession` is now runtime-first except for tool dispatch; `IMcpClient.CallTool(...)` is the last MCP transport dependency inside the core loop.
- A dedicated executor seam is the prerequisite for cleaner abort plumbing, because cancellation can flow through the executor contract even before the MCP client itself supports tokens.
- The same seam unlocks future in-process or built-in tools without changing the loop contract again.
- Tests get simpler because `ChatSession` can mock tool execution directly instead of coordinating `IMcpClient` and MCP result conversion.

## How

- Add a small runtime `ToolInvocation` model in `Mcp.Net.Agent` carrying `ToolCallId`, `ToolName`, and `Arguments` as `IReadOnlyDictionary<string, object?>`.
- Add `IToolExecutor.ExecuteAsync(ToolInvocation invocation, CancellationToken ct)`.
- Implement `McpToolExecutor` as the MCP-backed adapter over `IMcpClient.CallTool(...)` plus `ToolResultConverter`.
- Build `McpToolExecutor` from the session's dedicated `IMcpClient` rather than a root singleton so Web UI sessions keep their per-session MCP connection.
- Change `ChatSession` to keep registry-based "tool exists / enabled" policy, but delegate actual execution to `IToolExecutor`.
- Update DI, Web UI/session creation, and focused tests to use the new seam.

## Scope

- In scope:
  - add an agent-owned `ToolInvocation` and `IToolExecutor`
  - implement MCP-backed tool execution outside `ChatSession`
  - remove `IMcpClient` from the `ChatSession` constructor and runtime loop
  - update DI, Web UI chat creation, and focused regressions for the new seam
- Out of scope:
  - session abort plumbing in the same slice
  - moving transcript persistence out of the Web UI
  - hook/extension or branching systems
  - built-in/local tool libraries or composite executors
  - deeper changes to the provider request/stream boundary

## Current slice

1. Add failing regressions proving `ChatSession` can execute tool calls through a mocked `IToolExecutor` while preserving the current ordering, `ToolCallActivityChanged` transitions, and error semantics.
2. Introduce `ToolInvocation`, `IToolExecutor`, and `McpToolExecutor`, then move MCP tool execution and result conversion out of `ChatSession`.
3. Update DI and Web UI/session creation to build the executor from the session-specific `IMcpClient`, then verify focused `ChatSession` coverage and broader Agent/Web UI chat coverage.

## Next slices

1. Add session-level abort plumbing once tool execution is behind the new runtime seam.
2. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
3. Consider hook/extension or branching surfaces only after the core loop is more robust.
4. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added the first agent-owned context-window seam through transcript compaction before provider request build.
- The initial compactor uses a deterministic entry-count heuristic, preserves whole recent user turns, and collapses older context into a synthetic summary assistant entry.
- Existing session state, replay behavior, and transcript persistence remain unchanged because compaction currently affects outbound provider requests only.
- Parallelized independent tool execution inside `ChatSession` while preserving deterministic `ToolResult` transcript ordering and existing failure semantics.
- Made `ChatSession` runtime-first through `ChatSessionConfiguration`, runtime-owned `ChatRequestOptions`, and agent-to-session translation outside the runtime type.

## Open decisions

- Should local/built-in tools arrive as a separate `LocalToolExecutor` first, or only after the MCP executor seam is in place and stable?
- When session abort plumbing lands, how should partially completed parallel tool calls surface in transcript and activity events?
- Should context compaction stay request-only for a while, or should a later slice persist compacted summaries into session history?
- What should the first abort surface look like for consumers: explicit `AbortCurrentTurn()`, per-call cancellation tokens, or both?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Run focused `Mcp.Net.Tests.Agent` coverage for `ChatSession`.
- Run broader `Mcp.Net.Tests.Agent` and `Mcp.Net.Tests.WebUi.Chat` coverage after the focused pass is green.
- Verify `ChatSession` no longer depends on `IMcpClient` directly and that tool execution semantics remain unchanged.
