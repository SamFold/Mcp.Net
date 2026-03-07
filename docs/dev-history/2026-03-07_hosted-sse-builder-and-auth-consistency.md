# 2026-03-07 - Hosted SSE builder and auth consistency

## Change summary
- Fixed the hosted `SseServerBuilder` path so configured MCP and health endpoints are the actual runtime endpoints.
- Removed duplicate hosted SSE authentication by reusing middleware-authenticated request state in `SseRequestSecurity`.
- Added integration-style regression coverage for both hosted builder path routing and hosted auth invocation count.
- Advanced `docs/vnext.md` to the next review slice and updated `docs/roadmap.md` to reflect the completed work.

## Why
- The hosted SSE builder was ignoring its own configured path and health settings, which made the hosted contract diverge from the builder options and from generated canonical/OAuth values.
- Hosted SSE requests were invoking the auth handler twice, once in middleware and once again in the transport host path.
- Both issues were in the real hosted pipeline, so they needed regression coverage at that level.

## Major files changed
- `Mcp.Net.Server/ServerBuilder/SseServerBuilder.cs`
- `Mcp.Net.Server/Transport/Sse/SseRequestSecurity.cs`
- `Mcp.Net.Tests/Server/SseServerBuilderTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --filter "FullyQualifiedName~SseServerBuilderTests.ConfigureWebApplication_Should_Serve_ConfiguredSsePath_InsteadOf_DefaultPath" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --filter "FullyQualifiedName~SseServerBuilderTests.ConfigureWebApplication_Should_AuthenticateHostedSseConnection_OnlyOnce" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --filter "FullyQualifiedName~SseServerBuilderTests|FullyQualifiedName~SseConnectionManagerTests|FullyQualifiedName~McpAuthenticationMiddlewareTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -m:1`
- Result: full suite passed (`277/277`)
