# VNext: Mcp.Net.LLM

## Current status

- The block-based transcript, replay, and streaming-update architecture is the current baseline:
  - transcript entries: `User`, `Assistant`, `ToolResult`, `Error`
  - assistant blocks: `Text`, `Reasoning`, `ToolCall`
  - typed replay/history transforms remain required architecture
- Provider adapters preserve structured tool arguments, stream durable assistant updates keyed by stable transcript/block identifiers, and populate `Usage` / `StopReason` metadata on final assistant turns.
- `Mcp.Net.Agent` owns the agent/session runtime split: agent definitions, stores, session orchestration, tool inventory, and Web UI session-facing seams all live outside `Mcp.Net.LLM`.
- `IChatClient` is now a request-based provider boundary with a single `SendAsync(ChatClientRequest, ...)` call.
- `ChatSession` now owns system prompt, registered tools, transcript state, reset behavior, and transcript bootstrap, then builds provider requests from that state for each turn.
- `Mcp.Net.LLM` no longer depends on the MCP tool model or `ToolConverter`; provider tool payloads now cross the boundary through the LLM-local request/tool contract.
- MCP-backed prompt/resource catalog, completion, and elicitation helpers now live in `Mcp.Net.Agent`, and `Mcp.Net.LLM` no longer depends on `Mcp.Net.Client`.
- MCP tool-result translation now lives in `Mcp.Net.Agent.Tools.ToolResultConverter`, `ToolInvocationResult` is a pure LLM-local data/serialization type, and `Mcp.Net.LLM` has no project references at all.

## Goal

- Keep the completed provider-parity, replay, and streaming-update work stable while converging `Mcp.Net.LLM` on a pure provider-execution boundary.
- Make `ChatSession` the single owner of prompt, tool, and transcript state, with replay/history transforms applied at request-build time instead of through mutable provider-client preload.
- Remove MCP/session-side helpers from `Mcp.Net.LLM` so the project becomes a provider/replay/API-key layer rather than a second home for MCP client orchestration.

## Scope

- In scope:
  - introduce an explicit provider request/context contract in `Mcp.Net.LLM`
  - move prompt, tool, and transcript ownership fully into `ChatSession` / `Mcp.Net.Agent`
  - keep provider implementations, request/response models, replay types, and provider factory in `Mcp.Net.LLM`
  - move MCP-backed prompt/resource catalog, completion, and elicitation services out of `Mcp.Net.LLM`
  - remove `Mcp.Net.Core.Models.Tools.Tool` coupling from the `Mcp.Net.LLM` provider boundary
  - revisit whether the provider streaming surface should stay snapshot-based or move to a breaking async stream contract
  - keep the replay/history transformer and block-based transcript architecture stable while the boundary shift lands
- Out of scope:
  - session-level cancellation until after the provider-boundary shift
  - another full transcript or message-model rewrite
  - low-level memory or serialization optimization work
  - a streaming-surface redesign beyond what the new request/context contract requires

## Current slice

Decide the next long-term provider streaming surface now that the standalone provider boundary is complete:

1. **Revisit the provider streaming API shape**: decide whether `IProgress<ChatClientAssistantTurn>` snapshot updates remain the long-term surface or whether `IChatClient` should move to a breaking `IAsyncEnumerable<T>` stream contract.
   - Code references: `Mcp.Net.LLM/Interfaces/IChatClient.cs`, `Mcp.Net.LLM/Models/ChatClientAssistantTurn.cs`, `Mcp.Net.Agent/Core/ChatSession.cs`

2. **Preserve transcript-upsert semantics if the streaming surface changes**: stable assistant/block identifiers, durable transcript `Updated` events, and current replay assumptions must keep working if the transport contract changes.
   - Code references: `Mcp.Net.Agent/Core/ChatSession.cs`, `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs`, `Mcp.Net.LLM/OpenAI/*`, `Mcp.Net.LLM/Anthropic/*`

3. **Keep the now-clean provider boundary stable while revisiting that API**: do not reintroduce MCP/session helpers or provider-owned conversation state into `Mcp.Net.LLM`.
   - Code references: `Mcp.Net.LLM/Mcp.Net.LLM.csproj`, `Mcp.Net.LLM/Interfaces/IChatClient.cs`, `Mcp.Net.Agent/Tools/ToolResultConverter.cs`

## Completed background

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

5. **Completed: preserve and then replace the temporary Web UI seam**
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
   - Replace the temporary raw-client pass-through with explicit `ChatSession` / `ISignalRChatAdapter` operations for prompt update, conversation reset, and tool refresh.
   - Leave `ChatFactory` in `Mcp.Net.WebUi`; the current lane is narrowing project boundaries, not re-homing all composition code.

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

