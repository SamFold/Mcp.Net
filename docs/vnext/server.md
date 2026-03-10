# VNext: Mcp.Net.Server

## Current status

- We are in the second `Mcp.Net.Server` review pass.
- Session-routing, reconnect, readiness, transport-close, and builder/DI consistency fixes from the current review pass are already in place.
- Hosted SSE and stdio hosting paths now preserve configured values and use the intended registration and lifecycle paths.
- Request-context, cancellation, capability-negotiation, and `list_changed` notification fixes are in place and covered.
- The concrete SSE vs stdio parity gaps identified so far in this review pass are closed.

## Goal

- Continue the remaining `Mcp.Net.Server` review slice with focus on logging/debuggability and hidden mutable state.

## Scope

- In scope:
  - review the remaining server-side logging/debuggability gaps after the recent lifecycle fixes
  - identify one concrete hidden mutable state or weak observability issue
  - pin the issue with a failing regression before implementation when feasible
  - keep the next change commit-sized
- Out of scope:
  - revisiting the now-closed builder/DI default-copy fixes unless a new regression appears
  - broad non-review refactors
  - unrelated `Mcp.Net.LLM`, `Mcp.Net.Client`, or `Mcp.Net.WebUi` work

## Current slice

1. Resume the remaining `Mcp.Net.Server` review with focus on logging/debuggability and hidden mutable state.
2. Identify the next concrete server-side issue in that area and pin it with a failing regression first when feasible.
3. Keep the next change commit-sized and verify it in the focused server slice before broadening.

## Next slices

1. Implement the next concrete `Mcp.Net.Server` review fix once it is pinned by regression coverage.
2. Close the remaining server review items and cleanup once the current pass is exhausted.

## Open decisions

- Should `SseServerBuilder` delegate all endpoint mapping to `UseMcpServer(options => ...)`, or own an explicit hosting path with the same behavior contract?

## Verification checklist

- Pin the issue with a failing focused regression before implementation when feasible.
- Run the focused `Mcp.Net.Server` regression or targeted test group for the affected behavior.
- Run the next broader relevant server or integration slice after the focused test passes.
