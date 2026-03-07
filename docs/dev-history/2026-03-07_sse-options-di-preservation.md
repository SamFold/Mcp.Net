# 2026-03-07 - Preserve SSE options during DI registration

## Change summary
- Added a regression test proving `AddMcpSseTransport(SseServerOptions)` was dropping routing and security settings from the provided options instance.
- Updated the `SseServerOptions` overload to preserve the full transport-facing options surface during DI registration, including scheme, endpoint paths, CORS/origin settings, arguments, custom settings, and shared server option objects.
- Updated planning docs to point the next builder/DI slice at `AddMcpCore(McpServerBuilder)` option preservation.

## Why
- The options overload was resetting fields like `Scheme`, `SsePath`, `HealthCheckPath`, and `AllowedOrigins` back to defaults.
- That made direct DI registration inconsistent with the builder path and could silently weaken routing or security behavior for callers that passed a preconfigured `SseServerOptions` instance.

## Major files changed
- `Mcp.Net.Server/Extensions/Transport/SseTransportExtensions.cs`
- `Mcp.Net.Tests/Server/McpServerRegistrationExtensionsTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests.AddMcpSseTransport_WithOptionsInstance_ShouldPreserveRoutingAndSecuritySettings" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests|FullyQualifiedName~SseServerBuilderTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
