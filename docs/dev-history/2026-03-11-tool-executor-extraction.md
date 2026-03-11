# 2026-03-11 Tool Executor Extraction

## Change summary

- extracted runtime tool execution behind `IToolExecutor`
- removed direct `IMcpClient` dispatch from `ChatSession`
- added MCP-backed `McpToolExecutor` plus an agent-owned `ToolInvocation`
- updated agent/web UI session creation and tests to use the new seam
- tightened the active agent planning docs to reflect session-scoped executor construction

## Why

- `ChatSession` still had a hard MCP runtime dependency even after the provider/request cleanup
- the next tool slice needs the agent loop to execute tools without caring whether the backend is MCP or something else
- moving MCP dispatch behind an executor seam makes later local or composite tool execution possible without rewriting the loop again

## Major files changed

- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Agent/Tools/IToolExecutor.cs`
- `Mcp.Net.Agent/Tools/ToolInvocation.cs`
- `Mcp.Net.Agent/Tools/McpToolExecutor.cs`
- `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `docs/vnext/agent.md`
- `docs/roadmap/agent.md`

## Verification notes

- passed `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent.Core.ChatSessionTests`
- passed `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent`
- passed `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat|FullyQualifiedName~Mcp.Net.Tests.WebUi.Adapters.SignalR"`
- a full `dotnet test` run surfaced an unrelated existing timeout in `Mcp.Net.Tests.LLM.Models.ChatCompletionStreamTests.GetResultAsync_WhenRequestCancellationTokenIsCanceled_ShouldCancelResultFactory`
