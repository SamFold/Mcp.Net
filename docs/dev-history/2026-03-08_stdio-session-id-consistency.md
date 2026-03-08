# Stdio Session Id Consistency

## Change summary
- Added a regression proving `StdioServerBuilder` should assign the same stable logical session id across repeated builder usage in the same process.
- Removed the process-global stdio session-id counter and introduced a shared stdio default session id constant.
- Updated the direct stdio builder path, hosted stdio path, and strict stdio sample to use the same default session id.
- Advanced `docs/vnext.md` past the stdio session-id consistency slice and back to the remaining `Mcp.Net.Server` review work.

## Why
- The direct stdio builder path was assigning `"0"`, `"1"`, and so on from hidden static state, while the hosted stdio path and docs already treated the stdio session as `"stdio"`.
- `McpServer` uses `transport.Id()` as the logical session id, so this drift leaked into handler request context and diagnostics depending on which startup path was used.
- A stable shared id removes hidden mutable state and keeps stdio behavior consistent across production entry points.

## Major files changed
- `Mcp.Net.Server/Transport/Stdio/StdioTransport.cs`
- `Mcp.Net.Server/ServerBuilder/StdioServerBuilder.cs`
- `Mcp.Net.Server/ServerBuilder/McpServerHostedService.cs`
- `Mcp.Net.Examples.SimpleServer/StrictStdioServer.cs`
- `Mcp.Net.Tests/Server/StdioServerBuilderTests.cs`
- `docs/vnext.md`
- `docs/dev-history/2026-03-08_stdio-session-id-consistency.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~StdioServerBuilderTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Server"`
- Attempted `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release`, but that run stalled in `vstest`, so this slice records the focused and broader server-slice verification only.
