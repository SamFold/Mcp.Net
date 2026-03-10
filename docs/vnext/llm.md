# VNext: Mcp.Net.LLM

## Current status

- Provider clients now honor `ChatClientOptions.SystemPrompt` during construction.
- The library-path agent system-prompt regression is covered at both the agent factory layer and the provider-request layer.
- Nested tool arguments now survive the OpenAI and Anthropic adapter paths as structured values, with server-binding compatibility coverage in place.
- `ChatSession` now surfaces provider `System` and `Error` responses through the existing assistant-message event flow on both initial and post-tool turns.
- `Mcp.Net.LLM` now builds and passes the focused LLM/Web UI slice on `OpenAI` `2.9.1` and `Anthropic.SDK` `5.10.0`.
- The standalone SDK probe now captures live OpenAI and Anthropic text, tool, reasoning, and error shapes under `artifacts/llm-probe/`.
- The `ChatSession` spec has been revised away from the earlier flat five-kind item proposal to a block-based transcript model:
  - transcript entries: `User`, `Assistant`, `ToolResult`, `Error`
  - assistant blocks: `Text`, `Reasoning`, `ToolCall`
  - typed replay/history transforms are now considered required architecture
- `Mcp.Net.LLM` now has a provider-agnostic transcript replay transformer with coverage for:
  - same-model opaque reasoning token preservation
  - same-provider cross-model reasoning degradation
  - cross-provider reasoning safety defaults
  - synthetic tool-result repair for unmatched assistant tool calls
- Persisted chat history now stores typed `ChatTranscriptEntry` records instead of flat `StoredChatMessage` rows.
- `ChatSession` resume now rehydrates both its in-memory transcript and provider client history through the replay transformer and provider-specific replay loaders.
- The Web UI adapter bootstrap path now loads persisted transcript before session start, stores all runtime transcript entries including `User`, and no longer injects a fake `system` history message on prompt updates.
- Anthropic assistant turns now capture `ThinkingContent` and `RedactedThinkingContent` into transcript reasoning blocks, and Anthropic replay uses visibility-aware fallbacks so missing signatures degrade to portable text instead of emitting invalid thinking payloads.
- OpenAI replay now rebuilds mixed assistant text-plus-tool-call history as a single assistant message via the SDK `ChatCompletion` mock factory instead of splitting it into two assistant messages.
- Replay/provider tests now use the captured Anthropic reasoning probe fixture for same-provider cross-model degradation, cross-provider handoff safety, and Anthropic thinking round-trips.
- The remaining Web UI chat transport now uses discriminated transcript-entry DTOs for REST history and `ReceiveMessage`, and the controller send-message route now takes a dedicated user-message request DTO instead of the old flat `ChatMessageDto`.
- `IChatClient` and `ChatSession` now expose a typed assistant-turn update seam for streaming: one in-flight assistant transcript entry is updated in place by transcript `Id`, SignalR emits `UpdateMessage` for durable transcript updates, and persisted transcript storage now upserts updated entries instead of appending duplicates.
- The OpenAI chat adapter now uses the SDK streaming chat-completions API when assistant-turn updates are requested, emitting progressive text and tool-call snapshots while preserving stable transcript and block identifiers across partial and final turns.
- The Anthropic chat adapter now uses the SDK streaming messages API when assistant-turn updates are requested, emitting progressive reasoning, text, and tool-use snapshots while preserving stable transcript and block identifiers across partial and final turns.
- Stable assistant and block identifiers are now considered required behavior for transcript upserts and incremental Web UI updates, not an optional implementation detail.
- `ChatClientAssistantTurn`, `AssistantChatEntry`, and the Web UI assistant transcript DTOs now carry explicit `Usage` and `StopReason`, and both OpenAI and Anthropic populate that metadata on non-streaming and streaming turns.
- The streaming slice keeps `ToolExecutionUpdated` and `ThinkingStateChanged` as separate ephemeral UI events for now; only durable assistant content flows through transcript `Added` and `Updated` events.
- `ChatClientOptions` now carries `MaxOutputTokens`, existing agent/Web UI `max_tokens` intent reaches OpenAI and Anthropic request builders, Anthropic honors the shared `Temperature`, and blank `SystemPrompt` no longer injects adapter-owned demo text.
- Provider `RegisterTools` is now idempotent (clear-and-replace) on both OpenAI and Anthropic, with regressions for repeated and replacement registration scenarios.
- `AgentRegistry` now awaits initialization before public cache access, preserves recovery via replaceable reload tasks, and rejects invalid register/update user IDs before initialization blocking.
- Persisted agent settings now round-trip through `FileSystemAgentStore` into `AgentFactory` `ChatClientOptions`, with regression coverage for `JsonElement` coercion of `temperature` and `max_tokens`.
- The 2026-03-08 LLM review follow-ons are now resolved end to end.
- The latest planning review for this lane concludes the `Mcp.Net.Agent` extraction does not need to wait on an `IChatClient` statefulness redesign: the provider clients can stay stateful for the split, and the statefulness decision can become a later internal `Mcp.Net.LLM` refactor.
- That same review also identified one extraction caveat: Web UI currently reaches through `ChatSession.GetLlmClient()` to call `SetSystemPrompt`, `GetSystemPrompt`, `ResetConversation`, and `RegisterTools`, so the split must either preserve that raw-client pass-through temporarily or move those operations onto an agent-facing session seam.
- `Mcp.Net.Agent` now exists as a standalone `net10.0` project in `Mcp.Net.sln`, depends on `Mcp.Net.LLM` and `Mcp.Net.Client`, and is referenced by `Mcp.Net.WebUi`, `Mcp.Net.Tests`, and `Mcp.Net.Examples.LLMConsole` so the extraction can proceed without another reference-graph change first.
- Cancellation is explicitly delayed until after the `Mcp.Net.Agent` extraction.
- The `Mcp.Net.Agent` extraction is complete. All six slices (bootstrap, domain/service layer, session/history/events, tool inventory/categorization, Web UI seam verification, test re-homing) have landed. All 369 tests pass. `Mcp.Net.LLM` now contains only provider clients, replay, content models, API keys, completions, catalog, elicitation, and `ToolConverter`.

