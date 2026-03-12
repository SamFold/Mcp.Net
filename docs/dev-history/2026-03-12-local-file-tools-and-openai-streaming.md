# 2026-03-12 - Local file tools and OpenAI streaming fix

## Change summary
- Added the first bounded built-in local filesystem tools: `ReadFileTool` and `ListFilesTool`, backed by a shared `FileSystemToolPolicy`.
- Updated `Mcp.Net.Examples.LLMConsole` to run direct sessions through `ChatSession`, support optional local file tools, and clean up streaming/thinking UI behavior.
- Added a max tool-round guard to `ChatSession` so runaway tool loops stop with a session-visible error.
- Fixed the OpenAI adapter to reconstruct streamed tool calls by update index and raw argument bytes, matching the OpenAI SDK streaming function-calling model.
- Updated planning docs to move the active agent slice to the remaining abort transcript cleanup work.

## Why
- The agent runtime needed a real built-in tool slice to validate the new public local-tool authoring seam.
- `LLMConsole` needed to exercise `Mcp.Net.Agent` directly instead of carrying a separate direct-chat path.
- OpenAI tool calling was still broken while Anthropic worked, and the provider adapter was not following the SDK's streaming tool-call assembly model.
- Loop safety needed a first guardrail before broader write, shell, or discovery tools land.

## Major files changed
- `Mcp.Net.Agent/Tools/FileSystemToolPolicy.cs`
- `Mcp.Net.Agent/Tools/ReadFileTool.cs`
- `Mcp.Net.Agent/Tools/ListFilesTool.cs`
- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.Examples.LLMConsole/Program.cs`
- `Mcp.Net.Examples.LLMConsole/UI/ChatUIHandler.cs`
- `Mcp.Net.Tests/Agent/Tools/FileSystemToolsTests.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `docs/vnext/agent.md`
- `docs/roadmap/agent.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~FileSystemToolPolicyTests|FullyQualifiedName~ReadFileToolTests|FullyQualifiedName~ListFilesToolTests|FullyQualifiedName~ChatUIHandlerTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~SendUserMessageAsync_WhenToolCallRoundsExceedLimit_ShouldAppendSessionErrorAndStopLoop|FullyQualifiedName~ReadFileToolTests|FullyQualifiedName~ListFilesToolTests|FullyQualifiedName~ChatUIHandlerTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~OpenAiChatClientTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~LocalToolSchemaGeneratorTests|FullyQualifiedName~ReadFileToolTests|FullyQualifiedName~ListFilesToolTests|FullyQualifiedName~OpenAiChatClientTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent|FullyQualifiedName~ToolDiscoveryServiceTests"`
- `dotnet build Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj -c Release`
