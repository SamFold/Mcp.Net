# Hosted Service Startup Logging

## Change summary
- Added a regression proving `McpServerHostedService` startup logs reported a hardcoded server name/version instead of the configured server identity.
- Updated `McpServerHostedService` to source startup identity logs from the real `McpServer` configuration through a read-only `ServerInfo` snapshot.
- Advanced `docs/vnext.md` past the hosted-service startup logging fix and back to the remaining `Mcp.Net.Server` logging/debuggability and hidden mutable state review.

## Why
- Hosted-service startup logs are part of the server’s primary observability surface during boot.
- The existing implementation logged `"MCP Server"` and `"1.0.0"` regardless of the configured server identity.
- That made startup diagnostics misleading in real deployments and weakened the current review’s goal of making observability truthful.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Server/ServerBuilder/McpServerHostedService.cs`
- `Mcp.Net.Tests/Server/McpServerHostedServiceTests.cs`
- `docs/vnext.md`
- `docs/dev-history/2026-03-08_hosted-service-startup-logging.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerHostedServiceTests.StartAsync_Should_LogConfiguredServerIdentity" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerHostedServiceTests|FullyQualifiedName~McpServerRegistrationExtensionsTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
