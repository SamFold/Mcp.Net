# Roadmap: Mcp.Net.WebUi

## Current focus

- With the legacy agent-management UI/API path removed, decide whether Web UI should now compose sessions through `IChatSessionFactory`.
- Keep the default-model chat flow, transcript/history features, tool discovery, prompt/resource catalog, completion, and elicitation support intact.
- Avoid introducing another Web UI-only composition abstraction if the shared runtime seam is already sufficient.

## What

- Evaluate replacing inline `new ChatSession(...)` construction inside `ChatFactory` with the shared runtime factory seam.
- Keep per-session MCP-client creation/ownership in Web UI unless the runtime grows an explicit owning handle.
- Keep Web UI aligned with the narrowed `Mcp.Net.Agent` package boundary.

## Why

- The obsolete path is gone, so duplicated session-construction logic is now the most obvious remaining overlap between Web UI and the shared runtime.
- Using `IChatSessionFactory` may reduce drift without forcing Web UI to give up its per-session MCP-client orchestration.
- The next meaningful Web UI improvements should build on direct chat-session composition, not on reviving old agent-definition concepts.

## Near-term sequence

1. Decide whether `ChatFactory` should use `IChatSessionFactory` for `ChatSession` construction.
2. Keep non-agent chat/session creation and metadata/history behavior green.
3. Revisit session metadata defaults and naming now that the agent-derived path is gone.

## Dependencies and risks

- Web UI still depends on `Mcp.Net.Agent` for `ChatSession`, tool execution, session metadata/history contracts, prompt/resource catalogs, completion, and elicitation coordination.
- Session-start notification is now adapter-owned after the `Mcp.Net.Agent` session-start seam removal, so future runtime changes should keep re-running adapter lifecycle tests.
- The deletion slice should not weaken the current per-session MCP-client lifecycle or tool-list refresh behavior.
- Session titles and metadata defaults may need small follow-up cleanup after the agent-derived naming path disappears.
