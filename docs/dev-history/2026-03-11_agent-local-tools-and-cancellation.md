# 2026-03-11 agent-local-tools-and-cancellation

## Change summary

- Removed `IToolRegistry` from `ChatSession` so runtime tool validation now uses the session-owned tool catalog exclusively.
- Added `ILocalTool`, `LocalToolExecutor`, and `CompositeToolExecutor` to support mixed local and MCP-backed tool execution through the shared executor seam.
- Added cancellation-token flow through `SendUserMessageAsync(...)`, provider requests, and tool execution, including deterministic handling for provider cancellation and partially completed parallel tool work.
- Updated the active agent planning docs to move the next slice to concrete built-in/local tools.

## Why

- The old runtime had a double-registration problem: provider-visible tools came from session state, while execution validation still depended on `IToolRegistry`.
- The local/composite executor seam needed real runtime coverage before concrete built-in tools were added.
- Abort behavior needed to become cancellation-aware before the runtime grows more in-process tool capability.

## Major files changed

- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Agent/Events/ChatSessionEventArgs.cs`
- `Mcp.Net.Agent/Tools/ILocalTool.cs`
- `Mcp.Net.Agent/Tools/LocalToolExecutor.cs`
- `Mcp.Net.Agent/Tools/CompositeToolExecutor.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/Agent/Tools/CompositeToolExecutorTests.cs`
- `docs/vnext.md`
- `docs/vnext/agent.md`
- `docs/roadmap.md`
- `docs/roadmap/agent.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent.Core.ChatSessionTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent."`
- `dotnet build Mcp.Net.sln -c Release`
