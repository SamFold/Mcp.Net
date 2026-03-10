# VNext Track Files

Track files under `docs/vnext/` let the repo keep multiple commit-sized "next slices" active at the same time.
Use one file per component, subsystem, or cross-cutting lane when independent work can proceed in parallel.

## When To Create A Track

- Create a track when a component has its own near-term slice that does not need to wait on the current work in another area.
- Reuse the existing track file for that component when the work stays in the same lane.
- Create `cross-cutting.md` only when the work genuinely spans multiple components and no single component should own it.

## Naming

- Prefer short, stable names:
  - `server.md`
  - `llm.md`
  - `client.md`
  - `core.md`
  - `webui.md`
  - `examples.md`
  - `cross-cutting.md`

## Recommended Template

```md
# VNext: <Component>

## Current status
- brief context that matters for the next slice only

## Goal
- one sentence describing the next commit-sized outcome

## Scope
- In scope:
  - concrete work items for this slice
- Out of scope:
  - nearby but intentionally deferred work

## Current slice
1. first concrete step
2. second concrete step
3. verification / close-out step

## Next slices
1. likely follow-up after this slice lands

## Open decisions
- unresolved choice, if any

## Verification checklist
- focused tests to run
- broader tests to run after the focused pass
```

## Coordination

- `docs/vnext.md` is the repo-level index that points at active track files.
- `docs/roadmap.md` is the repo-level roadmap index for medium-term sequencing across all tracks.
- Detailed medium-term planning lives in `docs/roadmap/*.md`.
- Keep each track roughly commit-sized so one agent can own it without mixing unrelated work.
