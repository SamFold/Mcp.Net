# Roadmap: Mcp.Net.LLM

## Current focus

- Keep the block-based transcript, replay, and streaming-update baseline stable while narrowing `Mcp.Net.LLM` to provider execution concerns.
- The stateless-executor boundary shift is now live: `ChatSession` owns prompt/tools/transcript state and `Mcp.Net.LLM` executes explicit request snapshots.
- `Mcp.Net.LLM` is now a standalone provider library: MCP-facing prompt/resource catalog, completion, elicitation, and tool-result conversion all live outside the project.
- The async-stream transport slice is now complete: provider calls stream `ChatClientAssistantTurn` snapshots through a wrapper that also exposes the final `ChatClientTurnResult`.
- The request boundary now includes a typed `ChatRequestOptions` seam: the immediate priority is moving existing shared generation controls onto request execution before adding new provider knobs.
- OpenAI and Anthropic now honor request-time `Temperature` and `MaxOutputTokens`, while constructor-level defaults remain only as a temporary compatibility fallback.
- Cancellation remains deferred until after the request-options and payload-shape work because the current MCP client/tool-execution path still does not support it cleanly.

## Near-term sequence

1. Follow with typed agent/session defaults instead of raw parameter dictionaries, while keeping a brief compatibility bridge for existing agent metadata.
2. Remove the temporary constructor fallback so `ChatClientOptions` shrinks back to long-lived client configuration only.
3. Revisit whether a richer streaming event model is worth introducing, or whether `ChatClientAssistantTurn` snapshots remain sufficient on the new async-stream transport.
4. Revisit session cancellation only after the request-options and payload-shape decisions, and only if the MCP client/tool-execution path then needs a clean contract.

## Completed boundary work

1. Extract `Mcp.Net.Agent` and move the agent domain, store, session, tool-registry, and related DI surfaces out of `Mcp.Net.LLM`.
2. Re-home the agent/session/tool tests to the new boundary and keep the existing Web UI adapter/controller/hub regressions green across the move.
3. Replace the temporary Web UI raw-client pass-through with explicit `ChatSession` / adapter operations for prompt update, conversation reset, and tool refresh.
4. Cut `IChatClient` over to a request-based `SendAsync(...)` boundary, make `ChatSession` the single owner of prompt/tool/transcript state, and remove the old MCP tool-model coupling from `Mcp.Net.LLM`.
5. Move MCP-backed prompt/resource catalog, completion, and elicitation services into `Mcp.Net.Agent`, repoint Web UI / console consumers, and remove the `Mcp.Net.Client` project reference from `Mcp.Net.LLM`.
6. Move MCP tool-result conversion into `Mcp.Net.Agent.Tools.ToolResultConverter`, trim `ToolInvocationResult` down to a pure LLM-local result type, and remove the final `Mcp.Net.Core` project reference from `Mcp.Net.LLM`.

## Next milestone

1. **Move execution defaults fully onto the request/session boundary.**
   - Keep `ApiKey` and model selection as client-construction concerns.
   - Replace raw agent parameter dictionary lookups with typed defaults that flow through `ChatSession.BuildRequest()`.
   - Keep a brief compatibility bridge while migrating existing agent metadata.

2. **Remove the constructor fallback after request-owned defaults are live.**
   - Shrink `ChatClientOptions` back to long-lived client configuration only.
   - Keep shared generation controls on request state so future tool-choice, reasoning, or cache controls land on the right seam.
   - Avoid exposing provider-specific public option types unless shared capability-oriented options prove insufficient.

3. **Then decide whether the async-stream payload should stay snapshot-based.**
   - Keep the new wrapper stable unless a concrete consumer need justifies a richer event model.
   - Preserve stable assistant/block identifiers, transcript `Added` / `Updated` semantics, and final-result access while evaluating any payload changes.
   - Do not reopen another transport rewrite while this decision remains open.

## Recently completed

- Earlier boundary/parity work is complete: typed transcript entries and persistence rehydration, replay degradation rules, durable in-flight assistant updates, OpenAI/Anthropic streaming snapshot support, usage/stop-reason propagation, shared option handling, tool replacement coverage, and agent store/registry hardening.
- Provider streaming now uses an async-stream wrapper rather than callback-based `IProgress<ChatClientAssistantTurn>`, with coverage for Anthropic mixed reasoning/text/tool-call streaming, `ChatSession` transcript upserts, and SignalR transcript update delivery.
- The `Mcp.Net.Agent` extraction is complete, including the Web UI seam cleanup that removed raw `IChatClient` reach-through for prompt update, conversation reset, and tool refresh.
- The provider-boundary decision is now implemented: `IChatClient` is request-based, `ChatSession` owns prompt/tool/transcript state, provider clients rebuild provider payloads from explicit request snapshots, and the old MCP tool-model coupling has been removed from `Mcp.Net.LLM`.
- MCP-backed prompt/resource catalog, completion, elicitation, and tool-result conversion now all live outside `Mcp.Net.LLM`, and the project now builds as a standalone provider library with no project references.

## Dependencies and risks

- Session-level cancellation remains cross-project work because `IMcpClient.CallTool` currently exposes no `CancellationToken`; it stays out of scope until after the request-options and payload-shape work and only comes back when the seam is both needed and worth widening.
- The next LLM slices should stay focused on request/API shape and avoid reopening another transcript or message-model rewrite now that the project boundary work is complete.

## Open questions

- Should provider streaming stay on `ChatClientAssistantTurn` snapshots, or is there enough concrete pressure to justify a richer event model on top of the new async-stream transport?
- How far should shared request-time execution options go before provider-specific option types or nested provider option bags are warranted?
- Should the LLM-local tool definition live directly in `Mcp.Net.LLM` or in a thinner shared contract package if other projects need provider access without taking an agent dependency?
- Where should `ChatTranscriptEntry` live after the agent extraction — in `Mcp.Net.LLM` (current plan, since replay owns the types) or in a shared contracts package?
