# 2026-03-10 - LLM streaming transcript updates

## Change summary
- Added a typed assistant-turn update seam to `IChatClient` so provider adapters can report in-flight assistant snapshots without changing final transcript semantics.
- Updated `ChatSession`, SignalR delivery, and chat persistence so one assistant transcript entry is updated in place by transcript `Id` instead of appending temporary transcript rows.
- Added focused regression coverage for session-level assistant updates, SignalR durable update delivery, and in-memory transcript upsert behavior.
- Updated the LLM track doc and repo roadmap to point at provider-side streaming as the next active slice.

## Why
- The transcript rewrite, Web UI transcript migration, and replay/history seam were already in place, but there was no clean end-to-end path for partial assistant updates.
- Persisting partial assistant entries as append-only rows would have left chat history with stale or duplicated assistant content once streaming arrived.
- Landing the update seam first keeps the long-term API clean and makes the next provider-streaming slice a focused adapter job instead of another cross-cutting refactor.

## Major files changed
- `Mcp.Net.LLM/Interfaces/IChatClient.cs`
- `Mcp.Net.LLM/Core/ChatSession.cs`
- `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs`
- `Mcp.Net.WebUi/Infrastructure/Persistence/InMemoryChatHistoryManager.cs`
- `Mcp.Net.WebUi/Hubs/ChatHub.cs`
- `Mcp.Net.WebUi/Controllers/ChatController.cs`
- `Mcp.Net.Tests/LLM/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/WebUi/Adapters/SignalR/SignalRChatAdapterTests.cs`
- `Mcp.Net.Tests/WebUi/Infrastructure/InMemoryChatHistoryManagerTests.cs`
- `docs/vnext/llm.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Core.ChatSessionTests|FullyQualifiedName~Mcp.Net.Tests.WebUi.Adapters.SignalR.SignalRChatAdapterTests|FullyQualifiedName~Mcp.Net.Tests.WebUi.Infrastructure.InMemoryChatHistoryManagerTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
