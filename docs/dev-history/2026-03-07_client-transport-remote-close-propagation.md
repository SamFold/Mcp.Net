# Client Transport Remote-Close Propagation

## Change summary
- Added client transport regressions proving remote EOF / remote shutdown must raise `OnClose` for both SSE and stdio transports.
- Added client transport regressions proving pending client requests must fail promptly on remote close for both transports.
- Updated `SseClientTransport` and `StdioClientTransport` so listener/read-loop termination caused by remote EOF schedules a real `CloseAsync()` instead of silently exiting.
- Fixed SSE request timeout handling so transport cancellation is surfaced as cancellation instead of a false timeout.

## Why
- Both client transports detected remote EOF, but neither converted that into a real transport close event.
- That left the rest of the client stack in a half-dead state: `McpClient` would never see `OnClose`, and pending requests could hang or be misreported.
- The SSE path had an additional bug where transport cancellation could win the timeout race and be surfaced as `TimeoutException` instead of cancellation.

## Major files changed
- `Mcp.Net.Client/Transport/SseClientTransport.cs`
- `Mcp.Net.Client/Transport/StdioClientTransport.cs`
- `Mcp.Net.Tests/Client/SseClientTransportTests.cs`
- `Mcp.Net.Tests/Client/StdioClientTransportTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ListenToServerEventsAsync_ShouldRaiseOnClose_WhenRemoteStreamEnds|FullyQualifiedName~ListenToServerEventsAsync_ShouldFailPendingRequestsPromptly_WhenRemoteStreamEnds|FullyQualifiedName~ProcessMessagesAsync_ShouldRaiseOnClose_WhenRemoteInputEnds|FullyQualifiedName~ProcessMessagesAsync_ShouldFailPendingRequestsPromptly_WhenRemoteInputEnds" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~SseClientTransportTests|FullyQualifiedName~StdioClientTransportTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
