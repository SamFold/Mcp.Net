# Roadmap: Mcp.Net.Agent

## Current focus

- Keep the cleaned request/session split stable while `Mcp.Net.Agent` starts using the local-tool runtime for real work.
- The immediate value is now the first concrete built-in/local tools: `ChatSession` already owns tool validation, the runtime already executes mixed local and MCP-backed work through one executor seam, and cancellation already flows through the turn call path.
- The current abort surface is intentionally token-based. A later `AbortCurrentTurn()` wrapper can remain optional unless a real consumer asks for it.

## What

- Add the first concrete `ILocalTool` implementations.
- Compose them into the existing `LocalToolExecutor` / `CompositeToolExecutor` path outside `ChatSession`.
- Prove the concrete tool path through focused tests and runtime integration coverage.
- Keep the cancellation-aware runtime seam intact while adding those tools.

## Why

- The runtime seams are now in place and tested: session-owned tool catalogs, a provider boundary, a backend-agnostic executor graph, and token-driven cancellation through the active turn.
- Until concrete local tools exist, the new local/composite execution design is still only infrastructure.
- The next step should prove that the abstractions hold up under real in-process tool behavior before the repo adds more convenience APIs around them.

## How

### Concrete local tools

- Start with a minimal, useful set of concrete local tools rather than a broad unfinished toolbox.
- Keep the implementations cancellation-aware from the start by honoring the existing `CancellationToken` on `ILocalTool.ExecuteAsync(...)`.
- Keep provider-facing descriptors and execution logic on the same concrete tool objects.

### Composition boundary

- Build descriptors and local-executor registrations from the same concrete tool objects.
- Merge those descriptors with MCP descriptors before constructing `ChatSession`.
- Keep `ChatSession` ignorant of backend details.

### Verification

- Add focused tests for the concrete local tools themselves.
- Add runtime-level coverage proving the concrete tools execute correctly through the existing agent loop.
- Keep cancellation tests in scope so concrete local tools do not regress the token-based abort seam.

## Near-term sequence

1. Add the first concrete built-in/local tools once the contracts, routing, and abort semantics are stable.
2. Add an explicit `AbortCurrentTurn()` wrapper only if a real consumer needs a session-owned convenience API on top of the current token-based flow.
3. Revisit agent-owned transcript persistence when non-Web UI consumers need durable session state.
4. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
5. Revisit context-window management with a stronger trigger or summarizer path once real conversation pressure justifies it.

## Recently completed

- `ChatSession` now flows caller cancellation through provider requests and tool execution.
- Abort behavior is now deterministic for provider waits and tool execution, including partial tool-result persistence when some tool work finished before cancellation.
- `ChatSession` now validates tool execution against its own configured tool catalog and no longer depends on `IToolRegistry` at runtime.
- `Mcp.Net.Agent.Tools` now includes `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor`.
- Focused tests now cover mixed local+MCP turns plus missing-session-tool failure semantics through the shared executor seam.

## Dependencies and risks

- Full MCP tool-call cancellation still depends on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- The provider boundary should remain snapshot-based; the runtime should not reintroduce provider-owned conversation state.
- Concrete local tools need disciplined scope. If the first tool set balloons, the repo will mix contract validation with product-surface sprawl and lose the value of the slice.
- The current compaction trigger is intentionally simple; future pressure may require token-aware estimation or a stronger summarizer path.

## Open questions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project as soon as the contracts land?
- Should local tools always be app-owned reusable registrations, or should the runtime explicitly support session-owned local tool instances from the start?
- Which concrete local tools are the best first proof of the seam?
- Should a later `AbortCurrentTurn()` wrapper exist once a real consumer asks for it?
- When context-window pressure grows further, should the next compaction improvement be token-aware estimation or provider-backed summarization?
