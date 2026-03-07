# OAuth Authorization Update Summary (Issue 7)

## Overview
- Migrated the server from legacy API-key gating to an OAuth 2.1 resource-server model aligned with MCP spec revision 2025-06-18.
- Added `OAuthResourceServerOptions` and `OAuthAuthenticationHandler` so transports share a single token-validation pipeline (JWT + OpenID Connect discovery + resource indicator enforcement).
- Hardened the SSE transport by validating `Origin`, emitting RFC 9728-compliant `WWW-Authenticate` challenges, and publishing protected-resource metadata at `/.well-known/oauth-protected-resource`.
- Defaulted samples (console, stdio, web UI) to unauthenticated mode until proper OAuth configuration is supplied, preventing accidental dependence on obsolete API keys.

## Testing Summary
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj`
  - Added unit coverage for `OAuthAuthenticationHandler` (valid token, audience mismatch, query-string token path) and SSE origin rejection to protect against CSRF.
- Manual validation checklist (requires real auth server):
  1. Configure `OAuthResourceServerOptions` with `Authority`, `Resource`, and signing keys.
  2. Issue a token for the resource and confirm POST `/mcp` returns 202; invalid/expired tokens should trigger `401` with `WWW-Authenticate: Bearer resource_metadata="..."`.
  3. Hit `/mcp` without `Origin` or from disallowed host to verify `403 invalid_origin` response.
  4. Retrieve `/.well-known/oauth-protected-resource` and ensure `authorization_servers` matches the configured metadata endpoint.
- Pending automated work: integration tests using a stub authorization server to exercise the end-to-end discovery flow and HTTP 202/401/403 cases.

## Follow-up Improvements
1. **Configuration Pipeline**
   - Expose builder/host configuration knobs for `Authority`, `Resource`, signing keys, and TLS requirements.
   - Provide per-environment overrides (appsettings + environment variables) matching the spec’s security guidance.
2. **Discovery & Client Guidance**
   - Document how to stand up a compliant authorization server, register clients (dynamic or manual), and supply PKCE/resource parameters.
   - Update sample apps to demonstrate the full OAuth handshake once configuration is available.
3. **Transport Consistency**
   - Share the OAuth handler across stdio once transport-specific expectations are finalised (or explicitly document stdio exclusions per spec).
   - Implement Accept-header enforcement, DELETE session support, and SSE event IDs (Issue 10) with OAuth in mind.
4. **Observability & Logging**
   - Add structured success/failure logging for token validation events while respecting security constraints.
   - Consider metrics for unauthorized attempts to surface misconfiguration or attack attempts.
5. **Integration Tests**
   - Build automated tests that spin up a mock OAuth server to exercise resource metadata discovery, token validation, and failure paths.
   - Validate that `WWW-Authenticate` headers carry the protected-resource metadata URL in all relevant error responses.
