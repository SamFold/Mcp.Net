# VNext: Mcp.Net.Agent

## Current status

- `ChatSession` now owns prompt, transcript, request-default, and provider-facing tool state for the agent loop.
- The provider boundary is clean: `ChatSession` builds `ChatClientRequest` snapshots and `Mcp.Net.LLM` executes them through `IChatClient.SendAsync(...)`.
- `ChatSession` now validates tool execution against its own session-owned tool catalog; `IToolRegistry` is no longer a runtime dependency inside the loop.
- Tool execution now sits behind `IToolExecutor`, with `McpToolExecutor`, `LocalToolExecutor`, and `CompositeToolExecutor` available under `Mcp.Net.Agent.Tools`.
- Mixed local+MCP turns are now covered through the shared executor seam, and missing-session-tool failures are enforced from the session-owned catalog.
- `SendUserMessageAsync(...)` now accepts a `CancellationToken` and flows it through provider requests plus tool execution.
- Provider-wait cancellation and tool-execution cancellation are now deterministic, including partial tool-result persistence when some tool work finished before the turn was canceled.
- `ChatSession` is now explicitly single-active-turn: overlapping sends are rejected, mutators are guarded while a turn is active, and the session exposes `IsProcessing`, `AbortCurrentTurn()`, and `WaitForIdleAsync(...)`.
- The library now has a session-composition seam: `IChatSessionFactory`, `ChatSessionFactoryOptions`, and `ChatSessionFactory` can build sessions from local tools plus an optional caller-owned `IMcpClient`.
- The obsolete `AgentDefinition` / manager / registry / store stack has been removed; `Mcp.Net.Agent` is now centered on runtime/session concerns only.

## Goal

- Add the first concrete built-in/local tools on top of the narrowed chat-session runtime and session factory seam.

## What

- Add the first real `ILocalTool` implementations that the factory and runtime can expose end to end.
- Keep the tools bounded and deterministic so they validate the seam without reopening broader shell/process-policy work.
- Compose them through `ChatSessionFactoryOptions.LocalTools` rather than pushing more construction logic into callers or `ChatSession`.

## Why

- The runtime and factory seams are now in place and the obsolete model layer is gone.
- Adding concrete tools now validates the new factory composition path under real in-process behavior.
- This is the next smallest slice that turns the current API surface into something a non-Web UI consumer can use directly.

## How

### 1. Keep the first tools small

- Start with read-only, bounded tools such as `ReadFileTool` and `ListFilesTool`.
- Keep inputs and outputs constrained so the tools are deterministic and easy to test.
- Defer `BashTool` and any write/edit behavior until the public tool surface is more mature.

### 2. Use the factory seam directly

- Register local tools through `ChatSessionFactoryOptions.LocalTools`.
- Let the factory merge provider-facing descriptors and choose the correct executor graph.
- Keep `ChatSession` unchanged; it should only consume the already-composed tool catalog and executor.

### 3. Prove the real path

- Add focused unit tests for the concrete tool implementations.
- Add at least one runtime-level test that constructs a session through `IChatSessionFactory` and executes a real local tool end to end.
- Keep the completed lifecycle and mixed-tool execution tests green.

## Scope

- In scope:
  - add the first concrete built-in/local tools
  - compose them through `ChatSessionFactory`
  - add focused tool tests plus runtime coverage through the factory seam
  - preserve the completed `ChatSession` lifecycle, executor, and tool-registry behavior
- Out of scope:
  - `BashTool`, write/edit tools, or broad shell/process-policy work
  - full MCP tool-call cancellation while `IMcpClient.CallTool(...)` lacks `CancellationToken`
  - transcript persistence redesign
  - changes to the provider request/stream contract beyond the current lifecycle surface

## Current slice

1. Add the first concrete built-in/local tools on top of `ILocalTool`.
2. Compose them through `ChatSessionFactoryOptions.LocalTools`.
3. Add focused tool tests plus runtime coverage proving real local tool execution through the factory seam.
4. Keep the current lifecycle contract and mixed local+MCP routing stable.

## Next slices

1. Revisit `IMcpClient` ergonomics around `CallTool` cancellation and async disposal once a real caller needs it.
2. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
3. Consider hook/extension or branching surfaces only after the core loop is more robust.
4. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added cancellation-token flow through `SendUserMessageAsync(...)`, provider requests, and tool execution in `ChatSession`.
- Defined deterministic abort behavior for provider waits and tool execution, including partial tool-result persistence for work that finished before cancellation.
- Removed `IToolRegistry` from `ChatSession` and made the session-owned tool catalog the sole validation source inside the loop.
- Added `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor` under `Mcp.Net.Agent.Tools`.
- Added focused executor tests and `ChatSession` regressions covering missing-session-tool failures plus mixed local+MCP turns through one executor graph.
- Added a single-active-turn lifecycle contract to `ChatSession`, including overlap rejection, busy-state lifecycle APIs, and mutation guards while a turn is active.
- Added `IChatSessionFactory`, `ChatSessionFactoryOptions`, and `ChatSessionFactory` so callers can compose sessions from local tools plus an optional caller-owned `IMcpClient`.
- Removed the obsolete agent-definition / manager / registry / store layer from `Mcp.Net.Agent` and the corresponding agent-driven paths from `Mcp.Net.WebUi`.

## Open decisions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project immediately?
- When `IMcpClient` evolves, should the session factory stay consumer-owned only or grow an owning-handle path later?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Keep the completed `ChatSession` lifecycle contract stable while adding concrete tools.
- Verify real local tools execute through `ChatSessionFactory` without reopening `ChatSession`.
- Run broader `Mcp.Net.Tests.Agent` and relevant `Mcp.Net.Tests.WebUi` coverage after the focused pass is green.
