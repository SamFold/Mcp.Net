# VNext: Mcp.Net.Client

## Current status

- The current `Mcp.Net.Client` review has identified a transport-level spec gap in the HTTP client path.
- `SseClientTransport` now completes client requests from successful POST response bodies when the server replies with either `application/json` or a POST-scoped `text/event-stream`.
- `SseClientTransport` no longer requires a GET SSE stream or a pre-initialize session header in order to send the initial `initialize` request, so POST-only Streamable HTTP startup is now possible.
- The client remains compatible with the current in-repo server shape because the background GET SSE listener is still available, but that listener is still too central to the request-response model relative to the 2025-11-25 Streamable HTTP rules.
- Existing client and integration coverage now proves the transport can consume spec-compliant POST responses in both supported response modes, while preserving the current session and negotiated-protocol header behavior.

## Goal

- Make the HTTP client transport correctly handle 2025-11-25 Streamable HTTP request responses so a spec-compliant `Mcp.Net.Client` can interoperate with conforming MCP servers beyond the current in-repo server shape.

## Scope

- In scope:
  - add failing regressions proving HTTP POST request handling is currently incompatible with spec-compliant `application/json` and POST-scoped `text/event-stream` responses
  - implement the smallest transport change that lets client requests complete from either a single JSON HTTP response or an SSE stream returned by that same POST
  - preserve current session-id and negotiated-protocol header handling on subsequent requests
  - keep server-initiated requests and notifications working without regressing the current multi-session request-routing guarantees
- Out of scope:
  - resumable streams, `Last-Event-ID`, retry-driven polling, and redelivery semantics
  - explicit HTTP `DELETE` session termination support
  - full deprecated HTTP+SSE fallback for older 2024-11-05 servers
  - broader auth, reconnect, builder, or DI cleanup unless required by the focused transport fix

## Current slice

1. Add regressions proving the optional GET SSE stream is not the normal response path for new in-flight client requests once POST response handling is available.
2. Tighten `SseClientTransport` so POST-initiated requests prefer their own HTTP response bodies and the GET stream remains for server-initiated traffic unless resuming a previous stream.
3. Preserve compatibility with the current in-repo server shape while keeping the change narrow and avoiding reconnect, retry, or deprecated 2024-11-05 fallback work in the same slice.

## Next slices

1. Review reconnect, retry, and stale-state cleanup for HTTP streams, including session-expiry handling after HTTP 404 and protocol-correct cancellation behavior.
2. Add backwards-compatibility fallback for deprecated 2024-11-05 HTTP+SSE servers if cross-version interoperability remains a product requirement.
3. Revisit whether the 2025-11-25 behavior should remain an in-place evolution of `SseClientTransport` or become a distinct transport once compatibility requirements are clearer.

## Open decisions

- Should the 2025-11-25 transport alignment land as an in-place evolution of `SseClientTransport`, or should the client introduce a distinct `StreamableHttpClientTransport` and reserve the current behavior strictly for deprecated HTTP+SSE compatibility?

## Verification checklist

- Add failing regressions before implementation when feasible.
- Run focused `Mcp.Net.Tests.Client` coverage for the HTTP transport path.
- Run the relevant server-client integration slice after the focused transport tests pass.
- Verify the multi-session routing coverage still passes for concurrent SSE/HTTP clients.
