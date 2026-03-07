# Transport Error Session Cleanup

## Change summary
- Added a server regression proving a fatal transport error must not leave the session registered and initialized.
- Updated `McpServer` so the active transport error path closes the transport after failing pending requests with the original error.
- Moved planning docs forward within the same logging/debuggability and hidden mutable state review area.

## Why
- Before this change, `HandleTransportErrorAsync` only canceled pending client requests.
- The server left the broken transport registered in the connection manager and kept negotiated protocol state for that session.
- That created hidden stale state: later routing and broadcasts could still treat an errored session as active even though the server had already considered its in-flight work failed.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests.HandleTransportError_Should_CloseTransport_And_ClearSessionState|FullyQualifiedName~McpServerTests.HandleTransportError_Should_OnlyCancelPendingRequests_For_Session|FullyQualifiedName~McpServerTests.ConnectAsync_Should_Ignore_Close_From_Replaced_Transport_When_Tracking_ProtocolVersion" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~ServerClientIntegrationTests|FullyQualifiedName~SseConnectionManagerTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
