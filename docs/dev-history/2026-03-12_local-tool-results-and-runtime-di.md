# 2026-03-12 - Local tool results and runtime DI cleanup

## Change summary
- Added public local-tool result helpers through `ToolInvocation` and `ToolInvocationResults`.
- Replaced the internal-only result factory usage in agent runtime error paths with the new public helper surface.
- Split `ToolRegistry` registration out of `AddChatRuntimeServices()` into explicit `AddToolRegistry()` opt-in wiring.
- Updated agent and Web UI planning/docs to reflect the completed cleanup slice and the next typed-binding/tool slice.

## Why
- Local tool authors needed a usable public way to create `ToolInvocationResult` values without calling a low-level multi-parameter constructor directly.
- The shared agent runtime DI surface still registered a `ToolRegistry` seam that `ChatSession` and `ChatSessionFactory` no longer used.
- The planning docs needed to advance once the public result-helper and runtime-DI cleanup slice was complete.

## Major files changed
- `Mcp.Net.Agent/Tools/ToolInvocation.cs`
- `Mcp.Net.Agent/Tools/ToolInvocationResults.cs`
- `Mcp.Net.Agent/Extensions/ChatRuntimeServiceCollectionExtensions.cs`
- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Agent/Tools/McpToolExecutor.cs`
- `Mcp.Net.Agent/Tools/ToolResultConverter.cs`
- `Mcp.Net.WebUi/Startup/WebUiStartup.cs`
- `Mcp.Net.Tests/Agent/Extensions/ChatRuntimeServiceCollectionExtensionsTests.cs`
- `Mcp.Net.Tests/Agent/Tools/ToolInvocationResultHelpersTests.cs`
- `docs/vnext/agent.md`
- `docs/roadmap/agent.md`
- `docs/dev-history/2026-03-12_local-tool-results-and-runtime-di.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~ChatRuntimeServiceCollectionExtensionsTests|FullyQualifiedName~ToolInvocationResultHelpersTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent"`
