# VNext: Mcp.Net.WebUi

## Current status

- The primary chat experience already works without `AgentDefinition` selection by using `DefaultLlmSettings`, per-session MCP clients, and `ChatSession`.
- The legacy agent-driven endpoints, DTOs, startup wiring, and chat-factory branches have been removed.
- Web UI still constructs `ChatSession` inline inside `ChatFactory` even though the shared runtime now exposes `IChatSessionFactory`.
- The SignalR chat adapter now owns session-start notification directly after the `ChatSession` session-start seam was removed.

## Goal

- Decide whether Web UI should compose sessions through `IChatSessionFactory` instead of constructing `ChatSession` inline.

## What

- Evaluate moving the remaining inline `new ChatSession(...)` composition in `ChatFactory` onto `IChatSessionFactory`.
- Keep the existing non-agent chat flow, session history, prompt/resource catalog, completion, elicitation, and tool-refresh behavior intact.
- Avoid introducing another Web UI-only composition abstraction if the shared runtime seam is already sufficient.

## Why

- The legacy agent path is gone, so the remaining question is whether Web UI should now consume the shared runtime seam more directly.
- Using `IChatSessionFactory` would reduce duplicate session-construction logic and keep Web UI aligned with the library-first composition story.
- This is a small follow-up slice now that the obsolete path is out of the way.

## How

### 1. Compare the seams honestly

- Inspect what `ChatFactory` still does beyond session construction: per-session MCP client creation, tool-list refresh, prompt/resource catalogs, completion, and elicitation wiring.
- Move only the `ChatSession` composition portion if the boundary stays clear.
- Keep per-session MCP-client ownership in Web UI unless the shared runtime grows an explicit owning path.

### 2. Keep the chat path stable

- Preserve the existing metadata/history behavior and adapter lifecycle.
- Keep the default-model flow green while changing the construction seam.
- Avoid accidental behavior changes in tool refresh or prompt/resource/completion features.
- Keep the adapter aligned with the current `ChatSession` event/runtime contract while the shared runtime keeps tightening its internals.

### 3. Verify the reduced duplication

- Add or update tests around the surviving chat construction path.
- Keep SignalR adapter, chat factory, and tool-refresh behavior green.

## Scope

- In scope:
  - evaluate moving Web UI `ChatSession` construction onto `IChatSessionFactory`
  - preserve the default-model chat path and shared chat-session runtime behavior
  - reduce duplicated session-composition code where the seam is already stable
  - stay aligned with the current `ChatSession` runtime API while the shared runtime evolves
- Out of scope:
  - redesigning the Web UI chat UX
  - reintroducing agent-definition concepts
  - changing the MCP transport/auth flow beyond what deletion requires

## Current slice

1. Evaluate whether `ChatFactory` should use `IChatSessionFactory` for `ChatSession` construction.
2. Keep the direct chat-session path green while reducing duplicate composition logic.
3. Preserve per-session MCP-client lifecycle, tool refresh, prompt/resource catalog, completion, and elicitation behavior.

## Next slices

1. Revisit session metadata defaults and naming now that agent-derived titles are gone.
2. Revisit any remaining session-persistence cleanup once the runtime surface is narrower.
3. Revisit whether chat/session history abstractions belong in `Mcp.Net.Agent` or should live closer to Web UI.
