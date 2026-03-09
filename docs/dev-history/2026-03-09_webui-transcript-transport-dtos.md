# 2026-03-09 - Web UI transcript transport DTOs

## Change summary
- Replaced the remaining flat Web UI chat transport contract with discriminated transcript-entry DTOs for REST history and SignalR `ReceiveMessage`.
- Added typed assistant content block DTOs so reasoning, text, and tool-call ordering survives the Web UI boundary without falling back to string content plus metadata bags.
- Replaced the controller send-message payload with a dedicated `UserMessageRequestDto` instead of reusing the chat history DTO for inbound user input.
- Updated the active LLM track doc and Web UI README to reflect the new transport contract and next slice.

## Why
- The previous `ChatMessageDto` flattened typed transcript state back into `Type`, `Content`, and ad-hoc metadata, which reintroduced the lossy model we had already removed from `Mcp.Net.LLM`.
- Using the same DTO for persisted history, SignalR delivery, and inbound user messages conflated different concerns and made the API harder to evolve cleanly.
- The next streaming work is easier if Web UI transport already matches the typed transcript model.

## Major files changed
- `Mcp.Net.WebUi/DTOs/ChatTranscriptEntryDto.cs`
- `Mcp.Net.WebUi/DTOs/UserMessageRequestDto.cs`
- `Mcp.Net.WebUi/Chat/ChatTranscriptEntryMapper.cs`
- `Mcp.Net.WebUi/Chat/Repositories/ChatRepository.cs`
- `Mcp.Net.WebUi/Controllers/ChatController.cs`
- `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs`
- `Mcp.Net.Tests/WebUi/Chat/ChatRepositoryTranscriptDtoTests.cs`
- `Mcp.Net.Tests/WebUi/Controllers/ChatControllerTests.cs`
- `docs/vnext/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat.ChatRepositoryTranscriptDtoTests|FullyQualifiedName~Mcp.Net.Tests.WebUi.Controllers.ChatControllerTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.WebUi"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
