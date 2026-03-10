# 2026-03-10 - LLM option cleanup

## Change summary
- Added shared `MaxOutputTokens` to `ChatClientOptions`.
- Threaded agent and Web UI `max_tokens` settings into `ChatClientOptions` for both the library and Web UI agent factory paths.
- Mapped shared max-output-token handling into the OpenAI and Anthropic provider request builders.
- Made Anthropic honor shared `Temperature` and removed adapter-owned fallback prompt injection when `SystemPrompt` is blank.
- Updated LLM planning docs so the next slice points at session cancellation.

## Why
- Agent and Web UI settings already exposed `max_tokens`, but that intent was being dropped before provider request construction.
- Anthropic was ignoring the shared temperature knob, which made provider behavior diverge behind the same option surface.
- Hidden provider-owned prompt defaults made outbound requests less truthful and created surprising behavior for blank system prompts.
- The planning docs needed to advance to the next concrete slice once the option cleanup landed.

## Major files changed
- `Mcp.Net.LLM/Models/ChatClientOptions.cs`
- `Mcp.Net.LLM/Agents/AgentFactory.cs`
- `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Agents/AgentFactoryTests.cs`
- `Mcp.Net.Tests/WebUi/Chat/ChatFactoryTests.cs`
- `docs/vnext/llm.md`
- `docs/roadmap/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~OpenAiChatClientTests|FullyQualifiedName~AnthropicChatClientTests|FullyQualifiedName~AgentFactoryTests|FullyQualifiedName~ChatFactoryTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
- `git diff --check`
