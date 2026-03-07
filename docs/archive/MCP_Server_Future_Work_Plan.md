# MCP Server Future Work (Spec 2025-06-18)

This document tracks the remaining tasks required to bring `Mcp.Net.Server` fully in line with the 2025‑06‑18 MCP specification. Each issue is purposely scoped to land as discrete commits with appropriate tests.

## Issue 7: OAuth-Based Authorization
- Findings: HTTP transport does not expose OAuth resource metadata, enforce Origin headers, or support Resource Indicators.
- Why it matters: The 2025 spec classifies servers as OAuth resource servers; without this clients cannot authenticate securely.
- Work: Surface discovery metadata, validate `Origin`, respect Resource Indicator requirements, and document configuration/testing.
- Progress (Step 1): Added `OAuthResourceServerOptions`, wired authentication builders to surface OAuth settings, registered the options with DI, and defaulted the canonical MCP resource URL when the SSE server boots. Learned that existing middleware skipped registered `AuthOptions`, so DI wiring was essential before we could layer the remaining transport changes.
- Progress (Step 2): Hardened the SSE transport with origin validation, exposed the `.well-known/oauth-protected-resource` endpoint, and emitted RFC 9728-compliant `WWW-Authenticate` headers. Learned that tests require explicit host metadata or they'll bypass the new checks—future harness updates should configure hosts explicitly.
- Progress (Step 3): Implemented `OAuthAuthenticationHandler` with JWT validation, OpenID Connect discovery, and resource-indicator enforcement. Lessons: we default the audience to the MCP endpoint when unset and rely on discovery refresh when signatures fail, ensuring alignment with RFC 8729 flows.
- Progress (Step 4): Added unit tests for the OAuth handler (happy-path, audience failure, query token) and HTTP origin rejection, verifying the new pathways and running `dotnet test` to confirm regressions.
- Progress (Step 5): Removed the temporary API key authentication surface across the server, builders, transports, examples, and UI clients so OAuth is the only supported path. Samples now default to unauthenticated mode until OAuth configuration is supplied, preventing drift between spec guidance and sample code.

**Remaining gaps for Issue 7**
- Provide configuration surfaces (builder/options/environment) for authority metadata, OAuth client settings, and resource identifiers so operators can enable real authorization flows.
- Implement integration tests covering 401/403 responses, metadata discovery, and token audience validation once an authorization server stub is available.
- Supply docs/samples showing how to provision tokens and register clients (including PKCE + resource indicators).
- Ensure stdio transport reuses the OAuth handler for consistency once transport-level authentication requirements are finalised.
- Consider middleware for attaching `WWW-Authenticate` metadata on all relevant failure paths (including 403) and logging token validation results for observability.

### Provider Integration & Claim Mapping (NEW)
- **Auth0 / Entra ID**: Document how to set `Server:Authentication:OAuth:Authority`, add the protected-resource metadata path, and configure audiences/issuers. Include steps for creating Auth0/Entra applications that mint tokens carrying the user ID (`sub`) and optional role claims. Provide sample `appsettings.json` and instructions for provisioning JWKS endpoints / dynamic client registration.
- **Supabase**: Explain token exchange: either enable Supabase JWT signing and treat Supabase as the authorization server, or introduce a lightweight exchange service that mints MCP-scoped tokens using Supabase session claims. Highlight how to populate `Server:Authentication:OAuth:ValidIssuers` with Supabase JWT issuer, add the signing secret as a Base64 key, and map `sub` -> Supabase `user.id` for database queries.
- **Clerk.com**: Outline using Clerk’s OAuth applications to mint tokens for the MCP resource. Document required endpoints (issuer, JWKS) and the claim that represents the Clerk user (`sub`/`user_id`). Provide guidance for multi-tenant org claims if present.
- **Claim Usage Guidance**: Capture how tool code reads `AuthResult` from the HTTP context (`context.Items["AuthenticatedUserId"]`) and enforces that tool inputs match the authenticated subject, or checks for admin/coach roles before acting on another user’s data. Add recommendations for logging user identifiers to aid audits while respecting privacy.
- **Next Steps**: After documenting each provider, build integration tests or manual validation checklists to ensure tokens from these IdPs pass the resource-indicator enforcement and populate expected claims.

## Issue 8: Client Elicitation & Completions
- Findings: Client capability DTOs expose `elicitation`/`completions`, but the server lacks runtime handling and completion context support.
- Why it matters: Clients rely on these flows to guide user input and provide argument suggestions.
- Work: Implement `elicitation` message handling, completion request/response plumbing (including `CompletionRequest.context`), and tests.
- Progress: Introduced `IElicitationService` that issues `elicitation/create` requests via the active transport, tracks pending responses, and enforces timeouts. SimpleServer now demonstrates the flow by letting users adjust generated Inquisitors, and unit tests cover accept/decline/error scenarios. Completion handling remains outstanding.

## Issue 9: Tool Output Schema & Annotations
- Findings: Structured tool output is partially supported, but server does not validate against `outputSchema` or surface annotations.
- Why it matters: Tool callers need predictable structured payloads and annotation metadata for trust/safety decisions.
- Work: Extend tool registry/call sites to honour `outputSchema`, propagate annotations, and add validation/coverage.

## Issue 10: Streamable HTTP Hardening
- Findings: HTTP transport lacks Accept header enforcement, resumable SSE event IDs, and DELETE session handling.
- Why it matters: Clients expect strict content negotiation, resumability, and lifecycle control per the transport spec.
- Work: Enforce Accept/Content-Type rules, implement optional SSE `id` support with `Last-Event-ID`, add DELETE handling, and expand tests.

## Issue 11: STDIO Lifecycle & Diagnostics
- Findings: STDIO transport does not signal negotiated protocol metadata, and shutdown/logging behaviour is only lightly covered.
- Why it matters: The spec recommends clear shutdown semantics and consistent telemetry across transports.
- Work: Echo negotiated protocol info (as log/trace), tighten shutdown behaviour/tests, and document expected client/server interactions.
- Progress: `McpServer` now logs the negotiated MCP revision during initialization so stdio connections expose the same metadata as HTTP/SSE sessions. Remaining follow-up includes richer shutdown traces and emitting structured lifecycle events from the server transport itself.

## Issue 12: Metadata Validation & Utility Helpers
- Findings: `_meta` is now stored, but key prefix validation and helpers are missing.
- Why it matters: Incorrect metadata keys could collide with reserved namespaces; utilities ease correct use across features.
- Work: Add `_meta` validation helpers, guard against reserved prefixes, and add unit tests for allowed/disallowed keys.

## Issue 13: Structured Logging Capability
- Findings: Server advertises logging capability only when implemented; current code has no logging feature to expose.
- Why it matters: Clients depend on structured logging streams for diagnostics and spec compliance.
- Work: Design pluggable logging emission (e.g., dedicated JSON-RPC notifications or endpoints), gate the capability, and cover with tests/docs.

## Issue 14: Schema Delta Sweep
- Findings: Earlier spec revisions added features (progress `message`, audio content, batching removal) that need verification.
- Why it matters: Ensuring smaller schema deltas are covered prevents subtle interoperability bugs.
- Work: Audit schema from 2024‑11‑05 → 2025‑06‑18, implement missing pieces (e.g., audio content handling), and document results.

**Next steps:** Prioritize issues based on risk and user impact—authorization (Issue 7) and client interaction (Issue 8) are the most visible gaps.
