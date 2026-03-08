# Change Summary

- Added an OpenAI regression test that exercises `ParseToolArguments(...)` with nested object and array values.
- Added an Anthropic regression test that exercises tool-use payload parsing with nested object and array values.
- Kept the new regressions focused on preserving structured values instead of flattening them to strings.

# Why

- The nested tool-argument bug needed failing provider-level regressions before the implementation fix.
- These tests pin the exact broken behavior on both providers and protect the boundary where MCP tool calls are materialized from model responses.

# Major Files Changed

- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`

# Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~OpenAiChatClientTests.ParseToolArguments_WithNestedObjectAndArray_ShouldPreserveStructuredValues|FullyQualifiedName~AnthropicChatClientTests.SendMessageAsync_ToolCallWithNestedObjectAndArray_ShouldPreserveStructuredValues"`
