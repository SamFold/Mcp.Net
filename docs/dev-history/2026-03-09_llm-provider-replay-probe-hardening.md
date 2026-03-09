# 2026-03-09 - LLM provider replay probe hardening

## Change summary
- Hardened Anthropic replay handling so provider turns capture typed thinking blocks and replay them with visibility-aware fallbacks.
- Reworked OpenAI replay loading so assistant text and tool calls can be restored as a single assistant history message instead of split assistant messages.
- Added probe-backed replay regressions using the captured Anthropic reasoning fixture for same-provider model switches, cross-provider handoff, and Anthropic thinking round-trips.
- Updated the active LLM track doc to point at the next slice after replay hardening.

## Why
- The previous Anthropic replay mapping inferred thinking semantics too loosely and could emit invalid replay payloads when signatures were missing.
- The previous OpenAI replay loader lost the single-turn shape of mixed assistant text and tool calls, which made restored history diverge from provider semantics.
- Captured probe fixtures are the right source of truth for replay behavior now that transcript persistence and rehydration are in place.

## Major files changed
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Replay/ChatTranscriptReplayTransformerTests.cs`
- `docs/vnext/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.OpenAI.OpenAiChatClientTests|FullyQualifiedName~Mcp.Net.Tests.LLM.Anthropic.AnthropicChatClientTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Replay|FullyQualifiedName~Mcp.Net.Tests.LLM.OpenAI.OpenAiChatClientTests|FullyQualifiedName~Mcp.Net.Tests.LLM.Anthropic.AnthropicChatClientTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
