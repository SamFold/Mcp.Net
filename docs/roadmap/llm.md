# Roadmap: Mcp.Net.LLM

## Current focus

- `Mcp.Net.LLM` now has a multimodal baseline: typed user image input for OpenAI and Anthropic, plus OpenAI image-generation output through the Responses API.
- Keep the block-based transcript, replay, and streaming-update baseline stable while narrowing `Mcp.Net.LLM` to provider execution concerns.
- The stateless-executor boundary shift is now live: `ChatSession` owns prompt/tools/transcript state and `Mcp.Net.LLM` executes explicit request snapshots.
- `Mcp.Net.LLM` is now a standalone provider library: MCP-facing prompt/resource catalog, completion, elicitation, and tool-result conversion all live outside the project.
- The async-stream transport slice is now complete: provider calls stream `ChatClientAssistantTurn` snapshots through a wrapper that also exposes the final `ChatClientTurnResult`.
- The request boundary cleanup is now complete for shared generation controls: agent/session defaults flow into `ChatRequestOptions`, and `ChatClientOptions` is back to long-lived client construction only.
- The first new shared request-time control is now in place: `ToolChoice` flows from agent/session defaults through `ChatRequestOptions`, maps across OpenAI and Anthropic, and keeps constructor types clean.
- The snapshot streaming decision is now closed for the current baseline: `ChatClientAssistantTurn` snapshots remain the provider payload, and richer event layers should be derived above that stream only if a future consumer forces the need.
- Cancellation remains deferred until after any later payload-shape work because the current MCP client/tool-execution path still does not support it cleanly.

## Near-term sequence

1. Decide whether to add OpenAI upload/file-id support so multimodal input and image output can be combined in one turn.
2. Decide whether Web UI/session transport should expose typed user multimodal content rather than only the plain-text compatibility projection.
3. Centralize lightweight model capability helpers only if the current ad hoc checks become a maintenance problem.
4. Revisit richer multimodal transport and cancellation only after the first slice remains stable in consumers.

## Completed boundary work

1. Extract `Mcp.Net.Agent` and move the agent domain, store, session, tool-registry, and related DI surfaces out of `Mcp.Net.LLM`.
2. Re-home the agent/session/tool tests to the new boundary and keep the existing Web UI adapter/controller/hub regressions green across the move.
3. Replace the temporary Web UI raw-client pass-through with explicit `ChatSession` / adapter operations for prompt update, conversation reset, and tool refresh.
4. Cut `IChatClient` over to a request-based `SendAsync(...)` boundary, make `ChatSession` the single owner of prompt/tool/transcript state, and remove the old MCP tool-model coupling from `Mcp.Net.LLM`.
5. Move MCP-backed prompt/resource catalog, completion, and elicitation services into `Mcp.Net.Agent`, repoint Web UI / console consumers, and remove the `Mcp.Net.Client` project reference from `Mcp.Net.LLM`.
6. Move MCP tool-result conversion into `Mcp.Net.Agent.Tools.ToolResultConverter`, trim `ToolInvocationResult` down to a pure LLM-local result type, and remove the final `Mcp.Net.Core` project reference from `Mcp.Net.LLM`.

## Next milestone

1. **Typed multimodal transport beyond the current compatibility layer.**
   - Keep snapshot streaming as the long-term payload for the current baseline.
   - Keep the shared transcript/provider boundary provider-neutral even though only OpenAI currently emits assistant image output.
   - Prefer small typed additions over provider-specific option bags or a wider transport rewrite.

## Recently completed

- Earlier boundary/parity work is complete: typed transcript entries and persistence rehydration, replay degradation rules, durable in-flight assistant updates, OpenAI/Anthropic streaming snapshot support, usage/stop-reason propagation, shared option handling, tool replacement coverage, and agent store/registry hardening.
- The first multimodal slice is now in place: `UserChatEntry` carries ordered text/image parts, replay drops assistant image blocks, OpenAI and Anthropic accept multimodal user input, and OpenAI image generation returns provider-neutral assistant image blocks.
- `ToolChoice` is now implemented as the first new shared request-time control: `Auto`, `None`, `Required`, and `Specific` flow through `ChatRequestOptions`, `ChatSession`, and both provider adapters, with Anthropic suppressing tools on shared `None`.
- Provider streaming now uses an async-stream wrapper rather than callback-based `IProgress<ChatClientAssistantTurn>`, with coverage for Anthropic mixed reasoning/text/tool-call streaming, `ChatSession` transcript upserts, and SignalR transcript update delivery.
- The `Mcp.Net.Agent` extraction is complete, including the Web UI seam cleanup that removed raw `IChatClient` reach-through for prompt update, conversation reset, and tool refresh.
- The provider-boundary decision is now implemented: `IChatClient` is request-based, `ChatSession` owns prompt/tool/transcript state, provider clients rebuild provider payloads from explicit request snapshots, and the old MCP tool-model coupling has been removed from `Mcp.Net.LLM`.
- MCP-backed prompt/resource catalog, completion, elicitation, and tool-result conversion now all live outside `Mcp.Net.LLM`, and the project now builds as a standalone provider library with no project references.

## Dependencies and risks

- Session-level cancellation remains cross-project work because `IMcpClient.CallTool` currently exposes no `CancellationToken`; it stays out of scope until after the request-defaults cleanup and any later payload-shape work and only comes back when the seam is both needed and worth widening.
- The multimodal slice intentionally reopens part of the transcript model; keep follow-on work constrained to typed user transport and OpenAI upload/reference support rather than a broader content-system rewrite.

## Open questions

- How far should shared request-time execution options go before provider-specific option types or nested provider option bags are warranted?
- Should the LLM-local tool definition live directly in `Mcp.Net.LLM` or in a thinner shared contract package if other projects need provider access without taking an agent dependency?
- Where should `ChatTranscriptEntry` live after the agent extraction — in `Mcp.Net.LLM` (current plan, since replay owns the types) or in a shared contracts package?
