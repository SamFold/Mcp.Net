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
- Completion handlers now receive a read-only request-context snapshot with session, transport, and metadata.
- Prompt and resource handlers now receive the same read-only request-context snapshot with session, transport, and metadata.
- The server now tracks client-advertised capabilities per session during `initialize`, and server-initiated elicitation fails fast when the target session did not negotiate `elicitation`.
- The full suite is green (`292/292`).
- The notification/completion/resource-refresh review items are now closed.
- The `SseServerOptions` DI registration path now preserves routing and security settings from the provided options instance.
- `AddMcpCore(McpServerBuilder)` now preserves builder-configured server identity and instructions in the DI-registered `McpServerOptions`.
- `AddMcpStdioTransport(StdioServerOptions)` now preserves configured stdio and shared server option values during DI registration.
- `AddMcpStdioTransport(McpServerBuilder)` now preserves builder-configured server identity and instructions instead of falling back to defaults.
- The concrete builder/DI default-copy inconsistencies identified in this review pass are now closed.

## Goal
- Continue the `Mcp.Net.Server` review slice for SSE vs stdio parity on server-initiated flows.

## Scope
- In scope:
  - review whether server-initiated requests and notifications behave consistently across SSE and stdio transports
  - start from outbound elicitation now that per-session capability negotiation is enforced
  - identify one concrete transport parity gap
  - pin it with a failing regression first
  - fix one commit-sized parity issue
- Out of scope:
  - logging/debuggability cleanup

## Current slice
1. Compare SSE vs stdio disconnect and cancellation behavior for outbound elicitation requests.
2. Add the failing regression test first for the first verified mismatch.
3. Implement the smallest transport-parity fix.
4. Run the targeted tests, then the relevant broader server suite.

## Next slices
1. Resume the remaining `Mcp.Net.Server` review items:
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
