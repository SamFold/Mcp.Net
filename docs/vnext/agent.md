# VNext: Mcp.Net.Agent

## Current status

- `ChatSession` now owns prompt, tool, transcript, and request-default state for the agent loop.
- The provider boundary is clean: `ChatSession` builds `ChatClientRequest` snapshots and `Mcp.Net.LLM` executes them through `IChatClient.SendAsync(...)`.
- `ChatSession` now compacts oversized outbound provider transcripts through an agent-owned compaction seam before building requests, while keeping the in-memory transcript intact.
- Tool calls still execute sequentially inside the agent loop.
- Transcript bootstrap exists via `LoadTranscriptAsync(...)`, but persistence remains primarily a Web UI concern rather than an agent-owned seam.

## Goal

- Reduce multi-tool turn latency by parallelizing independent tool execution without changing transcript ordering or failure semantics.

## Scope

- In scope:
  - execute independent tool calls concurrently inside `ChatSession`
  - keep transcript append order deterministic even if tool calls finish out of order
  - preserve current tool activity events and failure/reporting behavior
  - add focused regressions for latency-independent ordering and error semantics
- Out of scope:
  - moving transcript persistence out of the Web UI
  - hook/extension or branching systems
  - session abort / cancellation plumbing
  - deeper changes to the provider request/stream boundary

## Current slice

1. Add failing regressions proving multi-tool turns still append transcript entries in a stable order even when tool execution runs concurrently.
2. Parallelize `ChatSession` tool execution while keeping transcript append order deterministic and preserving activity/error events.
3. Verify focused `ChatSession` coverage and broader Agent/Web UI chat coverage after the concurrency change.

## Next slices

1. Add session-level abort plumbing, even if full tool-call cancellation remains blocked on `IMcpClient.CallTool`.
2. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
3. Consider hook/extension or branching surfaces only after the core loop is more robust.

## Recently completed

- Added the first agent-owned context-window seam through transcript compaction before provider request build.
- The initial compactor uses a deterministic entry-count heuristic, preserves whole recent user turns, and collapses older context into a synthetic summary assistant entry.
- Existing session state, replay behavior, and transcript persistence remain unchanged because compaction currently affects outbound provider requests only.

## Open decisions

- When session abort plumbing lands, how should partially completed parallel tool calls surface in transcript and activity events?
- Should context compaction stay request-only for a while, or should a later slice persist compacted summaries into session history?
- Should parallel tool execution wait for all results and append them in original tool-call order, or should transcript ordering later become completion-order with a separate deterministic presentation layer?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Run focused `Mcp.Net.Tests.Agent` coverage for `ChatSession`.
- Run broader `Mcp.Net.Tests.Agent` and `Mcp.Net.Tests.WebUi.Chat` coverage after the focused pass is green.
- Verify transcript entry order, tool activity reporting, and failure semantics remain stable under concurrent tool execution.
