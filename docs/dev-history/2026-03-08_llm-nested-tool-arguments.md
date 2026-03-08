# Change Summary

- Fixed OpenAI tool argument parsing so nested objects and arrays are preserved as structured JSON values instead of throwing during `GetString()`.
- Fixed Anthropic tool argument parsing so nested objects and arrays are preserved as structured JSON values instead of being flattened to JSON strings.
- Added server-side compatibility coverage proving nested `JsonElement` argument values still serialize through `ToolCallRequest` and bind correctly in `ToolInvocationFactory`.
- Updated the active LLM vnext lane to reflect the nested tool-argument implementation slice and the chosen `JsonElement` representation.

# Why

- The new provider regression tests exposed that nested tool arguments were not safe to round-trip through the LLM adapters.
- OpenAI failed when nested object or array arguments reached `ParseToolArguments(...)`.
- Anthropic returned nested arguments as strings, which would break callers expecting structured payloads and weaken server-side tool binding.
- Preserving nested values as `JsonElement` keeps the change small while staying compatible with the existing MCP request serialization and binding path.

# Major Files Changed

- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.Tests/Server/ToolInvocationFactoryTests.cs`
- `docs/vnext/llm.md`

# Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~OpenAiChatClientTests.ParseToolArguments_WithNestedObjectAndArray_ShouldPreserveStructuredValues|FullyQualifiedName~AnthropicChatClientTests.SendMessageAsync_ToolCallWithNestedObjectAndArray_ShouldPreserveStructuredValues"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~ToolInvocationFactoryTests.CreateHandler_BindsNestedStructuredArguments_FromJsonElementValues"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi|FullyQualifiedName~Mcp.Net.Tests.Server.ToolInvocationFactoryTests"`
