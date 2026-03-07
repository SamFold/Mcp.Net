# 2026-03-07 - Preserve builder server options during DI registration

## Change summary
- Added a regression test proving `AddMcpCore(McpServerBuilder)` registered default `McpServerOptions` values even when the built server exposed different name, title, version, and instructions.
- Added internal builder metadata accessors so the DI registration path can reuse the builder's configured server identity and instructions.
- Updated the shared `McpServerOptions` copy path to preserve title and the nested logging, authentication, and tool-registration option objects instead of partially copying only legacy fields.
- Updated planning docs to point the next builder/DI slice at the stdio transport option copy path.

## Why
- The built `McpServer` and the DI-registered `IOptions<McpServerOptions>` could disagree about the server identity.
- That left the repo with two competing sources of truth: the protocol-visible server metadata and the options visible to downstream DI consumers.

## Major files changed
- `Mcp.Net.Server/ServerBuilder/McpServerBuilder.cs`
- `Mcp.Net.Server/Extensions/CoreServerExtensions.cs`
- `Mcp.Net.Tests/Server/McpServerRegistrationExtensionsTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests.AddMcpCore_WithBuilder_ShouldPreserveBuilderConfiguredServerOptions" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
