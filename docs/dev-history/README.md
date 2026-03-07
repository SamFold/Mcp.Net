# Dev History

One file per commit, stored here for quick lookup.

## File naming
Use:

`YYYY-MM-DD_short-description.md`

Example:
`2026-03-07_builder-auth-origin-consistency.md`

Keep the description short and kebab-case.

## Required contents
- Change summary
- Why
- Major files changed
- Verification notes

## How to add a new entry
1. Create the dev-history file with the commit date plus short description.
2. Add the required contents above.
3. Commit the dev-history file in the same commit as the related change.

## Suggested structure

```md
# 2026-03-07 - Short change title

## Change summary
- Brief summary of what changed

## Why
- Brief rationale for the slice

## Major files changed
- `path/to/file`

## Verification notes
- Commands run, tests passed, or note that the change was docs-only
```
