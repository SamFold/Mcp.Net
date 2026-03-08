# Core Title DI Fix

## Change summary
- Added a regression proving `AddMcpCore(Action<McpServerOptions>)` dropped a configured server title during `initialize`.
- Updated the DI-built `McpServer` path to preserve `options.Title` instead of overwriting the title with the server name.
- Advanced `docs/vnext.md` past the core title regression and back to the remaining `Mcp.Net.Server` review work.

## Why
- The builder-based registration path already preserved a configured title, but the `AddMcpCore(Action<McpServerOptions>)` path did not.
- That made `initialize` return the wrong display title for hosts that configured the server through DI options instead of the fluent builder.
- The bug was user-visible and easy to regress because the affected options path already had coverage for name and version, but not title.

## Major files changed
- `Mcp.Net.Server/Extensions/CoreServerExtensions.cs`
- `Mcp.Net.Tests/Server/McpServerRegistrationExtensionsTests.cs`
- `docs/vnext.md`
- `docs/dev-history/2026-03-08_core-title-di-fix.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~McpServerRegistrationExtensionsTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Server"`
