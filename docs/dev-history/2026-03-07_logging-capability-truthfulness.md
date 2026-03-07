# Logging Capability Truthfulness

## Change summary
- Added a server regression proving `initialize` must not advertise `logging` when the MCP logging primitive is not implemented.
- Updated `McpServer` to sanitize advertised capabilities so `logging` is suppressed even if callers set `ServerCapabilities.Logging`.
- Moved planning docs to the next server-review area: logging/debuggability and hidden mutable state.

## Why
- The default server path was already truthful, but callers could explicitly set `ServerCapabilities.Logging` and `initialize` would echo it back.
- The repo does not implement `logging/setLevel` or `notifications/message`, so advertising `logging` was a protocol lie in that explicit configuration path.
- The smallest safe fix is to suppress the capability until the protocol primitive exists.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ProcessJsonRpcRequest_Initialize_Should_NotAdvertiseLoggingCapability_WhenLoggingProtocolIsNotImplemented|FullyQualifiedName~ProcessJsonRpcRequest_Initialize_Should_Return_ServerInfo" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~McpClientInitializationTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
