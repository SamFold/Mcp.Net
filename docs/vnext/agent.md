# VNext: Mcp.Net.Agent

## Current status

- `ChatSession` now owns prompt, tool, transcript, and request-default state for the agent loop.
- The provider boundary is clean: `ChatSession` builds `ChatClientRequest` snapshots and `Mcp.Net.LLM` executes them through `IChatClient.SendAsync(...)`.
- `ChatSession` now compacts oversized outbound provider transcripts through an agent-owned compaction seam before building requests, while keeping the in-memory transcript intact.
- Independent tool calls now execute concurrently inside the agent loop, while `ToolResult` transcript entries are still appended in original tool-call order.
- `ChatSession` is now runtime-first: it accepts runtime configuration directly, stores `ChatRequestOptions`, and no longer exposes `AgentDefinition` or agent-based static factory helpers.
- Transcript bootstrap exists via `LoadTranscriptAsync(...)`, but persistence remains primarily a Web UI concern rather than an agent-owned seam.

## Goal

- Add session-level abort plumbing so callers can stop an in-flight agent turn even before full MCP tool-call cancellation support exists.

## Scope

- In scope:
  - add a session-owned abort/cancel seam to `ChatSession`
  - let callers interrupt provider waiting and prevent further turn progression
  - preserve current transcript semantics for completed work while defining how aborted turns surface
  - add focused regressions for interrupted provider waits and partially completed turn state
- Out of scope:
  - moving transcript persistence out of the Web UI
  - hook/extension or branching systems
  - full MCP tool-call cancellation until `IMcpClient.CallTool` exposes a `CancellationToken`
  - deeper changes to the provider request/stream boundary

## Current slice

1. Add failing regressions proving an in-flight turn can be aborted while waiting on the provider and that the session does not continue into later loop stages after abort.
2. Introduce session-owned abort plumbing in `ChatSession` without claiming full MCP tool-call cancellation yet.
3. Verify focused `ChatSession` coverage and broader Agent/Web UI chat coverage after the abort path is in place.

## Next slices

1. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
2. Consider hook/extension or branching surfaces only after the core loop is more robust.
3. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added the first agent-owned context-window seam through transcript compaction before provider request build.
- The initial compactor uses a deterministic entry-count heuristic, preserves whole recent user turns, and collapses older context into a synthetic summary assistant entry.
- Existing session state, replay behavior, and transcript persistence remain unchanged because compaction currently affects outbound provider requests only.
- Parallelized independent tool execution inside `ChatSession` while preserving deterministic `ToolResult` transcript ordering and existing failure semantics.
- Made `ChatSession` runtime-first through `ChatSessionConfiguration`, runtime-owned `ChatRequestOptions`, and agent-to-session translation outside the runtime type.

## Open decisions

- When session abort plumbing lands, how should partially completed parallel tool calls surface in transcript and activity events?
- Should context compaction stay request-only for a while, or should a later slice persist compacted summaries into session history?
- What should the first abort surface look like for consumers: explicit `AbortCurrentTurn()`, per-call cancellation tokens, or both?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Run focused `Mcp.Net.Tests.Agent` coverage for `ChatSession`.
- Run broader `Mcp.Net.Tests.Agent` and `Mcp.Net.Tests.WebUi.Chat` coverage after the focused pass is green.
- Verify aborted turns stop progressing cleanly and that completed transcript entries remain internally consistent.
