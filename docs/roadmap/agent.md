# Roadmap: Mcp.Net.Agent

## Current focus

- Build a library-first session factory/ownership model on top of the new explicit `ChatSession` lifecycle contract.
- The immediate value is now a clean consumer construction story: provider choice, executor composition, and resource ownership should not require internal runtime knowledge.
- After that construction/ownership layer is stable, add the first concrete built-in/local tools on top of the existing local/composite executor seam.

## What

- Introduce a library-first session factory surface.
- Support both consumer-owned dependencies and factory-owned disposable resources.
- Keep `ChatSession` lean and runtime-focused while the new factory handles construction/ownership concerns.
- Preserve the completed busy/abort/wait/mutation-guard lifecycle contract.

## Why

- The runtime now has explicit turn ownership, but consumers still need too much internal knowledge to construct and own a session correctly.
- Factory-created runtime dependencies need an explicit ownership/cleanup model.
- The first concrete local tools will be easier to expose once the library has a clean public construction story.

## How

### Factory and ownership

- Introduce a factory/options surface that does not depend on current app-specific wiring.
- Support both consumer-owned and factory-owned resource paths explicitly.
- Keep ownership and cleanup rules visible in the public API.

### Verification

- Add focused tests for construction, configuration, and ownership behavior.
- Keep the new `ChatSession` lifecycle tests green so the factory layer remains a thin composition surface.
- Run broader agent coverage after the focused factory tests pass.

## Near-term sequence

1. Add a library-first session factory/ownership model on top of the explicit lifecycle surface.
2. Add the first concrete built-in/local tools once that public construction story is stable.
3. Revisit agent-owned transcript persistence when non-Web UI consumers need durable session state.
4. Consider hook/extension and conversation-branching surfaces only after the core loop is robust.
5. Revisit context-window management with a stronger trigger or summarizer path once real conversation pressure justifies it.

## Recently completed

- `ChatSession` now flows caller cancellation through provider requests and tool execution.
- Abort behavior is now deterministic for provider waits and tool execution, including partial tool-result persistence when some tool work finished before cancellation.
- `ChatSession` now validates tool execution against its own configured tool catalog and no longer depends on `IToolRegistry` at runtime.
- `Mcp.Net.Agent.Tools` now includes `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor`.
- Focused tests now cover mixed local+MCP turns plus missing-session-tool failure semantics through the shared executor seam.
- `ChatSession` now rejects overlapping turns, exposes `IsProcessing` plus abort/wait lifecycle APIs, and blocks mutable state changes while a turn is active.

## Dependencies and risks

- Full MCP tool-call cancellation still depends on a `Mcp.Net.Client` seam because `IMcpClient.CallTool` does not yet accept a `CancellationToken`.
- The provider boundary should remain snapshot-based; the runtime should not reintroduce provider-owned conversation state.
- Factory-created runtime dependencies need an explicit ownership model so cleanup responsibilities stay clear.
- The current compaction trigger is intentionally simple; future pressure may require token-aware estimation or a stronger summarizer path.

## Open questions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project as soon as the contracts land?
- Should local tools always be app-owned reusable registrations, or should the runtime explicitly support session-owned local tool instances from the start?
- Should the library expose both direct-session and owning-handle factory paths, or only one public construction pattern?
- Which concrete local tools are the best first proof of the seam once the public factory surface is in place?
- When context-window pressure grows further, should the next compaction improvement be token-aware estimation or provider-backed summarization?
