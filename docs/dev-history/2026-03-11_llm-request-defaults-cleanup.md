# LLM Request Defaults Cleanup

## Change Summary

- Added `AgentExecutionDefaults` on the agent side and exposed it through `AgentDefinition` as the typed compatibility bridge for legacy `Parameters` storage.
- Updated `ChatSession` to own execution defaults and emit `ChatRequestOptions` from session state when building provider requests.
- Removed shared generation controls from `ChatClientOptions` so provider construction now carries only `ApiKey` and `Model`.
- Removed OpenAI and Anthropic constructor fallbacks for `Temperature` and `MaxOutputTokens`; shared execution controls now flow only through `ChatClientRequest.Options`.
- Updated agent, Web UI, and provider tests plus planning docs to match the cleaned request boundary and the next `ToolChoice` slice.

## Why

- Construction-time provider configuration and request-time execution controls were still split across two seams, which kept the provider boundary ambiguous for agent/session callers.
- Moving shared defaults fully onto the request/session path makes `ChatSession.BuildRequest()` the durable execution seam and leaves `ChatClientOptions` as a clean construction-only contract.
- Keeping the typed agent-side compatibility bridge avoids breaking persisted agent metadata while removing raw dictionary parsing from runtime request flow.

## Major Files Changed

- `Mcp.Net.Agent/Models/AgentExecutionDefaults.cs`
- `Mcp.Net.Agent/Models/AgentDefinition.cs`
- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Agent/Agents/AgentFactory.cs`
- `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
- `Mcp.Net.LLM/Models/ChatClientOptions.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/Agent/Models/AgentExecutionDefaultsTests.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `docs/vnext/llm.md`
- `docs/roadmap/llm.md`

## Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~ChatSessionTests|FullyQualifiedName~AgentExecutionDefaultsTests|FullyQualifiedName~AgentFactoryTests|FullyQualifiedName~ChatFactoryTests|FullyQualifiedName~AgentExtensionsTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~OpenAiChatClientTests|FullyQualifiedName~AnthropicChatClientTests|FullyQualifiedName~AgentFactoryTests|FullyQualifiedName~ChatFactoryTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.Agent|FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat"`
