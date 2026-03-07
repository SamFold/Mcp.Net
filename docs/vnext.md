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
- Next open review area is hosted SSE builder/auth/origin consistency.

## Goal
- Make the hosted SSE builder path honor its configured endpoint, auth, and origin settings through one coherent server pipeline.

## Scope
- In scope:
  - hosted SSE builder option handling
  - auth/origin consistency in the SSE hosted path
  - regression coverage for the above
- Out of scope:
  - notification/completion/resource refresh routing
  - SSE vs stdio parity review
  - logging/debuggability cleanup

## Current slice
1. Add a regression test proving hosted SSE builder configuration honors a non-default MCP path and corresponding origin/CORS behavior.
2. Fix the hosted builder/middleware wiring so the configured path and options are the actual runtime path.
3. Verify the targeted test, then the next broader relevant server test scope.

## Next slices
1. Collapse duplicate auth handling so the hosted SSE path uses one consistent auth contract.
2. Resume the remaining `Mcp.Net.Server` review items:
   - notification/completion/resource refresh routing
   - remaining builder/DI inconsistencies
   - SSE vs stdio parity
   - logging/debuggability and hidden mutable state

## Open decisions
- Should `SseTransportHost` perform any direct authentication, or should auth/context setup belong entirely to middleware?
- Should `SseServerBuilder` delegate all endpoint mapping to `UseMcpServer(options => ...)`, or own an explicit hosting path with the same behavior contract?

## Quality gates
- Test first for non-trivial behavior changes.
- A bug or inconsistency fix needs regression coverage before implementation is considered complete.
- Targeted tests must pass before broadening to a wider test scope.

## Verification checklist
- Run the new targeted regression test.
- Run the relevant broader `Mcp.Net.Server` or transport/auth test group.
- If endpoint mapping or auth pipeline behavior changes materially, run the full `Mcp.Net.Tests` suite before finishing.
