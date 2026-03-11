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

## Goal

- Add a library-first session factory and ownership model on top of the now-explicit `ChatSession` lifecycle contract.

## What

- Introduce a clean construction API for `ChatSession` that does not force consumers to hand-compose provider clients, executors, and configuration snapshots.
- Support both consumer-owned dependencies and factory-provisioned dependencies where the factory also owns cleanup.
- Keep `ChatSession` itself lean: the new factory should assemble runtime pieces, not absorb orchestration behavior into the session type.
- Preserve the existing session-owned lifecycle contract while making resource ownership explicit.

## Why

- The runtime now has the lifecycle primitives it was missing, but consumers still need too much internal knowledge to construct a session cleanly.
- Resource ownership is still implicit. In particular, factory-created MCP-backed sessions need an explicit cleanup model.
- The first concrete local tools will land more cleanly once the library has a consumer-facing session-construction surface.

## How

### 1. Add a library-first factory surface

- Introduce session-construction options that describe provider choice, request defaults, tools, and ownership mode without leaking current app wiring.
- Return a plain `ChatSession` when the consumer provides its own dependencies.
- Return an owning handle when the factory provisions disposable runtime resources.

### 2. Keep ownership explicit

- Make it obvious which path leaves cleanup to the consumer and which path makes the factory responsible for disposal.
- Keep `ChatSession` unaware of transport/resource ownership details.
- Avoid service-location-heavy APIs; prefer a factory service registered in DI.

### 3. Prove the consumer path

- Add focused tests for the new factory surface and ownership behavior.
- Keep the existing `ChatSession` lifecycle tests green so the new construction API layers on top of the runtime rather than reshaping it.
- Use the completed lifecycle contract as the stable base for later local-tool exposure.

## Target shape

```csharp
var options = new ChatSessionOptions
{
    Provider = LlmProvider.Anthropic,
    Model = "claude-sonnet-4-5-20250929",
    SystemPrompt = systemPrompt,
    RequestDefaults = requestDefaults,
    LocalTools = localTools,
    McpServerUrl = mcpServerUrl
};

var session = await chatSessionFactory.CreateAsync(options);

await using var ownedSession = await chatSessionFactory.CreateOwnedAsync(options);
var session = ownedSession.Session;
```

## Scope

- In scope:
  - add a library-first session factory surface
  - support both consumer-owned and factory-owned resource paths
  - add focused tests for construction and ownership behavior
  - preserve the completed `ChatSession` lifecycle contract
- Out of scope:
  - concrete built-in/local tool implementations
  - full MCP tool-call cancellation while `IMcpClient.CallTool(...)` lacks `CancellationToken`
  - transcript persistence changes
  - changes to the provider request/stream contract beyond the current lifecycle surface

## Current slice

1. Introduce a library-first session factory API on top of the explicit `ChatSession` lifecycle contract.
2. Support both consumer-owned runtime dependencies and factory-owned disposable resources.
3. Keep construction/configuration concerns outside `ChatSession`.
4. Add focused tests for the new factory/ownership surface without reopening provider or tool execution seams.

## Next slices

1. Add the first concrete built-in/local tools once the consumer-facing factory/ownership model is in place.
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

## Open decisions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project immediately?
- When the first concrete built-in tools arrive, should they be session-scoped objects, app-scoped singletons, or a mix depending on tool capabilities?
- Should the new session factory return `ChatSession` directly for consumer-owned dependencies, an owning handle for factory-owned dependencies, or both?
- Should the session factory expose a small runtime interface in addition to the concrete `ChatSession` type?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Keep the completed `ChatSession` lifecycle contract stable while adding the new factory/ownership layer.
- Verify cleanup ownership explicitly for any factory-provisioned resources.
- Run broader `Mcp.Net.Tests.Agent` coverage after the focused pass is green.
