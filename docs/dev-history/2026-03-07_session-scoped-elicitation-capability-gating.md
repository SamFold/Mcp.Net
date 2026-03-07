# 2026-03-07 - Enforce session-scoped elicitation capability gating

## Change summary
- Added a regression proving the server would still send `elicitation/create` even when the session had initialized without advertising `elicitation`.
- Stored client-advertised capabilities per session during `initialize` and cleared them on transport close.
- Updated `ElicitationService` to fail fast with `MethodNotFound` when the target session did not negotiate elicitation support.
- Updated server-side tests that were bypassing initialization to initialize the session with `elicitation` capability explicitly.

## Why
- The client already advertises `elicitation` only when a handler is registered, but the server was ignoring that negotiation state entirely.
- That meant server-initiated elicitation could be sent to unsupported sessions and only fail later via timeout or client-side `MethodNotFound`.
- The fix moves that failure to the server boundary and keeps the session contract explicit and per-session.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Server/Elicitation/ElicitationService.cs`
- `Mcp.Net.Tests/Server/Elicitation/ElicitationServiceTests.cs`
- `Mcp.Net.Tests/Server/McpServerRegistrationExtensionsTests.cs`
- `Mcp.Net.Tests/Server/ToolRegistryParameterBindingTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ElicitationServiceTests.RequestAsync_ShouldThrow_WhenClientDidNotAdvertiseElicitationCapability" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ElicitationServiceTests|FullyQualifiedName~McpServerRegistrationExtensionsTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ToolRegistryParameterBindingTests.ToolRegistry_UsesTargetServerForSessionBoundServices|FullyQualifiedName~ElicitationServiceTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
