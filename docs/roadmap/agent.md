# Roadmap: Mcp.Net.Agent

## Current focus

- Add the first concrete built-in/local tools now that the obsolete agent-management model is gone and the package boundary is narrower.
- Keep the `ChatSession` lifecycle and factory seams stable while the first real in-process tools land.
- Validate the public session-composition story with concrete behavior rather than infrastructure-only doubles.

## What

- Add the first concrete `ILocalTool` implementations.
- Compose them through `ChatSessionFactoryOptions.LocalTools`.
- Prove them through focused tool tests plus runtime coverage using `IChatSessionFactory`.

## Why

- The runtime and factory seams are now in place and the dead model layer is gone.
- The next useful proof point is real in-process behavior through the new composition surface.
- Read-only bounded tools are still the smallest slice that makes the current API surface practically useful.

## How

### Concrete local tools

- Start with read-only bounded tools such as file read/list operations.
- Keep the tools deterministic and cancellation-aware from the start.
- Defer shell execution and write/edit behavior until the public tool surface has more real usage behind it.

### Verification

- Add focused tests for the concrete tools themselves.
- Add runtime-level coverage proving those tools execute correctly through `IChatSessionFactory`.
- Keep the completed `ChatSession` lifecycle tests and broader agent/runtime coverage green.

## Near-term sequence

1. Add the first concrete built-in/local tools now that the public construction story is stable.
2. Revisit `IMcpClient` ergonomics when a real caller needs `CallTool` cancellation or async disposal.
3. Revisit session-owned transcript persistence when non-Web UI consumers need durable session state.
4. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
5. Revisit context-window management with a stronger trigger or summarizer path once real conversation pressure justifies it.

## Recently completed

- Removed the obsolete `AgentDefinition` / manager / store / registry model and agent-oriented DI/extensions from `Mcp.Net.Agent`.
- Removed the corresponding agent-driven controllers, DTOs, startup hooks, and chat-factory branches from `Mcp.Net.WebUi`.
- Narrowed the remaining registration story to `AddChatRuntimeServices()` plus `AddChatSessionFactory()`.
- `ChatSession` now flows caller cancellation through provider requests and tool execution.
- Abort behavior is now deterministic for provider waits and tool execution, including partial tool-result persistence when some tool work finished before cancellation.
- `ChatSession` now validates tool execution against its own configured tool catalog and no longer depends on `IToolRegistry` at runtime.
- `Mcp.Net.Agent.Tools` now includes `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor`.
- Focused tests now cover mixed local+MCP turns plus missing-session-tool failure semantics through the shared executor seam.
- `ChatSession` now rejects overlapping turns, exposes `IsProcessing` plus abort/wait lifecycle APIs, and blocks mutable state changes while a turn is active.
- `Mcp.Net.Agent` now includes `IChatSessionFactory`, `ChatSessionFactoryOptions`, and `ChatSessionFactory` for library-first session composition with caller-owned MCP clients.

## Dependencies and risks

- Full MCP tool-call cancellation still depends on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- The provider boundary should remain snapshot-based; the runtime should not reintroduce provider-owned conversation state.
- The first local tools need disciplined scope. If they expand into shell/write behavior too early, the slice will mix seam validation with policy decisions.
- The current compaction trigger is intentionally simple; future pressure may require token-aware estimation or a stronger summarizer path.

## Open questions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project as soon as the contracts land?
- Should local tools always be app-owned reusable registrations, or should the runtime explicitly support session-owned local tool instances from the start?
- When context-window pressure grows further, should the next compaction improvement be token-aware estimation or provider-backed summarization?
