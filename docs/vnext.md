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
- LLM and WebUI refresh listeners now accept the spec notification names.
- `HandleRequestAsync` now preserves request cancellation through resource, prompt, and completion execution.
- True non-tool request cancellation now stays cancellation instead of being normalized to `InternalError`, and SSE/stdio ingress no longer treat canceled requests as server faults.
- The full suite is green (`284/284`).
- Latest remaining review finding in the notification/completion/resource-refresh area:
  - prompt/resource/completion handlers still do not expose request metadata/session context at the final handler boundary

## Goal
- Expose request metadata/session context to non-tool handlers now that cancellation flow and semantics are both correct.

## Scope
- In scope:
  - define the smallest safe surface for prompt/resource/completion handlers to access request metadata
  - keep `HandleRequestAsync` as the authoritative context-aware request path
  - add regression coverage for one concrete metadata/session-context path first
- Out of scope:
  - further notification naming changes
  - SSE vs stdio parity review
  - logging/debuggability cleanup

## Current slice
1. Review the remaining non-tool handler surfaces and choose the smallest metadata/session-context seam.
2. Add the failing regression test first.
3. Implement the smallest fix that exposes request metadata/context through that path.
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
