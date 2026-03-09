# 2026-03-09 - LLM transcript persistence and rehydration

## Change summary
- Added replay-loading APIs to `IChatClient` and `ChatSession` so persisted transcript can repopulate both session state and provider request history.
- Replaced flat `StoredChatMessage` persistence with typed `ChatTranscriptEntry` storage across the Web UI repository/history path.
- Loaded persisted transcript during adapter creation in the hub and controller, stored all runtime transcript entries including `User`, and removed the fake system-history message broadcast.

## Why
- The next `Mcp.Net.LLM` slice required session resume to be real rather than relying on provider process memory.
- Typed transcript persistence matches the block-based transcript model and removes the old text-first history abstraction.
- Moving user-entry persistence into `ChatSession` keeps transcript ownership in one place and simplifies long-term API shape.

## Major files changed
- `Mcp.Net.LLM/Interfaces/IChatClient.cs`
- `Mcp.Net.LLM/Core/ChatSession.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.LLM/Interfaces/IChatHistoryManager.cs`
- `Mcp.Net.WebUi/Chat/Repositories/ChatRepository.cs`
- `Mcp.Net.WebUi/Infrastructure/Persistence/InMemoryChatHistoryManager.cs`
- `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs`
- `Mcp.Net.WebUi/Hubs/ChatHub.cs`
- `Mcp.Net.WebUi/Controllers/ChatController.cs`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Core.ChatSessionTests|FullyQualifiedName~Mcp.Net.Tests.LLM.OpenAI.OpenAiChatClientTests|FullyQualifiedName~Mcp.Net.Tests.LLM.Anthropic.AnthropicChatClientTests|FullyQualifiedName~Mcp.Net.Tests.WebUi.Hubs.ChatHubHistoryLoadingTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
