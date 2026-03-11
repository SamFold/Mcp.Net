# LLM Provider Boundary

## Decision

- `Mcp.Net.LLM` should move toward a stateless provider-execution boundary.
- `Mcp.Net.Agent.ChatSession` should become the single owner of conversation state: system prompt, enabled tools, transcript, and replay/bootstrap inputs.
- Replay/history transformation should run at request-build time from session-owned state, not by preloading mutable provider client history.
- MCP-facing prompt/resource catalog, completion, and elicitation helpers do not belong in `Mcp.Net.LLM`.
- `Mcp.Net.LLM` should stop depending on MCP tool models such as `Mcp.Net.Core.Models.Tools.Tool`; tool payloads should cross the provider boundary through an LLM-local request/tool contract instead.

## Target layering

- `Mcp.Net.LLM`
  - provider execution contracts
  - provider implementations
  - request/response models
  - streaming update models
  - replay/history transforms
  - API key and provider-factory concerns
- `Mcp.Net.Agent`
  - `ChatSession` state ownership
  - agent definitions and management
  - tool enablement and execution coordination
  - MCP-backed prompt/resource catalog
  - MCP-backed completion helpers
  - elicitation coordination
  - request building from transcript and session state
- `Mcp.Net.WebUi`
  - controllers, hubs, adapters, persistence, and composition

## Current status

The boundary cutover is now in place:

- `IChatClient` is request-based and executes explicit `ChatClientRequest` snapshots.
- `ChatSession` owns system prompt, registered tools, transcript state, reset behavior, and transcript bootstrap.
- provider adapters rebuild provider payloads from session-owned state at call time rather than preloading mutable provider history.
- MCP-backed prompt/resource catalog, completion, and elicitation helpers have moved to `Mcp.Net.Agent`.
- MCP tool-result conversion has moved to `Mcp.Net.Agent.Tools.ToolResultConverter`, and `ToolInvocationResult` is now a pure LLM-local result type.

The provider boundary is now clean: `Mcp.Net.LLM` is a standalone provider library with no project references and no MCP/session helper ownership.

## Directional shape

The target shape is closer to a provider executor:

```csharp
var request = new ChatRequestContext(
    systemPrompt,
    transcript,
    tools,
    pendingUserMessage,
    pendingToolResults);

var result = await chatClient.SendAsync(request, updates, cancellationToken);
```

In that model:

- `ChatSession` owns prompt, tools, transcript, and resume/reset behavior.
- `Mcp.Net.LLM` translates an explicit request into provider SDK calls.
- replay transforms run when building the provider request from transcript state.

## Immediate follow-on slices

1. Completed: introduce an explicit provider request/context contract in `Mcp.Net.LLM` and replace the old stateful `IChatClient` shape with a single request-based `SendAsync(...)` call.
2. Completed: move `PromptResourceCatalog`, `CompletionService`, `ElicitationCoordinator`, and related interfaces out of `Mcp.Net.LLM` to the agent/session side.
3. Completed: move MCP `ToolCallResult` / content-model conversion out of `ToolInvocationResult` so `Mcp.Net.LLM` no longer needs any project references.
4. Completed: remove the old MCP tool-model coupling from `Mcp.Net.LLM` by replacing `ToolConverter` and `Mcp.Net.Core.Models.Tools.Tool` usage with an LLM-local tool contract.
5. Next: revisit whether the provider streaming API should stay snapshot-based or move to a breaking `IAsyncEnumerable<T>` surface now that the boundary work is done.
