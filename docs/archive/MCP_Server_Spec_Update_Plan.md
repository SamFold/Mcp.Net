# MCP Server Spec Alignment Plan

This plan sequences the work needed to bring `Mcp.Net.Server` in line with the MCP specification revision **2025‑06‑18**. Each issue is scoped so we can land focused commits with accompanying tests.

## Issue 1: Initialization Negotiation
- Findings: `McpServer.HandleInitialize` always returns protocol `2024-11-05` and ignores the version the client requested (`Mcp.Net.Server/McpServer.cs:175`). We also don’t validate unsupported revisions or expose negotiated protocol headers.
- Why it matters: Spec §Lifecycle requires returning the latest supported version, rejecting incompatible ones, and persisting the negotiated version for subsequent transport use.
- Work: Update `McpServer` to compute the negotiated protocol version (latest supported constant for now), respond with that value, emit an error when the client requests unsupported versions, and retain the value for transports. Extend unit/functional tests to cover success, downgrade, and mismatch paths.
- Status: Completed. Summary: Added version negotiation with validation to `McpServer`, tracked the negotiated revision per transport, and expanded server tests to cover happy-path, fallback, and missing-version scenarios. Learnings: The server already centralizes request processing, so surfacing negotiated state for transports will be straightforward in later issues.

**Completed Work (Issue 1):** Negotiated protocol version handling added to the initialize flow, with tests covering supported, fallback, and missing version scenarios. The server now retains the negotiated version per transport.

## Issue 2: Streamable HTTP Transport Flow
- Findings: Our SSE transport still emits the legacy `"endpoint"` event and requires a secondary `/messages` POST (`Transport/Sse/SseTransport.cs:115`, `SseTransportHost.cs:265`). The spec now defines a single MCP endpoint handling both POST and GET.
- Why it matters: Clients written for 2025‑06‑18 expect immediate request handling on the initial POST and optional SSE GET subscriptions; the current handshake breaks interop.
- Work: Refactor the SSE/HTTP hosting pipeline to expose a unified MCP endpoint. The GET path should open an SSE stream directly; POST should dispatch JSON-RPC messages without redirecting. Add integration tests mirroring the new flow.
- Status: Completed. Summary: Consolidated GET/POST handling under `/mcp`, removed the legacy endpoint broadcast, emitted session IDs via headers, and taught message handling to accept the negotiated session header. Added coverage to ensure POST requests routed via headers trigger server responses. Learnings: Supporting existing clients during the transition requires temporary header/query fallbacks; downstream client transport will need an aligned update soon.

**Completed Work (Issue 2):** Unified the transport surface at `/mcp` for both SSE and POST, removed legacy endpoint events, and added header-aware session routing plus regression tests for the new flow.

## Issue 3: HTTP Protocol Semantics
- Findings: POST handlers return JSON payloads with 202 and don’t require `MCP-Protocol-Version` or `Mcp-Session-Id` headers (`SseTransportHost.cs:345`). Responses to notifications should be empty, and requests must capture/return negotiated headers.
- Why it matters: Spec §Transports mandates header enforcement, empty bodies on 202 Accepted, and proper session lifecycle; missing these causes non-compliance and security gaps.
- Work: Enforce header presence/values, persist session IDs, adjust response bodies, and cover header validation plus error cases with middleware-level tests.
- Status: Completed. Summary: Enforced session and protocol headers for POST traffic, returned empty 202 bodies, added protocol-aware error handling, and expanded transport tests for initialize, subsequent requests, mismatch/missing headers, and notification support. Learnings: Existing async flow allowed us to gate header validation without redesigning the server loop; per-transport protocol state will feed naturally into future metrics or authorization checks.
- Ongoing Notes: Refactored POST handling into helper methods, added XML documentation, and removed the unused cleanup timer to keep the HTTP transport logic small and self-documenting.

## Issue 4: Capability & Info Schema Updates
- Findings: `ServerCapabilities` only exposes `tools/resources/prompts/sampling`, and `ClientCapabilities` lacks `elicitation`. Both `ClientInfo` and `ServerInfo` are missing the new `title` property (`Mcp.Net.Core/Models/Capabilities/*.cs`).
- Why it matters: Negotiated capabilities drive optional feature use; omitting fields prevents advertising compliant behaviors and limits clients’ feature discovery.
- Work: Extend the capability models with the new properties, ensure serialization respects `_meta` (once added), and update default capability wiring plus tests that assert the serialized schema matches the spec.
- Status: Completed. Summary: Added `logging`, `completions`, and `experimental` slots to server capabilities, introduced client `elicitation`, and surfaced titles on client/server info with builder/options plumbing. Updated initialization tests to assert the new fields and ensured defaults populate empty capability objects for compatibility. Learnings: Keeping shared serializer options localized made validation straightforward; future shared parsing logic could move into a common utility if stdio adopts the same message classification.
- Follow-up: Surface `logging`/`completions` only when implementations exist—currently left unset until instrumentation and completion handlers are built.

## Issue 5: Metadata & Structured Tool Output
- Findings: DTOs drop the `_meta` field across requests/responses/content, and `ToolCallResult` only supports legacy `content/isError` results (`Mcp.Net.Core/JsonRpc/JsonRpcMessages.cs`, `Models/Tools/ToolCallResult.cs`).
- Why it matters: Structured output, resource links, and metadata are required for richer tool interactions and compatibility with modern clients.
- Work: Introduce extensible models that preserve `_meta`, support structured tool result shapes (including `content`, `isError`, `structured`, and `resourceLinks`), and extend transport/unit tests to assert round-tripping of metadata and structured payloads.
- Status: Completed. Summary: Added `_meta` to JSON-RPC messages, content, and tool results, introduced structured/tool resource link representations, and updated tests to confirm round-tripping. Learnings: Keeping metadata optional keeps wire payloads lean; future stdio updates may reuse the new payload helpers.

## Issue 6: STDIO Transport Parity Review
- Findings: STDIO transport assumes newline-terminated frames and doesn’t surface negotiated protocol metadata (`Transport/Stdio/StdioTransport.cs`). While mostly compliant, it should respect negotiated protocol details and document newline requirements test-wise.
- Why it matters: Both transports must align with the same negotiated protocol behavior and error handling.
- Work: Ensure STDIO shares the negotiated protocol state (from Issue 1), add explicit parse-error responses, and expand tests to cover malformed inputs versus spec expectations.

Once these issues are resolved we can reassess for remaining gaps (authorization refresh, elicitation handling, etc.) before updating the client library.

**Outstanding Spec Work:**
- Implement logging/completions capabilities so the server only advertises features with real handlers.
- Add `_meta` round-tripping and structured tool output support (Issue 5).
- Align STDIO transport with negotiated protocol metadata and timeout guidance (Issue 6).
- Revisit authorization/security updates once the new OAuth resource server flow is planned.
