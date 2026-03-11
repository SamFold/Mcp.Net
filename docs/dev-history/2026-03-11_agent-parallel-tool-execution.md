# 2026-03-11 agent-parallel-tool-execution

## Change summary

- Parallelized independent tool execution inside `ChatSession` with `Task.WhenAll`.
- Kept `ToolResult` transcript entries deterministic by appending them in original tool-call order after execution completes.
- Advanced the active agent planning lane from parallel tool execution to session abort plumbing.

## Why

- Sequential tool execution was the biggest avoidable latency source in the agent loop for multi-tool turns.
- Provider replay expects tool results in the same order as the originating tool calls, so transcript ordering needed to remain stable even when execution runs concurrently.
- The planning docs needed to reflect that parallel execution is complete and that abort plumbing is the next slice.

## Major files changed

- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `docs/vnext.md`
- `docs/vnext/agent.md`
- `docs/roadmap.md`
- `docs/roadmap/agent.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~ChatSessionTests|FullyQualifiedName~Mcp.Net.Tests.Agent|FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat"`
