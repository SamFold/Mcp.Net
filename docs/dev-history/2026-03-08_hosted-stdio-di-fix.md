# Hosted Stdio DI Fix

## Change summary
- Added regressions proving `AddMcpStdioTransport(...)` must register a real hosted service and that a hosted stdio server must start over configured custom streams.
- Updated the stdio DI registration path to use the shared hosted-service registration helper instead of registering `McpServerHostedService` as an inert singleton.
- Extended `McpServerHostedService` so the hosted path starts and stops stdio transport/ingress when `StdioServerOptions` are present.
- Advanced `docs/vnext.md` past the hosted stdio DI slice and back to the remaining `Mcp.Net.Server` review work.

## Why
- The direct stdio builder path worked, but the DI registration path exposed by `AddMcpStdioTransport(...)` did not.
- The stdio extension registered `McpServerHostedService` only as a concrete singleton, so the generic host never started it.
- Even if it had started, the hosted service did not create a stdio transport or ingress loop, so configured custom streams were dead configuration.

## Major files changed
- `Mcp.Net.Server/Extensions/Transport/StdioTransportExtensions.cs`
- `Mcp.Net.Server/ServerBuilder/McpServerHostedService.cs`
- `Mcp.Net.Tests/Server/McpServerRegistrationExtensionsTests.cs`
- `docs/vnext.md`
- `docs/dev-history/2026-03-08_hosted-stdio-di-fix.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Server"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release`
