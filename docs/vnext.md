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
- Outbound elicitation disconnect behavior is now covered end-to-end for both SSE and stdio; both transports cancel the pending server-side request promptly when the disconnect is simulated correctly.
- SSE and stdio client transports now convert remote EOF / remote shutdown into a real close event, and pending client requests now fail promptly with cancellation semantics instead of hanging or surfacing a false timeout.
- SSE and stdio server transports now serialize outbound writes per connection, so overlapping responses, requests, and notifications cannot enter the shared writer concurrently.
- `initialize` now suppresses the unimplemented `logging` capability even if callers set `ServerCapabilities.Logging`, keeping capability advertisement truthful.
- Transport errors now converge on the close path, so fatal send failures no longer leave a broken session registered with negotiated protocol state still present.
- Hosted SSE connections now follow a single authoritative registration path through `McpServer.ConnectAsync`, so the same transport is no longer registered twice on initial connect.
- `ConnectAsync` now rolls back startup-failed transports, so a `StartAsync` failure no longer leaves a registered-but-dead transport in the connection manager.
- The full suite is green (`304/304`).
- The notification/completion/resource-refresh review items are now closed.
- The `SseServerOptions` DI registration path now preserves routing and security settings from the provided options instance.
- `AddMcpCore(McpServerBuilder)` now preserves builder-configured server identity and instructions in the DI-registered `McpServerOptions`.
- `AddMcpStdioTransport(StdioServerOptions)` now preserves configured stdio and shared server option values during DI registration.
- `AddMcpStdioTransport(McpServerBuilder)` now preserves builder-configured server identity and instructions instead of falling back to defaults.
- The concrete builder/DI default-copy inconsistencies identified in this review pass are now closed.

## Goal
- Continue the `Mcp.Net.Server` review slice with logging/debuggability and hidden mutable state review.

## Scope
- In scope:
  - review remaining logging/debuggability gaps after the capability-truthfulness fix
  - identify one concrete issue involving hidden mutable state or weak observability
  - pin it with a failing regression first when feasible
  - keep the change commit-sized
- Out of scope:
  - full MCP logging primitive implementation
  - broad non-review refactors

## Current slice
1. Continue the logging/debuggability and hidden mutable state review in the server stack.
2. Identify the next concrete correctness or observability gap.
3. Add a failing regression first where practical.
4. Implement the smallest fix and rerun targeted then broader tests.

## Next slices
1. Resume the remaining `Mcp.Net.Server` review items:
   - decide whether to implement the MCP logging primitive after the review closes

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
