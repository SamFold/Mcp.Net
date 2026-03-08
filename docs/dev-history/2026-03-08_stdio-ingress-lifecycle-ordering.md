# Stdio Ingress Lifecycle Ordering

## Change summary
- Added a regression proving back-to-back stdio `initialize` and `notifications/initialized` messages could leave the session permanently not ready for later server-driven `list_changed` notifications.
- Updated `StdioIngressHost` to preserve per-connection ordering for client-originated requests and notifications while still allowing client responses to flow immediately.
- Advanced `docs/vnext.md` past the completed stdio ingress ordering slice and back to the remaining `Mcp.Net.Server` review work.

## Why
- Stdio ingress was parsing messages in order but dispatching requests and notifications as independent fire-and-forget tasks.
- That allowed `notifications/initialized` to run before the earlier `initialize` request had completed its negotiated session setup.
- When that race happened, the server dropped readiness and later lifecycle-gated notifications such as `notifications/tools/list_changed` were suppressed even though the client followed the MCP handshake correctly.

## Major files changed
- `Mcp.Net.Server/Transport/Stdio/StdioIngressHost.cs`
- `Mcp.Net.Tests/Transport/StdioTransportTests.cs`
- `docs/vnext.md`
- `docs/dev-history/2026-03-08_stdio-ingress-lifecycle-ordering.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~StdioIngressHost_BackToBackInitializeAndInitializedNotification_Should_LeaveSessionReady_ForServerDrivenNotifications" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~Mcp.Net.Tests.Transport.StdioTransportTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~StdioTransport_ShouldHandleElicitation_And_Completions_EndToEnd" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
