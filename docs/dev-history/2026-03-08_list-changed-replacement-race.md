# List Changed Replacement Race

## Change summary
- Added a regression proving an in-flight `notifications/.../list_changed` broadcast could land on a replacement transport before the replacement session re-initialized.
- Updated `McpServer` to re-check session readiness immediately before delivering server-driven `list_changed` notifications.
- Advanced `docs/vnext.md` and `docs/roadmap.md` past this race fix and back to the remaining `Mcp.Net.Server` logging/debuggability and hidden mutable state review.

## Why
- `list_changed` delivery was snapshotting ready `sessionId`s and resolving the active transport later.
- A reconnect replacement could clear readiness and swap in a new transport after the snapshot but before notification delivery.
- That let the replacement connection inherit a stale ready-session snapshot and receive a server-driven notification before completing lifecycle initialization.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`
- `docs/dev-history/2026-03-08_list-changed-replacement-race.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~RegisterTool_Should_NotNotify_ReplacementTransport_When_ListChanged_Broadcast_Is_Already_InFlight" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s -m:1`