## Goal

- Complete provider capability parity on top of the new block-based transcript, replay, and streaming-update architecture before revisiting larger API-shape changes.
- Keep the completed provider-parity and review-follow-on work stable while extracting `Mcp.Net.Agent` from `Mcp.Net.LLM` behind the current `IChatClient` boundary, and continue deferring cancellation until after that split.

## Scope

- In scope:
  - extract `ChatSession`, agent definitions/management/registry/store, session events, history/session metadata, and tool orchestration into `Mcp.Net.Agent`
  - keep `IChatClient`, provider implementations, `ChatClientOptions`, turn results, content blocks, usage, `ChatTranscriptEntry`, replay types, and provider factory in `Mcp.Net.LLM`
  - preserve the current stateful `IChatClient` contract during the split
  - decide how to handle the current Web UI raw-client pass-through (`GetSystemPrompt`, `SetSystemPrompt`, `ResetConversation`, `RegisterTools`) during the extraction
  - keep the replay/history transformer and block-based transcript architecture stable while the extraction lands
- Out of scope:
  - session-level cancellation until after the `Mcp.Net.Agent` extraction
  - an immediate breaking rewrite of `IChatClient` into a context-driven request API
  - another full transcript or message-model rewrite
  - low-level memory or serialization optimization work
  - broad provider-helper extraction beyond changes needed for the active slices

## Current slice

The `Mcp.Net.Agent` extraction is complete. All six concrete extraction slices have landed successfully. The next work items are from the "Next slices" section below.

## Concrete extraction slices

1. **Completed: bootstrap the new project and references**
   - Create `Mcp.Net.Agent/Mcp.Net.Agent.csproj` targeting `net10.0`.
   - Add `Mcp.Net.Agent` to `Mcp.Net.sln`.
   - Add project references from `Mcp.Net.WebUi/Mcp.Net.WebUi.csproj`, `Mcp.Net.Tests/Mcp.Net.Tests.csproj`, and `Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj` to `Mcp.Net.Agent`.
   - Keep `Mcp.Net.Agent` depending on `Mcp.Net.LLM` and `Mcp.Net.Client`; do not introduce a reverse dependency from `Mcp.Net.LLM`.
   - Do not split the Web UI tool inventory into separate concrete `ToolRegistry` instances while the DI extension and startup registrations still straddle the old and new project boundaries.

2. **Completed: move the agent domain and service layer as one slice**
   - Move to `Mcp.Net.Agent.Models`:
     - `Mcp.Net.LLM/Models/AgentDefinition.cs`
     - `Mcp.Net.LLM/Models/AgentCategory.cs`
   - Move to `Mcp.Net.Agent.Interfaces`:
     - `Mcp.Net.LLM/Interfaces/IAgentFactory.cs`
     - `Mcp.Net.LLM/Interfaces/IAgentManager.cs`
     - `Mcp.Net.LLM/Interfaces/IAgentRegistry.cs`
     - `Mcp.Net.LLM/Interfaces/IAgentStore.cs`
   - Move to `Mcp.Net.Agent.Agents`:
     - `Mcp.Net.LLM/Agents/AgentFactory.cs`
     - `Mcp.Net.LLM/Agents/AgentManager.cs`
     - `Mcp.Net.LLM/Agents/AgentRegistry.cs`
     - `Mcp.Net.LLM/Agents/DefaultAgentManager.cs`
     - `Mcp.Net.LLM/Agents/AgentExtensions.cs`
     - `Mcp.Net.LLM/Agents/Stores/FileSystemAgentStore.cs`
     - `Mcp.Net.LLM/Agents/Stores/InMemoryAgentStore.cs`
   - Move to `Mcp.Net.Agent.Extensions`:
     - `Mcp.Net.LLM/Extensions/AgentServiceCollectionExtensions.cs`
   - Keep the existing public `AddAgentServices`, `AddFileSystemAgentStore`, and `AddInMemoryAgentStore` entry points unchanged on the first pass so the Web UI startup diff stays mechanical.

