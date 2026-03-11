# Roadmap: Mcp.Net.LLM

## Current focus

- Keep the block-based transcript, replay, and streaming-update baseline stable while narrowing `Mcp.Net.LLM` to provider execution concerns.
- The stateless-executor boundary shift is now live: `ChatSession` owns prompt/tools/transcript state and `Mcp.Net.LLM` executes explicit request snapshots.
- `Mcp.Net.LLM` is now a standalone provider library: MCP-facing prompt/resource catalog, completion, elicitation, and tool-result conversion all live outside the project.
- The async-stream transport slice is now complete: provider calls stream `ChatClientAssistantTurn` snapshots through a wrapper that also exposes the final `ChatClientTurnResult`.
- The request boundary cleanup is now complete for shared generation controls: agent/session defaults flow into `ChatRequestOptions`, and `ChatClientOptions` is back to long-lived client construction only.
- The immediate priority is adding the first new shared request-time control on the clean seam rather than widening provider constructor types.
- Cancellation remains deferred until after any later payload-shape work because the current MCP client/tool-execution path still does not support it cleanly.

## Near-term sequence

1. Add `ToolChoice` as the first new shared request-time execution control across providers.
2. Revisit whether a richer streaming event model is worth introducing, or whether `ChatClientAssistantTurn` snapshots remain sufficient on the new async-stream transport.
3. Revisit session cancellation only after any later payload-shape decision, and only if the MCP client/tool-execution path then needs a clean contract.

## Completed boundary work

1. Extract `Mcp.Net.Agent` and move the agent domain, store, session, tool-registry, and related DI surfaces out of `Mcp.Net.LLM`.
2. Re-home the agent/session/tool tests to the new boundary and keep the existing Web UI adapter/controller/hub regressions green across the move.
3. Replace the temporary Web UI raw-client pass-through with explicit `ChatSession` / adapter operations for prompt update, conversation reset, and tool refresh.
4. Cut `IChatClient` over to a request-based `SendAsync(...)` boundary, make `ChatSession` the single owner of prompt/tool/transcript state, and remove the old MCP tool-model coupling from `Mcp.Net.LLM`.
5. Move MCP-backed prompt/resource catalog, completion, and elicitation services into `Mcp.Net.Agent`, repoint Web UI / console consumers, and remove the `Mcp.Net.Client` project reference from `Mcp.Net.LLM`.
6. Move MCP tool-result conversion into `Mcp.Net.Agent.Tools.ToolResultConverter`, trim `ToolInvocationResult` down to a pure LLM-local result type, and remove the final `Mcp.Net.Core` project reference from `Mcp.Net.LLM`.

## Next milestone

1. **Add `ToolChoice` on the cleaned request seam.**
   - Keep shared execution controls on `ChatRequestOptions` rather than on provider constructor types.
   - Map the new choice cleanly across OpenAI and Anthropic before considering narrower provider-specific knobs.
   - Preserve agent/session ownership of defaults and request building while introducing the new option.

2. **Then decide whether the async-stream payload should stay snapshot-based.**
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

- Session-level cancellation remains cross-project work because `IMcpClient.CallTool` currently exposes no `CancellationToken`; it stays out of scope until after the request-defaults cleanup and any later payload-shape work and only comes back when the seam is both needed and worth widening.
- The next LLM slices should stay focused on request/API shape and avoid reopening another transcript or message-model rewrite now that the project boundary work is complete.

## Open questions

- Should provider streaming stay on `ChatClientAssistantTurn` snapshots, or is there enough concrete pressure to justify a richer event model on top of the new async-stream transport?
- How far should shared request-time execution options go before provider-specific option types or nested provider option bags are warranted?
- Should the LLM-local tool definition live directly in `Mcp.Net.LLM` or in a thinner shared contract package if other projects need provider access without taking an agent dependency?
- Where should `ChatTranscriptEntry` live after the agent extraction — in `Mcp.Net.LLM` (current plan, since replay owns the types) or in a shared contracts package?
