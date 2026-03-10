# Roadmap: Cross-Cutting

## Current focus

- Keep the broader sequence across active project lanes visible without forcing repo-level planning back into a single monolithic roadmap file.
- Track the follow-on work that intentionally spans multiple projects or only becomes actionable after the current Client, Server, and LLM lanes settle.

## Near-term sequence

1. Close the remaining MCP server review items and cleanup once the current `Mcp.Net.Server` review pass is exhausted.
2. Continue MCP spec alignment work across server, client, and LLM integrations after the current project-local review lanes land.
3. Improve examples and diagnostics for OAuth, elicitation, completion, and tool execution.
4. Continue integration coverage for SSE and stdio parity, especially where client and server changes can silently drift apart.
5. Promote `Mcp.Net.Core`, `Mcp.Net.WebUi`, `Mcp.Net.Examples.*`, or `Mcp.Net.Tests` into their own roadmap files once any of them becomes an active independent planning lane.

## Active dependencies

- `Mcp.Net.LLM` session cancellation spans `Mcp.Net.LLM` and `Mcp.Net.Client`.
- `Mcp.Net.LLM` result metadata propagation may span `Mcp.Net.LLM` and `Mcp.Net.WebUi`.
- `Mcp.Net.Client` Streamable HTTP changes should keep re-running the relevant server-client integration slice against `Mcp.Net.Server`.

## Recently completed

- The repo now uses `docs/vnext.md` plus `docs/vnext/*.md` for commit-sized active slices instead of one global next-step document.
- The repo-level roadmap has been split into project-specific files under `docs/roadmap/`, with `docs/roadmap.md` retained as the cross-project index and priority page.

## Open questions

- When `Mcp.Net.WebUi` or `Mcp.Net.Core` picks up enough independent work, should it get its own roadmap file immediately, or continue to ride along inside the owning project lane until the work becomes clearly independent?
