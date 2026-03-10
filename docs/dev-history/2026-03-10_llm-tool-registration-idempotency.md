# 2026-03-10 - LLM tool registration idempotency

## Change summary
- Made OpenAI and Anthropic `RegisterTools` replace the registered tool set instead of appending to it.
- Added provider regressions proving repeated registration does not duplicate tools and later registrations replace earlier sets.
- Documented the replace semantics on `IChatClient.RegisterTools` and the provider implementations.
- Updated the LLM planning docs to defer session cancellation and make tool-registration idempotency the next active slice.

## Why
- Tool refresh paths can call `RegisterTools` more than once, and the old append behavior accumulated duplicate model-facing tool definitions.
- The current callers treat tool registration as setting the active tool set, so replacement semantics match the real usage better.
- The interface contract needed to be explicit once the method stopped behaving like an additive registration API.
- Cancellation work is currently heavier than the value of taking it next because the MCP client/tool path does not support it cleanly yet.

## Major files changed
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.LLM/Interfaces/IChatClient.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `docs/vnext.md`
- `docs/vnext/llm.md`
- `docs/roadmap.md`
- `docs/roadmap/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
- `git diff --check`
