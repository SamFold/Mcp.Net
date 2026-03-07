# 2026-03-07 - Docs workflow and archive cleanup

## Change summary
- Replaced `AGENTS.md` with a workflow-first version based on the existing multi-repo pattern, adapted for `Mcp.Net`, .NET, and C#.
- Added `docs/vnext.md`, `docs/roadmap.md`, and `docs/testing.md` as the planning and testing source-of-truth files.
- Added `docs/dev-history/README.md` to define the per-commit dev-history format.
- Moved root-level working notes and review plans into `docs/archive/`.
- Updated `.gitignore` so `AGENTS.md` and `docs/**/*.md` are tracked instead of being swallowed by the existing `*.MD` rule.

## Why
- Standardize this repo on the same planning, docs, and commit hygiene used in sibling repos.
- Make TDD, testability, and commit-sized planning explicit in the repo instructions.
- Reduce root-level markdown clutter and keep working docs under `docs/`.

## Major files changed
- `.gitignore`
- `AGENTS.md`
- `docs/vnext.md`
- `docs/roadmap.md`
- `docs/testing.md`
- `docs/dev-history/README.md`
- `docs/archive/*`

## Verification notes
- Docs-only change; no build or test execution required.
- Verified the intended root markdown files were moved into `docs/archive/`.
- Verified `AGENTS.md` and `docs/**/*.md` are no longer ignored by git.
