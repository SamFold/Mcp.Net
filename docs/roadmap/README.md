# Roadmap Files

Roadmap files under `docs/roadmap/` hold medium-term planning for one project or cross-cutting lane.
Use them for work that is broader than one commit-sized `vnext` slice but still specific enough to have an owning area.

## When To Create A Roadmap

- Create a project roadmap when one project has multiple upcoming milestones that should not be collapsed into the repo-level `docs/roadmap.md`.
- Reuse the existing roadmap file for that project while the work stays in the same lane.
- Create `cross-cutting.md` when the work genuinely spans multiple projects and no single project should own it.

## Naming

- Prefer short, stable names:
  - `client.md`
  - `server.md`
  - `llm.md`
  - `core.md`
  - `webui.md`
  - `examples.md`
  - `tests.md`
  - `cross-cutting.md`

## Recommended Template

```md
# Roadmap: <Project>

## Current focus
- the active medium-term lane for this project

## Near-term sequence
1. the next few milestones in order

## Recently completed
- only the completed work that still matters for planning context

## Dependencies and risks
- cross-project dependencies, sequencing constraints, or notable risks

## Open questions
- unresolved decisions, if any
```

## Coordination

- `docs/roadmap.md` is the repo-level index for active roadmap files and cross-project priorities.
- `docs/vnext.md` and `docs/vnext/*.md` hold the commit-sized next slices that execute against these broader roadmaps.
- Keep roadmap files concise; detailed design notes belong in dedicated docs under `docs/`.
