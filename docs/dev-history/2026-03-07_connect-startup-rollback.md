# Connect Startup Rollback

## Change summary
- Added a server regression proving `ConnectAsync` must not leave a transport registered when `StartAsync` fails.
- Updated `McpServer.ConnectAsync` to roll back startup failures by closing the transport and removing the registration before rethrowing.
- Kept planning docs aligned inside the ongoing logging/debuggability and hidden mutable state review.

## Why
- `ConnectAsync` registered the transport with the connection manager before calling `StartAsync`.
- If `StartAsync` threw, direct callers were left with a registered-but-dead transport in the connection manager.
- That stale registration is hidden state: later session lookups can still find a transport that never started successfully.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests.ConnectAsync_Should_NotLeave_Transport_Registered_When_StartAsync_Fails|FullyQualifiedName~McpServerTests.ConnectAsync_Should_Ignore_Close_From_Replaced_Transport_When_Tracking_ProtocolVersion|FullyQualifiedName~McpServerTests.HandleTransportError_Should_CloseTransport_And_ClearSessionState" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~SseConnectionManagerTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
