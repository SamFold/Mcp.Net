# Roadmap: Mcp.Net.Agent

## Current focus

- Keep the cleaned request/session split stable while `Mcp.Net.Agent` grows the next orchestration capabilities: cancellation plumbing, persistence, and eventually hook/extension surfaces.
- The first context-window management and parallel tool-execution slices are now in place, so the next immediate value is giving callers a real way to interrupt an in-flight turn.

## Near-term sequence

1. Add session-level abort plumbing; full tool-call cancellation can only complete after `IMcpClient.CallTool` accepts a `CancellationToken`.
2. Revisit agent-owned transcript persistence when non-Web UI consumers need durable session state.
3. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
4. Revisit context-window management with a stronger trigger or summarizer path once real conversation pressure justifies it.
5. Revisit parallel execution limits or orchestration controls only if real tool fan-out makes them necessary.

## Recently completed

- `Mcp.Net.Agent` now owns agent definitions, stores, session orchestration, tool inventory, MCP-backed prompt/resource helpers, and tool-result conversion.
- `ChatSession` now builds request snapshots from session-owned prompt, tool, transcript, and execution-default state.
- Shared execution defaults now flow from agent configuration into `ChatRequestOptions`, and `ToolChoice` is available end to end through the cleaned provider seam.
- The Web UI session seam now works through `ChatSession` / adapter operations rather than mutating raw provider client state.
- `ChatSession` now compacts oversized outbound provider transcripts through an agent-owned compaction seam, preserving recent turns and collapsing older context into a deterministic summary entry.
- `ChatSession` now executes independent tool calls concurrently while preserving deterministic transcript ordering for `ToolResult` entries.

## Dependencies and risks

- Full session cancellation still depends partly on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- The current compaction trigger is intentionally simple; future pressure may require token-aware estimation or a stronger summarizer path.
- Session abort work needs to make partial completion behavior explicit so callers do not get ambiguous transcript or activity state.

## Open questions

- Should transcript persistence remain a Web UI concern until another consumer needs it, or should it move into `Mcp.Net.Agent` once compaction lands?
- What should the first abort surface look like for consumers, given that provider cancellation and tool cancellation do not yet have the same capabilities?
- When context-window pressure grows further, should the next compaction improvement be token-aware estimation or provider-backed summarization?
