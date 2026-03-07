# Connect Replacement Startup Order

## Change summary
- Added a regression proving a failed replacement transport startup must not evict an existing live transport for the same session.
- Updated `McpServer.ConnectAsync` to start transports before registering them, so session replacement only happens after successful startup.
- Kept planning docs aligned inside the ongoing logging/debuggability and hidden mutable state review.

## Why
- `ConnectAsync` previously registered the replacement transport before calling `StartAsync`.
- On reconnect, that displaced the existing live transport immediately.
- If the replacement startup then failed, rollback removed the new transport and left the session with no active transport at all.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests.ConnectAsync_Should_NotEvict_Existing_Transport_When_Replacement_StartAsync_Fails|FullyQualifiedName~McpServerTests.ConnectAsync_Should_NotLeave_Transport_Registered_When_StartAsync_Fails|FullyQualifiedName~McpServerTests.ConnectAsync_Should_Subscribe_To_Transport_Events" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~SseConnectionManagerTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
