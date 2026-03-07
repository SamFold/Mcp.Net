# SSE Single Registration Path

## Change summary
- Added a regression proving a hosted SSE connection registered the same transport twice during initial connect.
- Removed the redundant host-side `RegisterTransportAsync` call so `McpServer.ConnectAsync` remains the single authoritative registration path.
- Updated planning docs within the ongoing logging/debuggability and hidden mutable state review.

## Why
- `SseTransportHost.HandleSseConnectionAsync` registered the transport with the shared connection manager and then immediately called `McpServer.ConnectAsync`, which registered the same transport again.
- That created hidden duplicate state for the same connection: duplicate close subscriptions and misleading "replacing existing transport" logs even though nothing was actually being replaced.
- The clean fix is to keep one registration path and let the server own transport registration.

## Major files changed
- `Mcp.Net.Server/Transport/Sse/SseTransportHost.cs`
- `Mcp.Net.Tests/Server/Transport/SseConnectionManagerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~SseConnectionManagerTests.HandleSseConnectionAsync_Should_Register_New_Transport_Only_Once|FullyQualifiedName~SseConnectionManagerTests.HandleMessageAsync_Should_Reject_Post_For_Different_Authenticated_User_Than_Session_Owner|FullyQualifiedName~SseConnectionManagerTests.CloseAsync_Should_Remove_Transport_And_Cancel_Pending_Request_When_Response_Completion_Fails" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~SseConnectionManagerTests|FullyQualifiedName~ServerClientIntegrationTests|FullyQualifiedName~McpServerRegistrationExtensionsTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