7. **Completed: re-home MCP-backed prompt/resource/completion/elicitation helpers**
   - Move to `Mcp.Net.Agent.Interfaces`:
     - `Mcp.Net.LLM/Interfaces/IPromptResourceCatalog.cs`
     - `Mcp.Net.LLM/Interfaces/ICompletionService.cs`
     - `Mcp.Net.LLM/Interfaces/IElicitationPromptProvider.cs`
   - Move to `Mcp.Net.Agent.Catalog` / `Mcp.Net.Agent.Completions` / `Mcp.Net.Agent.Elicitation`:
     - `Mcp.Net.LLM/Catalog/PromptResourceCatalog.cs`
     - `Mcp.Net.LLM/Completions/CompletionService.cs`
     - `Mcp.Net.LLM/Elicitation/ElicitationCoordinator.cs`
   - Repoint `Mcp.Net.WebUi`, `Mcp.Net.Examples.LLMConsole`, and the corresponding tests to the new namespaces.
   - Replace the `Mcp.Net.LLM` project reference on `Mcp.Net.Client` with a narrower direct reference to `Mcp.Net.Core`.

8. **Completed: move the final MCP/Core tool-result conversion out of `Mcp.Net.LLM`**
   - Remove MCP model knowledge from `Mcp.Net.LLM/Models/ToolInvocationResult.cs`.
   - Add `Mcp.Net.Agent/Tools/ToolResultConverter.cs` for MCP `ToolCallResult` / content conversion.
   - Repoint `Mcp.Net.Agent/Core/ChatSession.cs` to use the new converter.
   - Drop the remaining `Mcp.Net.Core` project reference from `Mcp.Net.LLM/Mcp.Net.LLM.csproj`.
   - Add a project-boundary regression proving `Mcp.Net.LLM` has no project references.

## Stay in LLM

- Keep these in `Mcp.Net.LLM` for the boundary shift:
  - `Mcp.Net.LLM/Interfaces/IChatClient.cs`
  - `Mcp.Net.LLM/Interfaces/IChatClientFactory.cs`
  - `Mcp.Net.LLM/Interfaces/IApiKeyProvider.cs`
  - `Mcp.Net.LLM/Interfaces/IUserApiKeyProvider.cs`
  - `Mcp.Net.LLM/Interfaces/IChatTranscriptReplayTransformer.cs`
  - `Mcp.Net.LLM/Models/ChatClientOptions.cs`
  - `Mcp.Net.LLM/Models/ChatClientRequest.cs`
  - `Mcp.Net.LLM/Models/ChatClientTool.cs`
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
- The target boundary leaves `Mcp.Net.LLM` as the provider SDK, request/response, replay, and API-key layer rather than a second home for session orchestration or MCP client helpers.

## Next slices

1. Revisit whether the long-term streaming API should move from snapshot-based progress updates to a breaking `IAsyncEnumerable<T>` surface after the provider boundary settles.
2. Revisit session cancellation only after the provider-boundary shift, and only if the MCP client/tool-execution path then needs a clean seam.
3. Revisit whether shared `ChatClientOptions` should stay flat or start splitting into provider-specific option shapes once more provider-specific features accumulate.

## Open decisions

- Should the long-term provider streaming API stay snapshot-based or move to a breaking delta-event `IAsyncEnumerable<T>` contract after parity lands?
- How far should shared `ChatClientOptions` go before provider-specific option types or nested provider option bags are warranted?
- After the provider-boundary shift, what seam should the MCP client expose so agent/session orchestration can cancel tool execution without inventing provider-specific escape hatches? Note: `IMcpClient.CallTool` currently accepts no `CancellationToken`, so cancellation still requires an MCP client contract change, not just a session-layer parameter addition.
- Should the LLM-local tool definition live directly in `Mcp.Net.LLM` or in a thinner shared contract package if other projects need to construct provider requests without taking an agent dependency?
- Where should `ChatTranscriptEntry` live after the `Mcp.Net.Agent` extraction — it is the shared contract between replay (provider layer) and session persistence (agent layer)? Current plan keeps it in `Mcp.Net.LLM` since replay owns the type definitions.

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Verify replay/history transforms continue to run from session-owned transcript state and still preserve same-model/provider degradation rules.
- Verify the `Mcp.Net.LLM` provider boundary stays free of provider-owned conversation state and MCP/session helper drift.
- Verify Web UI and session bootstrap still work after the boundary cleanup with no raw provider-state mutation paths reintroduced.
- Add regressions for Anthropic ordered assistant blocks containing reasoning, text, and tool calls under streaming updates.
- Add regressions for stable transcript and block identifiers across partial and final assistant updates.
- Add regressions for tool execution appending `ToolResult` entries instead of mutating transcript state.
- Add regressions for provider failures surfacing as `Error` entries instead of assistant text.
- Add replay/history transform coverage for same-model preservation and degraded cross-model/provider replay.
