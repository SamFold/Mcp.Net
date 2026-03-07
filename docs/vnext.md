# VNext (Mcp.Net)

This document tracks the current active slice of work.
Keep it focused on the next commit-sized change, not the whole backlog.

## Current status
- We are in the second `Mcp.Net.Server` review pass.
- Session-routing fixes are already in place:
  - shared SSE connection manager
  - session-scoped pending client request routing
  - reconnect-safe replacement transports
  - session-scoped negotiated protocol version
  - session-bound elicitation/DI cleanup
  - multi-session SSE routing isolation coverage
  - SSE POST session-owner enforcement
  - reliable transport close cleanup on shutdown errors
- Hosted SSE builder path and health-path wiring now honor configured values and are covered by regression tests.
- Hosted SSE requests now reuse middleware-authenticated request state instead of running the auth handler twice.
- Next open review area is notification/completion/resource refresh routing.

## Goal
- Review and harden server-side routing for notifications, completions, and resource refresh flows so session isolation and transport behavior stay correct under concurrency.

## Scope
- In scope:
  - notification routing
  - completion request/response routing
  - prompt/resource refresh notifications
  - regression coverage where session-scoped behavior crosses component boundaries
- Out of scope:
  - SSE vs stdio parity review
  - logging/debuggability cleanup

## Current slice
1. Review notification, completion, and resource-refresh routing for session/transport correctness.
2. Add regression coverage for any routing bug found in that area.
3. Fix the smallest verified issue and run the targeted and broader relevant test scopes.

## Next slices
1. Resume the remaining `Mcp.Net.Server` review items:
   - remaining builder/DI inconsistencies
   - SSE vs stdio parity
   - logging/debuggability and hidden mutable state

## Open decisions
- Should `SseServerBuilder` delegate all endpoint mapping to `UseMcpServer(options => ...)`, or own an explicit hosting path with the same behavior contract?

## Quality gates
- Test first for non-trivial behavior changes.
- A bug or inconsistency fix needs regression coverage before implementation is considered complete.
- Targeted tests must pass before broadening to a wider test scope.

## Verification checklist
- Run the new targeted regression test.
- Run the relevant broader `Mcp.Net.Server` or transport/auth test group.
- If endpoint mapping or auth pipeline behavior changes materially, run the full `Mcp.Net.Tests` suite before finishing.
