# Roadmap: Mcp.Net.Agent

## Current focus

- Keep the cleaned request/session split stable while `Mcp.Net.Agent` grows the next orchestration capabilities: parallel tool execution, cancellation plumbing, and eventually persistence/hook surfaces.
- The first context-window management slice is now in place, so the next immediate value is reducing latency for multi-tool turns without changing transcript semantics.

## Near-term sequence

1. Parallelize independent tool execution while preserving deterministic transcript ordering and error handling.
2. Add session-level abort plumbing; full tool-call cancellation can only complete after `IMcpClient.CallTool` accepts a `CancellationToken`.
3. Revisit agent-owned transcript persistence when non-Web UI consumers need durable session state.
4. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
5. Revisit context-window management with a stronger trigger or summarizer path once real conversation pressure justifies it.

## Recently completed

- `Mcp.Net.Agent` now owns agent definitions, stores, session orchestration, tool inventory, MCP-backed prompt/resource helpers, and tool-result conversion.
- `ChatSession` now builds request snapshots from session-owned prompt, tool, transcript, and execution-default state.
- Shared execution defaults now flow from agent configuration into `ChatRequestOptions`, and `ToolChoice` is available end to end through the cleaned provider seam.
- The Web UI session seam now works through `ChatSession` / adapter operations rather than mutating raw provider client state.
- `ChatSession` now compacts oversized outbound provider transcripts through an agent-owned compaction seam, preserving recent turns and collapsing older context into a deterministic summary entry.

## Dependencies and risks

- Full session cancellation still depends partly on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- The current compaction trigger is intentionally simple; future pressure may require token-aware estimation or a stronger summarizer path.
- Parallel tool execution will need to preserve deterministic transcript ordering and failure reporting even when tool completions arrive out of order.

## Open questions

- Should transcript persistence remain a Web UI concern until another consumer needs it, or should it move into `Mcp.Net.Agent` once compaction lands?
- How far should the agent loop go with concurrency before it needs a more explicit hook or orchestration surface?
- When context-window pressure grows further, should the next compaction improvement be token-aware estimation or provider-backed summarization?
