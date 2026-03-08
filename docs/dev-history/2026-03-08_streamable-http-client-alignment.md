# 2026-03-08 Streamable HTTP client alignment

## Change summary

- updated `SseClientTransport` to complete JSON-RPC requests from successful POST response bodies when the server replies with either `application/json` or POST-scoped `text/event-stream`
- allowed POST-only Streamable HTTP startup by treating GET SSE as optional and allowing the initial `initialize` request before a session header exists
- added focused client regressions for inline POST JSON responses, POST-scoped SSE responses, and POST-only initialization
- added a skipped next-slice regression proving fresh POST requests should not normally complete from the optional GET SSE stream
- updated the active client planning track and roadmap notes for the next transport-routing slice

## Why

- the client was still aligned to deprecated HTTP+SSE request-response behavior and could not interoperate correctly with 2025-11-25 Streamable HTTP servers that return request responses on the POST itself
- startup also incorrectly assumed a GET SSE stream and pre-initialize session header were mandatory, which blocks spec-compliant POST-only servers
- the skipped regression captures the next remaining transport gap without mixing that routing change into this commit-sized slice

## Major files changed

- `Mcp.Net.Client/Transport/SseClientTransport.cs`
- `Mcp.Net.Tests/Client/SseClientTransportTests.cs`
- `docs/vnext.md`
- `docs/vnext/client.md`
- `docs/roadmap.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Client.SseClientTransportTests.SendRequestAsync_ShouldCompleteFromApplicationJsonPostResponseBody|FullyQualifiedName~Mcp.Net.Tests.Client.SseClientTransportTests.SendRequestAsync_ShouldCompleteFromPostScopedSseResponseStream"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Client.SseClientTransportTests.Initialize_ShouldNotRequireGetSse_WhenServerUsesPostOnlyStreamableHttp"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Client"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Integration.ServerClientIntegrationTests"`
