# Roadmap (Mcp.Net)

This document tracks the medium-term sequence of work across the repo.
Update it when priorities, milestones, or major decisions change.

## Current priority
1. Finish the `Mcp.Net.Server` stability and consistency review

## Near-term roadmap
1. Remaining builder/DI inconsistencies
2. SSE vs stdio parity for server-initiated flows
3. Logging/debuggability and hidden mutable state review

## Recently completed
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

## Broader roadmap
1. MCP server review closure and cleanup
2. MCP spec alignment work across server, client, and LLM integrations
3. Improved examples and diagnostics for OAuth, elicitation, completion, and tool execution
4. Continued integration coverage for SSE and stdio parity

## Notes
- `docs/vnext.md` is for the next slice only.
- This file is for the broader sequence of upcoming work.
- The next active bug class is still remaining builder/DI inconsistencies, with the next concrete target being the stdio builder registration path.
