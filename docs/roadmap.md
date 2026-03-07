# Roadmap (Mcp.Net)

This document tracks the medium-term sequence of work across the repo.
Update it when priorities, milestones, or major decisions change.

## Current priority
1. Finish the `Mcp.Net.Server` stability and consistency review

## Near-term roadmap
1. Hosted SSE builder/auth/origin consistency
2. Notification/completion/resource refresh routing review
3. Remaining builder/DI inconsistencies
4. SSE vs stdio parity for server-initiated flows
5. Logging/debuggability and hidden mutable state review

## Server stability themes
- Keep all session-scoped state isolated by connection/session
- Prefer one authoritative routing path per transport
- Add regression coverage for every production bug we fix
- Keep auth, origin validation, and teardown behavior consistent across hosting paths

## Broader roadmap
1. MCP server review closure and cleanup
2. MCP spec alignment work across server, client, and LLM integrations
3. Improved examples and diagnostics for OAuth, elicitation, completion, and tool execution
4. Continued integration coverage for SSE and stdio parity

## Notes
- `docs/vnext.md` is for the next slice only.
- This file is for the broader sequence of upcoming work.
