# VNext (Mcp.Net)

This file is the repo-level entry point for commit-sized planning.
Use it to see which component or system tracks are active and where each next slice lives.

## How To Use This Index

- Keep detailed next-slice planning in per-component files under `docs/vnext/`.
- Update the relevant track file or files before substantial implementation when the planned slice changes.
- Update the same track file or files again after completing a slice so they point at the next commit-sized step for that area.
- Keep each track narrow enough that one agent can execute it as a coherent vertical slice.
- Use `docs/roadmap.md` and the detailed project roadmaps under `docs/roadmap/` for the broader sequence and cross-track prioritization.

## Active Tracks

- `Mcp.Net.Agent`: `docs/vnext/agent.md`
  - Current slice: add the shared bounded filesystem policy plus the first read-only built-in filesystem tools.
- `Mcp.Net.WebUi`: `docs/vnext/webui.md`
  - Current slice: decide whether Web UI should compose sessions through `IChatSessionFactory` instead of constructing `ChatSession` inline.
- `Mcp.Net.Server`: `docs/vnext/server.md`
  - Current slice: continue the logging/debuggability and hidden mutable state review.
- `Mcp.Net.Client`: `docs/vnext/client.md`
  - Current slice: review reconnect, retry, and stale-state cleanup for Streamable HTTP request and SSE flows.

## On-Demand Tracks

- `Mcp.Net.LLM`: `docs/vnext/llm.md`
  - Current status: stable provider boundary; snapshot streaming is ratified, and further work is on-demand rather than an active execution lane.
- Create additional track files under `docs/vnext/` when a component needs an independent next slice.
- Recommended names:
  - `core.md`
  - `client.md`
  - `webui.md`
  - `examples.md`
  - `cross-cutting.md`

## Coordination Rules

- Prefer one owning track per commit-sized slice.
- If a slice spans multiple components, update each affected track and call out the dependency between them.
- Keep this index concise. Detailed context, risks, and verification notes belong in the track files.
