# Roadmap (Mcp.Net)

This file is the repo-level entry point for medium-term planning.
Use it to see which project roadmaps are active, how they are sequenced, and where cross-project coordination is needed.

## How To Use This Index

- Keep detailed medium-term planning in project files under `docs/roadmap/`.
- Update the relevant roadmap file or files when priorities, milestones, or major decisions change.
- If work spans multiple projects, update each affected roadmap and call out the dependency, or use `docs/roadmap/cross-cutting.md` when no single project should own the lane.
- Use `docs/vnext.md` and `docs/vnext/*.md` for commit-sized execution slices.

## Current priorities
1. Continue the `Mcp.Net.Agent` orchestration lane with session-level abort plumbing after landing the runtime/configuration cleanup
2. Continue the `Mcp.Net.Client` Streamable HTTP reconnect, retry, and stale-state cleanup review slice
3. Finish the remaining `Mcp.Net.Server` logging/debuggability and hidden-state review

## Active Project Roadmaps

- `Mcp.Net.Agent`: `docs/roadmap/agent.md`
  - Current focus: add abort plumbing next, then revisit persistence and hook/extension surfaces.
- `Mcp.Net.Client`: `docs/roadmap/client.md`
  - Current focus: reconnect, retry, stale-state cleanup, and HTTP `404` session-expiry behavior for Streamable HTTP request and SSE flows.
- `Mcp.Net.Server`: `docs/roadmap/server.md`
  - Current focus: close the remaining logging/debuggability and hidden mutable-state review findings.
- Cross-cutting: `docs/roadmap/cross-cutting.md`
  - Current focus: repo-wide review closure, spec alignment, and examples/diagnostics work that does not belong to one owning project.

## Stable / On-Demand Roadmaps

- `Mcp.Net.LLM`: `docs/roadmap/llm.md`
  - Current status: stable provider boundary with snapshot streaming ratified; reopen only when a new consumer needs another shared provider capability.

## Current cross-project dependencies

- `Mcp.Net.Agent` runtime-surface cleanup, compaction, and any later abort work should preserve the now-stable `Mcp.Net.LLM` request/stream boundary rather than reopening provider-owned conversation state.
- `Mcp.Net.LLM` cancellation still depends on a `Mcp.Net.Client` cancellation seam for `IMcpClient.CallTool`, but that work is deferred until the client contract changes.
- `Mcp.Net.Client` Streamable HTTP reconnect and stale-state work should keep re-running the relevant server-client integration slice so client behavior does not drift from `Mcp.Net.Server`.

## On-Demand Roadmaps

- Create additional files under `docs/roadmap/` when a project needs an independent medium-term lane.
- Recommended names:
  - `core.md`
  - `webui.md`
  - `examples.md`
  - `tests.md`

## Notes

- `docs/roadmap/README.md` describes the roadmap file pattern.
- `docs/vnext.md` is the repo-level index for commit-sized execution slices.
- The currently active medium-term lanes are `docs/roadmap/agent.md`, `docs/roadmap/client.md`, `docs/roadmap/server.md`, and `docs/roadmap/cross-cutting.md`.
