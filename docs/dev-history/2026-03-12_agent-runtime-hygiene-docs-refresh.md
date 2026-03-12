# 2026-03-12 - Agent runtime-hygiene docs refresh

## Change summary
- Updated the agent planning docs to mark guarded event dispatch as completed.
- Advanced the active `Mcp.Net.Agent` slice to async transcript compaction plus reset/load transcript notifications.
- Aligned the repo-level `docs/vnext.md` and `docs/roadmap.md` indexes with the updated agent sequencing.

## Why
- The planning docs had fallen behind the committed runtime work and were still describing event-dispatch hardening as pending.
- The next implementation slice should now point at the remaining transcript lifecycle and compaction hygiene work before built-in tools.

## Major files changed
- `docs/vnext/agent.md`
- `docs/vnext.md`
- `docs/roadmap/agent.md`
- `docs/roadmap.md`
- `docs/dev-history/2026-03-12_agent-runtime-hygiene-docs-refresh.md`

## Verification notes
- Reviewed the staged doc diffs to confirm the active slice and near-term sequence now start at `CompactAsync(...)` plus reset/load transcript notifications.
- No code or tests changed in this slice.