3. **Completed: Move session, history, and session-event orchestration together**
   - Move to `Mcp.Net.Agent.Core`:
     - `Mcp.Net.LLM/Core/ChatSession.cs`
   - Move to `Mcp.Net.Agent.Interfaces`:
     - `Mcp.Net.LLM/Interfaces/IChatSessionEvents.cs`
     - `Mcp.Net.LLM/Interfaces/IChatHistoryManager.cs`
   - Move to `Mcp.Net.Agent.Events`:
     - `Mcp.Net.LLM/Events/ChatSessionEventArgs.cs`
   - Move to `Mcp.Net.Agent.Models`:
     - `Mcp.Net.LLM/Models/ChatSessionMetadata.cs`
   - Move to `Mcp.Net.Agent.Extensions`:
     - `Mcp.Net.LLM/Extensions/AgentExtensions.cs`
   - Keep `ChatTranscriptEntry`, assistant block types, turn results, usage, replay types, and `IChatTranscriptReplayTransformer` in `Mcp.Net.LLM`; `ChatSession` should reference them across the project boundary rather than re-home them in the same slice.

4. **Completed: Move tool inventory and categorization with agent orchestration**
   - Move to `Mcp.Net.Agent.Tools`:
     - `Mcp.Net.LLM/Interfaces/IToolsRegistry.cs`
     - `Mcp.Net.LLM/Tools/ToolRegistry.cs`
     - `Mcp.Net.LLM/Tools/ToolCategoryCatalog.cs`
     - `Mcp.Net.LLM/Tools/ToolCategoryDescriptor.cs`
     - `Mcp.Net.LLM/Tools/ToolCategoryMetadataParser.cs`
     - `Mcp.Net.LLM/Tools/ToolNameClassifier.cs`
   - This keeps tool discovery, enablement, categorization, and execution lookup on the agent/session side while leaving provider clients in `Mcp.Net.LLM` responsible only for native tool registration payloads.

