# Roadmap: Mcp.Net.Client

## Current focus

- Finish the current Streamable HTTP review lane with reconnect, retry, cancellation, and stale-state cleanup for request and SSE flows.
- Keep the 2025-11-25 HTTP behavior aligned with the spec without regressing current in-repo server compatibility or multi-session routing.

## Near-term sequence

1. Add regressions around broken or closed Streamable HTTP SSE flows so reconnect and stale-state behavior is pinned before implementation.
2. Review HTTP stream retry and cancellation behavior, especially where optional GET SSE, POST-scoped SSE, and timed-out requests can leave stale deferred state behind.
3. Pin the expected client behavior when a sessioned request returns HTTP `404`, including the boundary between transport reset and protocol re-initialization.
4. Add backwards-compatibility fallback for deprecated 2024-11-05 HTTP+SSE servers only if cross-version interoperability remains a product requirement.
5. Revisit whether the 2025-11-25 behavior should remain an in-place evolution of `SseClientTransport` or become a distinct `StreamableHttpClientTransport` once compatibility requirements are clearer.
6. Review negotiated protocol-version defaults and headers so the client advertises the intended Streamable HTTP support level after transport behavior stabilizes.

## Recently completed

- `SseClientTransport` now completes client requests from successful POST response bodies when the server replies with either `application/json` or a POST-scoped `text/event-stream`.
- `SseClientTransport` no longer requires a GET SSE stream or a pre-initialize session header in order to send the initial `initialize` request, so POST-only Streamable HTTP startup is now possible.
- Fresh POST requests no longer complete from the optional GET SSE stream once the client can determine that the request is bound to a POST response path, while legacy no-body POST flows still fall back to GET for current server compatibility.

## Dependencies and risks

- `Mcp.Net.LLM` session-level cancellation will require a client seam that can pass a `CancellationToken` through `IMcpClient.CallTool`.
- Transport changes in this lane should keep re-running the relevant server-client integration coverage so request routing and server-initiated traffic do not drift from `Mcp.Net.Server`.
- Optional GET SSE fallback, POST-scoped SSE, and retry behavior still create the main risk of stale deferred request state.

## Open questions

- Should the 2025-11-25 transport alignment remain an in-place evolution of `SseClientTransport`, or should the client introduce a distinct `StreamableHttpClientTransport` and reserve the current behavior strictly for deprecated HTTP+SSE compatibility?
