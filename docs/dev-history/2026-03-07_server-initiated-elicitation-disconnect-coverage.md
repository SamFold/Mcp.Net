# 2026-03-07 - Add disconnect coverage for server-initiated elicitation

## Change summary
- Added paired integration tests covering server-initiated elicitation while the client disconnects mid-flight over both SSE and stdio.
- Extended the stdio integration harness with an explicit `DisconnectClientAsync()` helper so tests can signal a real EOF instead of only disposing `leaveOpen` stream wrappers.
- Verified that both transports cancel the pending server-side elicitation promptly when the disconnect is simulated correctly.
- Advanced `vnext` to the next parity candidate: server-initiated notification behavior.

## Why
- The SSE vs stdio parity review needed coverage for the highest-risk lifecycle case: a pending outbound client request during disconnect.
- The first stdio test attempt exposed a harness issue rather than a product bug: disposing the client-side stream wrapper did not close the underlying pipe, so the server never saw EOF.
- Capturing the correct disconnect behavior in the harness prevents false positives in later parity work.

## Major files changed
- `Mcp.Net.Tests/Integration/ServerClientIntegrationTests.cs`
- `Mcp.Net.Tests/Integration/TestServerHarness.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~SseTransport_ShouldCancel_ServerInitiatedElicitation_WhenClientDisconnects|FullyQualifiedName~StdioTransport_ShouldCancel_ServerInitiatedElicitation_WhenClientDisconnects" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~SseTransport_ShouldCancel_ServerInitiatedElicitation_WhenClientDisconnects|FullyQualifiedName~StdioTransport_ShouldCancel_ServerInitiatedElicitation_WhenClientDisconnects|FullyQualifiedName~StdioClientTransportTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
