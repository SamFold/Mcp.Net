# 2026-03-09 llm-chat-session-transcript-model

## Change summary

- Replaced the legacy `ChatSession` assistant-string and tool-shim event flow with a typed transcript and activity model.
- Replaced `IChatClient`, `IChatSessionEvents`, `LlmResponse`, `LlmMessage`, and `MessageType` with typed provider turn results, transcript entries, and assistant content blocks.
- Updated the OpenAI and Anthropic chat clients to return typed assistant turns and typed failures.
- Updated the immediate Web UI bridge, DTOs, and storage event path to consume transcript changes instead of legacy assistant/tool/thinking events.
- Rewrote the `ChatSession` and provider tests to lock the new contract.
- Updated the LLM spec and roadmap docs to reflect the contract-breaking transcript rewrite and deletion-first migration stance.

## Why

- Modern provider behavior includes ordered assistant content like reasoning, text, and tool calls in a single turn, which the old flat text-first model could not represent correctly.
- Provider failures should be surfaced as typed errors rather than fake assistant text.
- Tool calls and tool results need to be distinct concepts for clean transcript semantics and future replay/history transforms.
- Keeping the old abstractions around would add tech debt and fight the new long-term `Mcp.Net.LLM` API shape.

## Major files changed

- `Mcp.Net.LLM/Core/ChatSession.cs`
- `Mcp.Net.LLM/Interfaces/IChatClient.cs`
- `Mcp.Net.LLM/Interfaces/IChatSessionEvents.cs`
- `Mcp.Net.LLM/Models/AssistantContentBlock.cs`
- `Mcp.Net.LLM/Models/ChatClientTurnResult.cs`
- `Mcp.Net.LLM/Models/ChatTranscriptEntry.cs`
- `Mcp.Net.LLM/Events/ChatSessionEventArgs.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs`
- `Mcp.Net.Tests/LLM/Core/ChatSessionTests.cs`
- `docs/llm-chat-session-item-model.md`
- `docs/vnext/llm.md`
- `docs/roadmap.md`
- `docs/vnext.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Core.ChatSessionTests|FullyQualifiedName~Mcp.Net.Tests.LLM.OpenAI.OpenAiChatClientTests|FullyQualifiedName~Mcp.Net.Tests.LLM.Anthropic.AnthropicChatClientTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
