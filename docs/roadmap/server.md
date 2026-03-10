# Roadmap: Mcp.Net.Server

## Current focus

- Continue the remaining `Mcp.Net.Server` review slice with focus on logging/debuggability and hidden mutable state.
- Keep the next server changes review-driven, regression-backed, and commit-sized rather than reopening broader hosting refactors.

## Near-term sequence

1. Resume the remaining server review with focus on logging/debuggability and hidden mutable state.
2. Identify the next concrete issue in that area and pin it with a failing regression before implementation when feasible.
3. Land the smallest focused fix and verify it in the targeted server slice before broadening.
4. Close the remaining server review items and cleanup once the current pass is exhausted.
5. Feed broader spec-alignment or diagnostics work back into the cross-cutting roadmap once the current review lane is closed.

## Recently completed

- Lifecycle and hidden-state fixes are in place for transport replacement and teardown, including startup rollback, replacement-startup ordering, negotiated-state reset, pending client-request cleanup, and transport-error cleanup through the normal close path.
- Lifecycle readiness and capability correctness are tighter: `list_changed` now waits for `notifications/initialized`, per-session elicitation capability negotiation is enforced, and `initialize` no longer advertises unimplemented `logging`.
- Hosting-path consistency improved across SSE and stdio: hosted SSE now uses one registration path, authenticated request state is reused, configured endpoints are honored, builder and DI option copies preserve configured values, remote closes propagate, and outbound writes serialize per connection.
- Request-context and cancellation behavior now preserve request metadata and cancellation tokens through prompt, resource, and completion handlers, including true cancellation semantics for non-tool requests.

## Dependencies and risks

- `Mcp.Net.Client` Streamable HTTP changes should continue to re-run the relevant integration slice so server and client transport behavior do not diverge.
- The remaining work in this lane is review-driven, so the next concrete milestone depends on what the next hidden-state or observability issue turns out to be.

## Stability themes

- Keep all session-scoped state isolated by connection and session.
- Prefer one authoritative routing path per transport.
- Add regression coverage for every production bug we fix.
- Keep auth, origin validation, and teardown behavior consistent across hosting paths.
- Keep refresh and `list_changed` behavior spec-aligned so connected clients do not drift stale.
- Preserve request cancellation and request metadata until the final handler boundary.
- Preserve client capability negotiation per session before issuing server-initiated client-feature requests.
- Serialize outbound writes per transport so responses, requests, and notifications cannot corrupt one another on a shared connection.
- Keep capability advertisement truthful so `initialize` does not promise primitives the server does not implement.
