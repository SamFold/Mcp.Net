# Replacement Transport Lifecycle State Reset

## Change summary
- Added regressions proving a replacement transport was inheriting the prior session's ready state and elicitation capability before re-initializing.
- Updated `McpServer.ConnectAsync` to clear inherited negotiated protocol, client capability, and readiness state when a new transport replaces an existing active session.
- Restored the previous lifecycle state if transport registration fails after startup.

## Why
- Session lifecycle state was keyed only by `sessionId`.
- When a new transport replaced an existing session, the old negotiated and ready state remained until the new transport sent its own `initialize`.
- That let the replacement connection receive `list_changed` notifications and server-initiated elicitation too early.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `Mcp.Net.Tests/Server/Elicitation/ElicitationServiceTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ConnectAsync_Should_NotCarryReadyState_To_ReplacementTransport_BeforeReinitialize|FullyQualifiedName~RequestAsync_ShouldNotReuse_ElicitationCapability_OnReplacementTransport_BeforeReinitialize" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~ElicitationServiceTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
