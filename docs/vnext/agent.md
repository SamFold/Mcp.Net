# VNext: Mcp.Net.Agent

## Current status

- `ChatSession` now owns prompt, transcript, request-default, and provider-facing tool state for the agent loop.
- The provider boundary is clean: `ChatSession` builds `ChatClientRequest` snapshots and `Mcp.Net.LLM` executes them through `IChatClient.SendAsync(...)`.
- `ChatSession` now validates tool execution against its own session-owned tool catalog; `IToolRegistry` is no longer a runtime dependency inside the loop.
- Tool execution now sits behind `IToolExecutor`, with `McpToolExecutor`, `LocalToolExecutor`, and `CompositeToolExecutor` available under `Mcp.Net.Agent.Tools`.
- Mixed local+MCP turns are now covered through the shared executor seam, and missing-session-tool failures are enforced from the session-owned catalog.
- `SendUserMessageAsync(...)` now accepts a `CancellationToken` and flows it through provider requests plus tool execution.
- Provider-wait cancellation and tool-execution cancellation are now deterministic, including partial tool-result persistence when some tool work finished before the turn was canceled.

## Goal

- Add the first concrete built-in/local tools on top of the now-stable session-owned catalog, composite executor graph, and cancellation-aware runtime seam.

## What

- Add the first real `ILocalTool` implementations that the LLM can see and the runtime can execute in-process.
- Keep composition outside `ChatSession`: build local descriptors and local executor registrations from the same tool objects, then merge them with MCP descriptors before session creation.
- Prove end-to-end behavior with real local tools rather than only test doubles.
- Keep the new cancellation-token flow intact so local tool execution can be interrupted cleanly.

## Why

- The runtime seams are now in place and tested: session-owned tool catalogs, a backend-agnostic executor graph, and token-driven cancellation through the active turn.
- Until concrete local tools exist, the new local/composite execution design is still only infrastructure.
- The next slice should validate the abstractions against real in-process behavior before adding convenience APIs like `AbortCurrentTurn()`.

## How

### 1. Pick the first concrete local tools deliberately

- Start with tools that are useful enough to validate the runtime contracts but small enough to keep the slice tight.
- Keep implementations cancellation-aware from the start by honoring the existing `CancellationToken` on `ILocalTool.ExecuteAsync(...)`.
- Avoid broad unfinished tool shells; prove one coherent vertical slice with a minimal set of real capabilities.

### 2. Keep composition external

- Build local tool descriptors and local executor registrations from the same concrete tool objects.
- Merge those descriptors with any MCP-discovered descriptors before the session is created.
- Keep `ChatSession` unaware of which tools are local versus MCP-backed.

### 3. Prove the real path

- Add focused tests for the first concrete local tools.
- Add at least one runtime-level regression showing a real local tool exposed through session configuration and executed through the existing executor graph.
- Keep cancellation behavior covered for local tools so the new slice does not regress the token-based abort work.

## Target shape

```csharp
var localTools = new ILocalTool[]
{
    new ExampleLocalTool()
};

var sessionTools =
    mcpTools
        .Concat(localTools.Select(tool => tool.Descriptor))
        .ToArray();

var localExecutor = new LocalToolExecutor(localTools);
var mcpExecutor = new McpToolExecutor(sessionMcpClient, logger);
var toolExecutor = new CompositeToolExecutor(localExecutor, mcpExecutor);

var session = new ChatSession(
    chatClient,
    toolExecutor,
    logger,
    new ChatSessionConfiguration
    {
        Tools = sessionTools,
        SystemPrompt = systemPrompt,
        RequestDefaults = requestDefaults
    });
```

## Scope

- In scope:
  - add the first concrete built-in/local tools
  - compose those tools into the existing local/composite executor path
  - add focused tests for real local tool behavior plus runtime integration coverage
  - preserve the existing cancellation-token flow through tool execution
- Out of scope:
  - an explicit `AbortCurrentTurn()` convenience wrapper unless a consumer actually needs it
  - full MCP tool-call cancellation while `IMcpClient.CallTool(...)` lacks `CancellationToken`
  - transcript persistence changes
  - changes to the provider request/stream contract beyond the current cancellation flow

## Current slice

1. Choose the first concrete local tools that are small enough for one vertical slice.
2. Implement them as `ILocalTool` instances with provider-facing descriptors and cancellation-aware execution.
3. Compose them into the existing local/composite executor path without reopening `ChatSession`.
4. Add focused tool tests plus runtime coverage proving real local tool execution through the existing session loop.
5. Keep the new tools aligned with the current cancellation-token abort semantics.

## Next slices

1. Add an explicit `AbortCurrentTurn()` wrapper only if a real consumer needs a session-owned convenience API on top of the current token-based flow.
2. Revisit session-owned transcript persistence when non-Web UI consumers need durable conversation state.
3. Consider hook/extension or branching surfaces only after the core loop is more robust.
4. Revisit context-window management with stronger token-aware triggers or summarizer-backed compaction only when real pressure justifies it.

## Recently completed

- Added cancellation-token flow through `SendUserMessageAsync(...)`, provider requests, and tool execution in `ChatSession`.
- Defined deterministic abort behavior for provider waits and tool execution, including partial tool-result persistence for work that finished before cancellation.
- Removed `IToolRegistry` from `ChatSession` and made the session-owned tool catalog the sole validation source inside the loop.
- Added `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor` under `Mcp.Net.Agent.Tools`.
- Added focused executor tests and `ChatSession` regressions covering missing-session-tool failures plus mixed local+MCP turns through one executor graph.

## Open decisions

- Should concrete built-in tools live under `Mcp.Net.Agent` temporarily, or should the repo create a dedicated `Mcp.Net.Tools` project immediately?
- When the first concrete built-in tools arrive, should they be session-scoped objects, app-scoped singletons, or a mix depending on tool capabilities?
- Which concrete tools are the best first proof of the local-tool seam?
- Should a later `AbortCurrentTurn()` API exist as a convenience wrapper once a real consumer asks for it?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Run focused tests for the concrete local tools first.
- Verify the local tools execute through the existing composite executor/runtime path without reopening `ChatSession`.
- Keep cancellation behavior covered for local tool execution.
- Run broader `Mcp.Net.Tests.Agent` coverage after the focused pass is green.