5. **Completed: Preserve the Web UI seam during the move; do not redesign it in the same slice**
   - Keep `ChatSession.GetLlmClient()` and `ISignalRChatAdapter.GetLlmClient()` temporarily so these existing callers continue to work during extraction:
     - `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs`
     - `Mcp.Net.WebUi/Controllers/ChatController.cs`
     - `Mcp.Net.WebUi/Hubs/ChatHub.cs`
   - Repoint Web UI consumers of moved types to `Mcp.Net.Agent` namespaces without changing behavior:
     - `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
     - `Mcp.Net.WebUi/Adapters/Interfaces/ISignalRChatAdapter.cs`
     - `Mcp.Net.WebUi/Controllers/AgentsController.cs`
     - `Mcp.Net.WebUi/Controllers/ToolsController.cs`
     - `Mcp.Net.WebUi/Infrastructure/DefaultAgentInitializer.cs`
     - `Mcp.Net.WebUi/Infrastructure/Persistence/InMemoryChatHistoryManager.cs`
     - `Mcp.Net.WebUi/DTOs/CreateAgentDto.cs`
     - `Mcp.Net.WebUi/DTOs/UpdateAgentDto.cs`
     - `Mcp.Net.WebUi/DTOs/AgentSummaryDto.cs`
     - `Mcp.Net.WebUi/DTOs/AgentDetailsDto.cs`
   - Leave `ChatFactory` in `Mcp.Net.WebUi` for the first extraction pass even though it still contains session/client creation logic; avoid mixing that refactor into the project split.

6. **Completed: Re-home and widen the verification scope in the same order as the code moves**
   - Agent/session/tool tests that should move or be renamed to the new project boundary:
     - `Mcp.Net.Tests/LLM/Agents/AgentFactoryTests.cs`
     - `Mcp.Net.Tests/LLM/Agents/AgentManagerTests.cs`
     - `Mcp.Net.Tests/LLM/Agents/AgentRegistryTests.cs`
     - `Mcp.Net.Tests/LLM/Agents/AgentExtensionsTests.cs`
     - `Mcp.Net.Tests/LLM/Core/ChatSessionTests.cs`
     - `Mcp.Net.Tests/LLM/Extensions/AgentServiceExtensionsTests.cs`
     - `Mcp.Net.Tests/LLM/Tools/ToolRegistryTests.cs`
   - Web UI regression coverage that must keep passing across the new boundary:
     - `Mcp.Net.Tests/WebUi/Chat/ChatFactoryTests.cs`
     - `Mcp.Net.Tests/WebUi/Adapters/SignalR/SignalRChatAdapterTests.cs`
     - `Mcp.Net.Tests/WebUi/Controllers/ChatControllerTests.cs`
     - `Mcp.Net.Tests/WebUi/Hubs/ChatHubTests.cs`
     - `Mcp.Net.Tests/WebUi/Hubs/ChatHubHistoryLoadingTests.cs`
     - `Mcp.Net.Tests/WebUi/Infrastructure/InMemoryChatHistoryManagerTests.cs`

## Stay in LLM

- Keep these in `Mcp.Net.LLM` for the extraction:
  - `Mcp.Net.LLM/Interfaces/IChatClient.cs`
  - `Mcp.Net.LLM/Interfaces/IChatClientFactory.cs`
  - `Mcp.Net.LLM/Interfaces/IApiKeyProvider.cs`
  - `Mcp.Net.LLM/Interfaces/IUserApiKeyProvider.cs`
  - `Mcp.Net.LLM/Interfaces/IChatTranscriptReplayTransformer.cs`
  - `Mcp.Net.LLM/Models/ChatClientOptions.cs`
  - `Mcp.Net.LLM/Models/ChatClientTurnResult.cs`
  - `Mcp.Net.LLM/Models/AssistantContentBlock.cs`
  - `Mcp.Net.LLM/Models/ChatUsage.cs`
  - `Mcp.Net.LLM/Models/ChatTranscriptEntry.cs`
  - `Mcp.Net.LLM/Models/ToolInvocation.cs`
  - `Mcp.Net.LLM/Models/ToolInvocationResult.cs`
  - `Mcp.Net.LLM/Models/ToolResultResourceLink.cs`
  - `Mcp.Net.LLM/Models/LLMProvider.cs`
  - `Mcp.Net.LLM/Models/Exceptions/ToolNotFoundException.cs`
  - `Mcp.Net.LLM/Replay/*`
  - `Mcp.Net.LLM/OpenAI/*`
  - `Mcp.Net.LLM/Anthropic/*`
  - `Mcp.Net.LLM/Factories/ChatClientFactory.cs`
  - `Mcp.Net.LLM/ApiKeys/*`
  - `Mcp.Net.LLM/Catalog/*`
  - `Mcp.Net.LLM/Completions/*`
  - `Mcp.Net.LLM/Elicitation/*`
- The extraction should leave `Mcp.Net.LLM` as the provider SDK, replay, prompt/resource, completion, and API-key layer rather than a second home for session orchestration.

## Next slices

1. Revisit whether `IChatClient` should remain conversation-stateful or move to an explicit context-driven request API once the extraction is complete.
2. Revisit whether `IChatClient` should expose a breaking `IAsyncEnumerable<T>` stream surface after the client-state decision is settled.
3. Revisit a typed activity DTO family only if the existing ephemeral `ToolExecutionUpdated` and `ThinkingStateChanged` events prove too weak once provider streaming is live.
4. Revisit session cancellation only after the `Mcp.Net.Agent` extraction and only if the MCP client/tool-execution path then needs a clean cancellation seam.

## Open decisions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- After the `Mcp.Net.Agent` extraction, what seam should the MCP client expose so agent/session orchestration can cancel tool execution without inventing provider-specific escape hatches? Note: `IMcpClient.CallTool` currently accepts no `CancellationToken`, so cancellation still requires an MCP client contract change, not just a session-layer parameter addition.
- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?
- Where should `ChatTranscriptEntry` live after the `Mcp.Net.Agent` extraction — it is the shared contract between replay (provider layer) and session persistence (agent layer)? Current plan keeps it in `Mcp.Net.LLM` since replay owns the type definitions.

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Keep the current `IChatClient` stateful contract stable through the extraction.
- Preserve or intentionally replace the current Web UI raw-client pass-through behavior (`GetSystemPrompt`, `SetSystemPrompt`, `ResetConversation`, `RegisterTools`) during the split.
- Verify transcript load/replay, provider history, and tool registration behavior still hold across the new `Mcp.Net.Agent` / `Mcp.Net.LLM` boundary.
- Add regressions for Anthropic ordered assistant blocks containing reasoning, text, and tool calls under streaming updates.
- Add regressions for stable transcript and block identifiers across partial and final assistant updates.
- Add regressions for tool execution appending `ToolResult` entries instead of mutating transcript state.
- Add regressions for provider failures surfacing as `Error` entries instead of assistant text.
- Add replay/history transform coverage for same-model preservation and degraded cross-model/provider replay.
