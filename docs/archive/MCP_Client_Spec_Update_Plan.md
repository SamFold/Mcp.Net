# MCP Client Spec Alignment Plan

This roadmap brings `Mcp.Net.Client` into compliance with MCP specification revision **2025‑06‑18** and the OAuth-focused server updates. Each issue is scoped for a focused pull request accompanied by unit tests and clean, single-responsibility code.

## Issue 1: Initialization & Capability Negotiation
- Findings: `McpClient` hardcodes protocol `2024-11-05`, omits new capability flags, and never captures the negotiated version or instructions. The client therefore operates out of spec the moment it initializes.
- Work: Updated the initialize payload to advertise `2025-06-18`, include the expanded client capabilities (`roots`, `sampling`, `elicitation`, `_meta` support), and capture the negotiated protocol version plus instructions from the server response. Persisted the negotiated version for transport reuse and exposed it via the client API.
- Tests: Added `McpClientInitializationTests` covering initialize payload serialization, downgrade/unsupported-version handling, default title behaviour, and response parsing (including instructions/capabilities propagation).
- Notes: Exposed negotiated metadata on `IMcpClient` so higher layers can honour protocol headers; builder now accepts an optional display title which defaults to the client name.

## Issue 2: Streamable HTTP Transport Alignment ✅
- Findings: `SseClientTransport` still uses the legacy `/sse` + `endpoint` bootstrap and omits required headers (`MCP-Protocol-Version`, `Mcp-Session-Id`, `Accept`) on POST calls; it cannot consume SSE responses on the unified `/mcp` endpoint.
- Work: Collapsed to the single `/mcp` endpoint—initial GET opens the SSE stream while POSTs attach `Accept`, `Mcp-Session-Id`, and negotiated `MCP-Protocol-Version` headers. Captured session/negotiated protocol from server responses and removed the legacy `"endpoint"` event flow. SSE handshake now flushes headers and a keep-alive comment so `HttpClient` completes immediately and we get deterministic session IDs.
- Tests: Added `SseClientTransportTests` that simulate the GET handshake and subsequent POSTs via an in-memory handler/SSE stream, verifying header propagation and response handling.
- Notes: Transport now disposes of the SSE reader/response cleanly, gracefully handles server-initiated disconnects, and downgrades expected cancellation noise to debug-level logging.

## Issue 3: OAuth Discovery, Dynamic Registration & Token Acquisition ✅
- Findings: The client still relied on API keys and could not parse `401 WWW-Authenticate` challenges, follow protected-resource metadata, or obtain bearer tokens. Our demo server only supported static client credentials, so the PKCE sample required hard-coded IDs.
- Work: Completed the OAuth pipeline end-to-end. The client now parses challenges, performs discovery, and uses a pluggable `IOAuthTokenProvider`. A new shared support library (`Mcp.Net.Examples.Shared`) hosts `DemoOAuthDefaults` so the samples compile cleanly. SimpleServer gained an in-memory `DemoOAuthClientRegistry` and `/oauth/register` endpoint that is advertised through its metadata documents, mirroring RFC 7591. Existing static credentials (`demo-client`) remain seed data, but PKCE flows now auto-register at runtime and receive ephemeral client IDs. SimpleClient’s PKCE mode uses `DemoDynamicClientRegistrar` to register, then runs the authorization-code flow with the returned credentials; client-credentials mode continues to rely on the seeded confidential client. All OAuth-aware transports retry after `401` responses and attach the negotiated `Authorization: Bearer` header automatically. The server logs now reflect registration and per-client subjects, providing realistic telemetry for production integrators.
- Tests: Added `DemoDynamicClientRegistrarTests` to ensure the registration payload and metadata resolution behave correctly, and `DemoOAuthClientRegistryTests` to cover registry validation (redirect URIs, secrets, grant permissions). Existing transport/OAuth tests continue to validate challenge handling.
- Notes: Developers embedding these libraries can still plug in their own token providers (e.g., Supabase) without dynamic registration; the demo registry simply removes the last manual step from the sample PKCE flow while remaining spec compliant.

## Issue 4: JSON-RPC Models & `_meta` Parity
- Findings: Client DTOs drop `_meta`, structured tool results, and resource links that the server now emits, limiting interoperability.
- Work: Updated core models to mirror the 2025‑06‑18 schema—`ToolCallResult` now exposes `structured`, `resourceLinks`, and `_meta`, tool/resource/prompt DTOs surface annotations/meta/title, and a new `ToolCallResourceLink` type captures linked resources.
- Tests: Expanded `CallToolResultTests` to cover structured payloads and resource links, ensuring round-tripping with metadata intact.

## Issue 5: STDIO Transport Parity
- Findings: `StdioClientTransport` ignores the negotiated protocol version, lacks configurable timeouts, and does not surface cancellation/progress semantics.
- Work: Share the negotiated protocol state with the stdio transport, add timeout and cancellation handling per spec guidance, improve error reporting for malformed frames, and expose progress notifications to the client.
- Additional Work: Propagate negotiated protocol metadata through `StdioClientTransport`, surface server notifications via a new client-side event hook, and log progress updates so long-running operations can emit heartbeats similar to the HTTP transport.
- Tests: Stream-based unit tests covering newline framing, malformed message handling, timeout cancellation, and progress notification propagation.

## Issue 6: Sample Applications & Documentation Refresh ✅
- Findings: `Mcp.Net.Examples.SimpleClient` still demonstrates API-key flows and the deprecated transport handshake, making the samples misleading.
- Work: Rewrite samples to demonstrate OAuth discovery, token acquisition, and the updated `/mcp` endpoint (with an opt-in unauthenticated dev mode). Update README/AGENTS guidance so consumers understand configuration requirements and manual validation steps. Latest changes added richer diagnostics, ensured SSE handshake completes immediately, expanded the sample flow to validate tools (success + error), resources, prompts, and structured payloads against live SimpleServer, and added a PKCE integration path for end-to-end OAuth validation. Seeded SimpleServer with markdown resources plus reusable prompts and taught SimpleClient to verify them during its integration run so the capability surfaces stay regression-tested. Root README and the sample README files now document the PKCE flow, dynamic registration, and the seeded resource/prompt catalogue.
- Tests: Lightweight integration harness that exercises SimpleClient against SimpleServer in unauthenticated mode, plus documented manual/PKCE steps for validating OAuth end-to-end.

Each issue lands as an isolated commit with accompanying unit tests, ensuring incremental progress while keeping classes short, focused, and easy to review.

### Additional TODOs (priority order)
1. **400/refresh retry integration guardrails.** Once coverage expands, add integration checks that validate error-body propagation and refresh retry limits so transport regressions are caught early.
2. **Future polish: browser/desktop helpers.** Optional convenience APIs for launching the authorization URL in a browser or handling custom redirect servers can follow after the spec-alignment items land.
3. **OAuth hardening tests.** Extend coverage for JWT audience/issuer enforcement, replay/expiration handling, and metadata discovery failures so the demo issuer mirrors production-grade security expectations.
