# Lifecycle Readiness For List Changed

## Change summary
- Added a regression proving server-driven `list_changed` notifications were sent after `initialize` but before the client sent `notifications/initialized`.
- Updated `McpServer` to track per-session lifecycle readiness separately from negotiated protocol state.
- Gated `list_changed` broadcasts on the readiness marker and cleared it on close and re-initialize.

## Why
- The server previously treated “negotiated protocol version exists” as equivalent to “session is ready for normal server-driven notifications.”
- MCP lifecycle uses `notifications/initialized` as the client’s readiness marker.
- That meant tool, prompt, and resource mutations could push `list_changed` notifications too early in the session lifecycle.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~RegisteringServerPrimitives_BeforeNotificationsInitialized_Should_NotNotify_Until_ClientIsReady|FullyQualifiedName~RegisteringServerPrimitives_AfterInitialize_Should_Notify_Connected_Client_With_ListChangedNotifications" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
