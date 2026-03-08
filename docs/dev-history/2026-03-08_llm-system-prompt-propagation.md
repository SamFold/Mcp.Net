# Change Summary

- Fixed `Mcp.Net.LLM` provider client construction so configured `ChatClientOptions.SystemPrompt` is applied on the library path for OpenAI and Anthropic clients.
- Added regression coverage proving agent-created clients use the configured prompt.
- Added provider-level tests that verify the first outbound Anthropic request payload and OpenAI message history carry the configured prompt, with fallback coverage for the default prompt path.

# Why

- The library agent path was passing `SystemPrompt` through `ChatClientOptions` but the concrete provider clients ignored it and always started with their built-in default prompt.
- That meant sessions created through `AgentFactory` and `ChatSession.CreateFromAgent*` did not honor `AgentDefinition.SystemPrompt` until an external caller explicitly invoked `SetSystemPrompt`.
- The added tests close the original regression and harden the behavior at the provider boundary so future changes do not silently reintroduce it.

# Major Files Changed

- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.Tests/LLM/Agents/AgentFactoryTests.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `docs/vnext.md`

# Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~CreateClientFromAgentDefinitionAsync_WithConcreteProviderClient_ShouldUseAgentSystemPrompt"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~AnthropicChatClientTests|FullyQualifiedName~OpenAiChatClientTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat.ChatFactoryTests"`
