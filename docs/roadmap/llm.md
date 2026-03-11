# Roadmap: Mcp.Net.LLM

## Current focus

- Keep the block-based transcript, replay, and streaming-update baseline stable while narrowing `Mcp.Net.LLM` to provider execution concerns.
- The stateless-executor boundary shift is now live: `ChatSession` owns prompt/tools/transcript state and `Mcp.Net.LLM` executes explicit request snapshots.
- `Mcp.Net.LLM` is now a standalone provider library: MCP-facing prompt/resource catalog, completion, elicitation, and tool-result conversion all live outside the project.
- Cancellation remains deferred until after the next API-shape decision because the current MCP client/tool-execution path still does not support it cleanly.

## Near-term sequence

1. Revisit whether snapshot-based `IProgress<ChatClientAssistantTurn>` updates should remain or move to a breaking `IAsyncEnumerable<T>` surface after the provider boundary settles.
2. Revisit session cancellation only after the provider-boundary shift, and only if the MCP client/tool-execution path then needs a clean contract.
3. Revisit whether shared `ChatClientOptions` should stay flat or start splitting into provider-specific option shapes as more provider-specific knobs appear.

## Completed boundary work

1. Extract `Mcp.Net.Agent` and move the agent domain, store, session, tool-registry, and related DI surfaces out of `Mcp.Net.LLM`.
2. Re-home the agent/session/tool tests to the new boundary and keep the existing Web UI adapter/controller/hub regressions green across the move.
3. Replace the temporary Web UI raw-client pass-through with explicit `ChatSession` / adapter operations for prompt update, conversation reset, and tool refresh.
4. Cut `IChatClient` over to a request-based `SendAsync(...)` boundary, make `ChatSession` the single owner of prompt/tool/transcript state, and remove the old MCP tool-model coupling from `Mcp.Net.LLM`.
5. Move MCP-backed prompt/resource catalog, completion, and elicitation services into `Mcp.Net.Agent`, repoint Web UI / console consumers, and remove the `Mcp.Net.Client` project reference from `Mcp.Net.LLM`.
6. Move MCP tool-result conversion into `Mcp.Net.Agent.Tools.ToolResultConverter`, trim `ToolInvocationResult` down to a pure LLM-local result type, and remove the final `Mcp.Net.Core` project reference from `Mcp.Net.LLM`.

## Next milestone

1. **Converge `Mcp.Net.LLM` on a pure provider boundary.** Now that the `Mcp.Net.Agent` extraction, Web UI seam cleanup, and request-based `IChatClient` cutover are complete, finish the second half of the separation:
   - `Mcp.Net.LLM` becomes a pure, reusable provider-execution library: provider request/response contracts, provider implementations, replay/history transforms, and API-key/provider-factory concerns.
   - `Mcp.Net.Agent` owns conversation state and MCP-backed session helpers: `ChatSession`, prompt/tool/transcript state, prompt/resource catalog, completion lookups, elicitation coordination, and tool execution.
   - `Mcp.Net.LLM` should no longer depend on MCP client abstractions or MCP tool models once the migration finishes.
   - Keep the transcript and replay architecture stable while the boundary shifts.

## Recently completed

- Provider request builders now honor shared prompt/options inputs, nested tool arguments preserve structured values through both provider adapters, and provider `System` and `Error` responses surface through `ChatSession`.
- The transcript architecture has shifted to typed `ChatTranscriptEntry` records with block-based assistant content, provider-agnostic replay/history transforms, transcript persistence rehydration, and Web UI discriminated transcript DTOs.
- Anthropic reasoning capture and replay are in place, including visibility-aware degradation for missing signatures and captured probe-based regression coverage for same-provider and cross-provider replay cases.
- `IChatClient`, `ChatSession`, SignalR delivery, and transcript persistence now support durable in-flight assistant updates keyed by stable transcript `Id`.
- OpenAI streaming is now wired into that update seam, including partial text and tool-call reconstruction with stable assistant and block identifiers.
- Anthropic streaming is now wired into that update seam too, including progressive reasoning, text, and tool-use snapshots plus regression coverage proving post-stream tool follow-ups still replay the final assistant turn correctly.
- `Usage` and `StopReason` now propagate through `ChatClientAssistantTurn`, assistant transcript entries, persistence, and Web UI assistant DTOs for both OpenAI and Anthropic, including streaming final-snapshot metadata updates.
- `ChatClientOptions` now carries shared `MaxOutputTokens`, existing agent/Web UI `max_tokens` intent reaches provider request builders, Anthropic honors shared `Temperature`, and blank `SystemPrompt` no longer injects adapter-owned prompt copy.
- Tool replacement scenarios are covered on both OpenAI and Anthropic, and the request-based boundary now keeps the active tool set explicit on each provider call.
- `AgentManager.CloneAgentAsync` now surfaces persistence failures instead of returning a phantom clone, with a regression covering the failed `RegisterAgentAsync` path.
- `AgentRegistry` now blocks public cache access on initialization, can recover from a failed first reload via manual `ReloadAgentsAsync`, and preserves immediate `userId` validation on register/update paths.
- Persisted agent settings now round-trip through the real `FileSystemAgentStore` path into `AgentFactory` `ChatClientOptions`, with regression coverage for deserialized `JsonElement` temperature and `max_tokens` values.
- The `Mcp.Net.Agent` extraction is complete, including the Web UI seam cleanup that removed raw `IChatClient` reach-through for prompt update, conversation reset, and tool refresh.
- The provider-boundary decision is now implemented: `IChatClient` is request-based, `ChatSession` owns prompt/tool/transcript state, provider clients rebuild provider payloads from explicit request snapshots, and the old MCP tool-model coupling has been removed from `Mcp.Net.LLM`.
- MCP-backed prompt/resource catalog, completion, elicitation, and tool-result conversion now all live outside `Mcp.Net.LLM`, and the project now builds as a standalone provider library with no project references.

## Dependencies and risks

- Session-level cancellation remains cross-project work because `IMcpClient.CallTool` currently exposes no `CancellationToken`; it stays out of scope until after the provider-boundary shift and only comes back when the seam is both needed and worth widening.
- The next LLM slices should stay focused on API shape and avoid reopening another transcript or message-model rewrite now that the project boundary work is complete.

## Open questions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- Should the LLM-local tool definition live directly in `Mcp.Net.LLM` or in a thinner shared contract package if other projects need provider access without taking an agent dependency?
- Where should `ChatTranscriptEntry` live after the agent extraction — in `Mcp.Net.LLM` (current plan, since replay owns the types) or in a shared contracts package?
