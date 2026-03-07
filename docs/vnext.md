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
- The full suite is green (`289/289`).
- The notification/completion/resource-refresh review items are now closed.
- The `SseServerOptions` DI registration path now preserves routing and security settings from the provided options instance.
- `AddMcpCore(McpServerBuilder)` now preserves builder-configured server identity and instructions in the DI-registered `McpServerOptions`.

## Goal
- Start the next remaining `Mcp.Net.Server` review slice: builder/DI inconsistencies.

## Scope
- In scope:
  - review remaining builder and DI registration paths for constructibility and consistency
  - identify the smallest concrete inconsistency to pin with a regression test
  - fix one commit-sized builder/DI issue with tests first
- Out of scope:
  - SSE vs stdio parity review
  - logging/debuggability cleanup

## Current slice
1. Pin the next builder/DI inconsistency: `AddMcpStdioTransport(...)` still uses the old partial/default option copy path.
2. Add the failing regression test first.
3. Preserve stdio transport option values instead of resetting them to defaults during DI registration.
4. Run the targeted tests, then the relevant broader server suite.

## Next slices
1. Resume the remaining `Mcp.Net.Server` review items:
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
