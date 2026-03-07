# 2026-03-07 - Preserve stdio options during DI registration

## Change summary
- Added a regression test proving `AddMcpStdioTransport(StdioServerOptions)` was dropping configured stdio and shared server option values during DI registration.
- Updated the stdio options overload to preserve title, instructions, nested auth/tool/logging settings, and stdio-specific stream configuration instead of copying only a legacy subset.
- Updated planning docs to point the next builder/DI slice at the stdio builder registration overload.

## Why
- The stdio transport registration path had the same partial-copy bug that existed in the SSE options path.
- Callers providing a preconfigured `StdioServerOptions` instance could lose title, auth flags, tool registration settings, or custom stream configuration after DI registration.

## Major files changed
- `Mcp.Net.Server/Extensions/Transport/StdioTransportExtensions.cs`
- `Mcp.Net.Tests/Server/McpServerRegistrationExtensionsTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests.AddMcpStdioTransport_WithOptionsInstance_ShouldPreserveConfiguredOptions" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
