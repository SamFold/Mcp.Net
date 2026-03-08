# Pending Client Request Replacement Cleanup

## Change summary
- Added a regression proving a server-initiated client request started on an old transport was still pending after the same session reconnected on a replacement transport.
- Updated `McpServer.ConnectAsync` to cancel session-scoped pending client requests when a replacement transport successfully takes over the session.
- Advanced `docs/vnext.md` and `docs/roadmap.md` past this slice to continue the `Mcp.Net.Server` logging/debuggability and hidden mutable state review.

## Why
- Pending outbound client requests were keyed by `sessionId`.
- On reconnect replacement, lifecycle state was cleared, but the pending-request dictionary was not.
- That left old-connection requests alive until timeout even though the connection that owned them had already been replaced.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`
- `docs/dev-history/2026-03-08_pending-client-request-replacement-cleanup.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ConnectAsync_Should_CancelPendingClientRequests_From_Replaced_Transport" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~ElicitationServiceTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
