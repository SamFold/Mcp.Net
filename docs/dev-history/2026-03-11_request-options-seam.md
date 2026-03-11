# Request Options Seam

## Change Summary

- Added `ChatRequestOptions` and attached it to `ChatClientRequest` as a typed request-time execution-options seam.
- Updated OpenAI and Anthropic providers to read `Temperature` and `MaxOutputTokens` from request options first, with constructor-level `ChatClientOptions` values kept as a temporary compatibility fallback.
- Added direct request-model tests plus provider regressions covering request-time option application, precedence over constructor defaults, and OpenAI temperature support gating.
- Updated the active LLM vnext and roadmap docs so the next slice points at typed agent/session defaults and later constructor-fallback removal.

## Why

- `ChatClientOptions` was carrying both long-lived client construction data and per-request execution controls.
- Moving shared generation controls toward the request boundary keeps the provider surface provider-agnostic and creates the right seam for future shared controls such as `ToolChoice`.
- The temporary fallback preserves existing behavior while the agent/session side is migrated to supply request-owned defaults.

## Major Files Changed

- `Mcp.Net.LLM/Models/ChatRequestOptions.cs`
- `Mcp.Net.LLM/Models/ChatClientRequest.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.Tests/LLM/Models/ChatClientRequestTests.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `docs/vnext/llm.md`
- `docs/roadmap/llm.md`

## Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "OpenAiChatClientTests|AnthropicChatClientTests|ChatClientRequestTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM"`
