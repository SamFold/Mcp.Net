# Roadmap (Mcp.Net)

This document tracks the medium-term sequence of work across the repo.
Update it when priorities, milestones, or major decisions change.

## Current priority
1. Finish the `Mcp.Net.Server` stability and consistency review

## Near-term roadmap
1. Logging/debuggability and hidden mutable state review
2. MCP server review closure and cleanup
3. MCP spec alignment work across server, client, and LLM integrations
4. Continued integration coverage for SSE and stdio parity

## Recently completed
- Server-driven `list_changed` notifications now wait for `notifications/initialized`, so protocol negotiation is no longer treated as equivalent to lifecycle readiness
- Reconnect replacement now starts before registration, so a failed replacement startup no longer evicts the existing live transport for that session
- `ConnectAsync` now rolls back startup-failed transports so a `StartAsync` exception does not leave a dead transport registered in the connection manager
- Hosted SSE connections now use a single authoritative registration path, removing duplicate transport registration and duplicate close-subscription state on initial connect
- Transport errors now converge on the close path, so fatal send failures clear session-scoped negotiated state and remove the broken transport instead of leaving stale active-session state behind
- `initialize` now suppresses the unimplemented `logging` capability even when callers set `ServerCapabilities.Logging`, so capability advertisement stays truthful
- SSE and stdio server transports now serialize outbound writes per connection so overlapping responses, requests, and notifications cannot enter the shared writer concurrently
- SSE and stdio client transports now raise `OnClose` when the remote side ends the connection, and pending client requests now fail promptly with cancellation semantics instead of hanging or surfacing a false timeout
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
- Serialize outbound writes per transport so responses, requests, and notifications cannot corrupt one another on a shared connection
- Keep capability advertisement truthful so `initialize` does not promise primitives the server does not implement

## Broader roadmap
1. MCP server review closure and cleanup
2. MCP spec alignment work across server, client, and LLM integrations
3. Improved examples and diagnostics for OAuth, elicitation, completion, and tool execution
4. Continued integration coverage for SSE and stdio parity

## Notes
- `docs/vnext.md` is for the next slice only.
- This file is for the broader sequence of upcoming work.
- The builder/DI inconsistency slice is now closed for the concrete default-copy bugs found in this review pass.
- The SSE vs stdio parity slice has closed the concrete gaps found in this pass: per-session elicitation capability enforcement, disconnect handling, client remote-close propagation, and outbound write serialization.
- The next active review area is logging/debuggability and hidden mutable state.
- The logging-capability truthfulness gap is now closed by suppressing unimplemented `logging` from advertised server capabilities.
- The transport-error hidden-state gap is now closed by forcing fatal transport errors through the normal close cleanup path.
- The hosted SSE duplicate-registration hidden-state gap is now closed by removing the redundant host-side registration before `McpServer.ConnectAsync`.
- The transport-startup rollback gap is now closed by cleaning up registration when `ConnectAsync` fails during `StartAsync`.
- The reconnect replacement startup gap is now closed by delaying session replacement until the new transport has started successfully.
- The lifecycle-readiness gap is now closed by requiring `notifications/initialized` before server-driven `list_changed` broadcasts.
