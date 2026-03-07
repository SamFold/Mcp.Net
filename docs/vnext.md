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
- Spec-aligned `notifications/.../list_changed` broadcasts now fire for post-initialize tool, prompt, and resource mutations.
- LLM and WebUI refresh listeners now accept the spec notification names and the full suite is green (`278/278`).
- Latest remaining review finding in the notification/completion/resource-refresh area:
  - prompt/resource/completion handlers lose cancellation and request metadata

## Goal
- Preserve cancellation and request metadata through prompt, resource, and completion handler execution.

## Scope
- In scope:
  - keep `ServerRequestContext.CancellationToken` alive through prompt/resource/completion paths
  - preserve request metadata/session context until the non-tool handlers run
  - add regression coverage for cancellation or metadata loss in one concrete path first
- Out of scope:
  - further notification naming changes
  - SSE vs stdio parity review
  - logging/debuggability cleanup

## Current slice
1. Review the prompt/resource/completion call path and identify the first concrete cancellation or metadata regression to pin.
2. Add the failing regression test first.
3. Implement the smallest fix that preserves request context through that path.
4. Run the targeted tests, then the relevant broader server suite.

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
- Keep the targeted regression failing until the production fix is in place.
- After implementation, run the targeted regression test.
- Run the relevant broader `Mcp.Net.Server` and integration test group.
- Run the full `Mcp.Net.Tests` suite if the fix changes shared request handling behavior.
