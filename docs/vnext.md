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
- Latest review findings in the notification/completion/resource-refresh area:
  - server-side tool/prompt/resource mutations do not notify connected clients
  - prompt/resource/completion handlers lose cancellation and request metadata
  - current refresh listeners use non-spec notification names
- A targeted failing regression now exists for the missing server-side `list_changed` notification path.
- Next slice is to implement the minimal notification path and make that regression pass.

## Goal
- Add spec-aligned `notifications/.../list_changed` broadcasts for tool, prompt, and resource mutations so connected clients refresh without reconnecting.

## Scope
- In scope:
  - server-side tool/prompt/resource mutation notifications
  - spec-aligned `notifications/tools|prompts|resources/list_changed` naming
  - integration/regression coverage for post-initialize refresh behavior
- Out of scope:
  - prompt/resource/completion cancellation plumbing
  - metadata propagation into non-tool handlers
  - SSE vs stdio parity review
  - logging/debuggability cleanup

## Current slice
1. Implement the minimal server-side notification path for active sessions.
2. Update listeners/tests to the spec `notifications/.../list_changed` names if needed to make the end-to-end refresh path work.
3. Run the targeted refresh-routing tests, then the relevant broader suite.

## Next slices
1. Resume the remaining `Mcp.Net.Server` review items:
   - preserve cancellation and metadata through prompt/resource/completion handlers
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
- Keep the targeted regression failing until the production fix is in place.
- After implementation, run the targeted regression test.
- Run the relevant broader `Mcp.Net.Server`, integration, or client refresh test group.
- If notification naming or routing changes affect multiple layers, run the full `Mcp.Net.Tests` suite before finishing.
