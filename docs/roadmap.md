# Roadmap (Mcp.Net)

This file is the repo-level entry point for medium-term planning.
Use it to see which project roadmaps are active, how they are sequenced, and where cross-project coordination is needed.

## How To Use This Index

- Keep detailed medium-term planning in project files under `docs/roadmap/`.
- Update the relevant roadmap file or files when priorities, milestones, or major decisions change.
- If work spans multiple projects, update each affected roadmap and call out the dependency, or use `docs/roadmap/cross-cutting.md` when no single project should own the lane.
- Use `docs/vnext.md` and `docs/vnext/*.md` for commit-sized execution slices.

## Current priorities
1. Continue the `Mcp.Net.Client` Streamable HTTP reconnect, retry, and stale-state cleanup review slice
2. Finish the remaining `Mcp.Net.Server` logging/debuggability and hidden-state review
3. Finish the remaining `Mcp.Net.LLM` provider-parity slice, starting with session cancellation now that shared option cleanup and result metadata propagation are in place

## Active Project Roadmaps

- `Mcp.Net.Client`: `docs/roadmap/client.md`
  - Current focus: reconnect, retry, stale-state cleanup, and HTTP `404` session-expiry behavior for Streamable HTTP request and SSE flows.
- `Mcp.Net.Server`: `docs/roadmap/server.md`
  - Current focus: close the remaining logging/debuggability and hidden mutable-state review findings.
- `Mcp.Net.LLM`: `docs/roadmap/llm.md`
  - Current focus: session cancellation first, then tool-registration idempotency and unresolved LLM review follow-ons now that shared option cleanup is in place.
- Cross-cutting: `docs/roadmap/cross-cutting.md`
  - Current focus: repo-wide review closure, spec alignment, and examples/diagnostics work that does not belong to one owning project.

## Current cross-project dependencies

- `Mcp.Net.LLM` session cancellation depends on a `Mcp.Net.Client` cancellation seam for `IMcpClient.CallTool`, so the LLM cancellation slice is not purely local.
- The completed `Mcp.Net.LLM` `Usage` and `StopReason` slice already touched `Mcp.Net.WebUi`; the next option-cleanup slice should stay local unless shared request contracts broaden.
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
- The currently active medium-term lanes are `docs/roadmap/client.md`, `docs/roadmap/server.md`, `docs/roadmap/llm.md`, and `docs/roadmap/cross-cutting.md`.
