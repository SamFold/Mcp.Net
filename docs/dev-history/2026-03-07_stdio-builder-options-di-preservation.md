# 2026-03-07 - Preserve stdio builder options during DI registration

## Change summary
- Added a regression test proving `AddMcpStdioTransport(McpServerBuilder)` was still constructing default stdio options instead of preserving builder-configured server metadata.
- Updated the stdio builder registration path to preserve builder-configured name, title, version, instructions, and capabilities.
- Marked the concrete builder/DI default-copy inconsistencies from this review pass as closed and moved `vnext` on to SSE vs stdio parity.

## Why
- The stdio builder path had drifted from the builder configuration in the same way earlier SSE and core registration paths had.
- That left DI consumers with default server metadata even when the builder was explicitly configured.

## Major files changed
- `Mcp.Net.Server/Extensions/Transport/StdioTransportExtensions.cs`
- `Mcp.Net.Tests/Server/McpServerRegistrationExtensionsTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests.AddMcpStdioTransport_WithBuilder_ShouldPreserveBuilderConfiguredServerOptions" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
