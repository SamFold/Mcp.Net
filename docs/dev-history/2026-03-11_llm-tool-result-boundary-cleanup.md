# 2026-03-11 - LLM tool-result boundary cleanup

## Change summary
- Removed MCP tool-result conversion logic from `Mcp.Net.LLM/Models/ToolInvocationResult.cs`, leaving it as a pure LLM-local data and serialization type.
- Added `Mcp.Net.Agent/Tools/ToolResultConverter.cs` and updated `ChatSession` to perform MCP-to-LLM result translation on the agent side.
- Removed the final `Mcp.Net.Core` project reference from `Mcp.Net.LLM`.
- Updated the LLM planning docs to mark the standalone provider boundary as complete and point the next slice at the streaming API decision.

## Why
- The provider library should not know about MCP transport/content/result models.
- Moving the conversion into `Mcp.Net.Agent` completes the provider-boundary cleanup and makes `Mcp.Net.LLM` a standalone provider package.

## Major files changed
- `Mcp.Net.LLM/Models/ToolInvocationResult.cs`
- `Mcp.Net.LLM/Mcp.Net.LLM.csproj`
- `Mcp.Net.Agent/Tools/ToolResultConverter.cs`
- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Tests/Agent/Tools/ToolResultConverterTests.cs`
- `Mcp.Net.Tests/LLM/Models/ToolInvocationResultTests.cs`
- `Mcp.Net.Tests/LLM/ProjectBoundary/McpNetLlmProjectBoundaryTests.cs`
- `docs/vnext/llm.md`
- `docs/roadmap/llm.md`
- `docs/llm-provider-boundary.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Mcp.Net.Tests.Agent.Tools.ToolResultConverterTests|FullyQualifiedName~Mcp.Net.Tests.LLM.Models.ToolInvocationResultTests|FullyQualifiedName~Mcp.Net.Tests.LLM.ProjectBoundary.McpNetLlmProjectBoundaryTests|FullyQualifiedName~Mcp.Net.Tests.Agent.Core.ChatSessionTests"`
- `dotnet build Mcp.Net.sln -c Release --no-restore`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore`
- Full-suite note: `Mcp.Net.Tests.Integration.ServerClientIntegrationTests.SseTransport_ShouldKeepConcurrentToolResults_IsolatedPerSession` timed out once in the full run, then passed when rerun alone.
