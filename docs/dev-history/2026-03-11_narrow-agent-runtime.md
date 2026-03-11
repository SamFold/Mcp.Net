# 2026-03-11 Narrow Agent Runtime

## Change summary

- added the shared chat-session composition surface with `IChatSessionFactory`, `ChatSessionFactory`, `ChatSessionFactoryOptions`, `NoOpToolExecutor`, and `AddChatRuntimeServices()`
- removed the obsolete `AgentDefinition` / manager / registry / store layer from `Mcp.Net.Agent`
- removed the corresponding agent-driven controllers, DTOs, startup hooks, and chat-factory branches from `Mcp.Net.WebUi`
- updated tests and planning docs to reflect the narrowed runtime-first package boundary

## Why

- the core of the project is the `ChatSession` runtime, not the old Web UI-driven agent-management model
- keeping the legacy layer alive obscured the package boundary and forced current code to preserve obsolete abstractions
- the narrowed runtime surface leaves the next slice ready for real built-in/local tools on top of the session factory seam

## Major files changed

- `Mcp.Net.Agent/Extensions/ChatRuntimeServiceCollectionExtensions.cs`
- `Mcp.Net.Agent/Factories/ChatSessionFactory.cs`
- `Mcp.Net.Agent/Interfaces/IChatSessionFactory.cs`
- `Mcp.Net.Agent/Models/ChatSessionFactoryOptions.cs`
- `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
- `Mcp.Net.WebUi/Controllers/ChatController.cs`
- `Mcp.Net.WebUi/Controllers/ToolsController.cs`
- `Mcp.Net.WebUi/Startup/WebUiStartup.cs`

## Verification notes

- passed: `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --filter "FullyQualifiedName~ChatRuntimeServiceCollectionExtensionsTests|FullyQualifiedName~ToolsControllerTests|FullyQualifiedName~ChatFactoryTests|FullyQualifiedName~ChatControllerTests"`
- passed: `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Agent|FullyQualifiedName~WebUi"`
- failed outside this slice: `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName!~Integration"` due to `StdioTransportTests.StdioIngressHost_BackToBackInitializeAndInitializedNotification_Should_LeaveSessionReady_ForServerDrivenNotifications`
- failed outside this slice: `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release` due to an existing SSE integration timeout in `ServerClientIntegrationTests.SseTransport_ShouldHandleElicitation_And_Completions_EndToEnd`
