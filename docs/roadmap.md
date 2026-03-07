# Roadmap (Mcp.Net)

This document tracks the medium-term sequence of work across the repo.
Update it when priorities, milestones, or major decisions change.

## Current priority
1. Finish the `Mcp.Net.Server` stability and consistency review

## Near-term roadmap
1. SSE vs stdio parity for server-initiated flows
2. Logging/debuggability and hidden mutable state review

## Recently completed
- Added integration coverage proving outbound server-initiated elicitation cancels promptly on disconnect for both SSE and stdio
- Server-initiated elicitation now honors per-session client capability negotiation instead of sending requests to sessions that never advertised `elicitation`
- `AddMcpStdioTransport(McpServerBuilder)` now preserves builder-configured server identity and instructions during DI registration
- `AddMcpStdioTransport(StdioServerOptions)` now preserves configured stdio and shared server option values during DI registration
- `AddMcpCore(McpServerBuilder)` now preserves builder-configured server identity and instructions in the DI-registered `McpServerOptions`
- `AddMcpSseTransport(SseServerOptions)` now preserves routing and security settings instead of dropping them during DI registration
- Hosted SSE builder path now honors configured MCP and health endpoints
- Hosted SSE requests now reuse middleware-authenticated request state instead of authenticating twice
- Server-driven `notifications/.../list_changed` broadcasts now fire for post-initialize tool, prompt, and resource mutations
- LLM and WebUI refresh listeners now accept the spec notification names and refresh-path coverage is in place
- `HandleRequestAsync` now preserves cancellation tokens through resource, prompt, and completion execution
- True non-tool request cancellation now propagates as cancellation instead of being normalized to `InternalError`
- Completion handlers now receive a request-context snapshot with session, transport, and metadata
- Prompt and resource handlers now receive a request-context snapshot with session, transport, and metadata

## Server stability themes
- Keep all session-scoped state isolated by connection/session
- Prefer one authoritative routing path per transport
- Add regression coverage for every production bug we fix
- Keep auth, origin validation, and teardown behavior consistent across hosting paths
- Keep refresh/list-changed behavior spec-aligned so connected clients do not drift stale
- Preserve request cancellation and request metadata until the final handler boundary
- Preserve client capability negotiation per session before issuing server-initiated client-feature requests

## Broader roadmap
1. MCP server review closure and cleanup
2. MCP spec alignment work across server, client, and LLM integrations
3. Improved examples and diagnostics for OAuth, elicitation, completion, and tool execution
4. Continued integration coverage for SSE and stdio parity

## Notes
- `docs/vnext.md` is for the next slice only.
- This file is for the broader sequence of upcoming work.
- The builder/DI inconsistency slice is now closed for the concrete default-copy bugs found in this review pass.
- The next active review area is SSE vs stdio parity for server-initiated flows.
- The first server-initiated flow gap closed in this area was per-session elicitation capability enforcement.
- Outbound elicitation disconnect coverage is now in place for both transports; the next parity candidate is server-initiated notification behavior.
