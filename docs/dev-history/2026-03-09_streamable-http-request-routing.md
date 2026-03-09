# 2026-03-09 Streamable HTTP request routing

## Change summary

- updated `SseClientTransport` so fresh POST-initiated requests no longer complete from the optional GET SSE stream once the client can determine the response is bound to the POST path
- preserved compatibility with the current in-repo server shape by allowing legacy no-body POST handling to fall back to the optional GET SSE stream when needed
- failed POST-scoped SSE requests promptly when the response stream ends before any JSON-RPC response arrives, instead of leaving the request pending until timeout
- enabled the previously skipped routing regression and added a new regression covering premature POST-scoped SSE end-of-stream behavior
- advanced the client planning track and roadmap to the next reconnect, retry, and stale-state cleanup slice

## Why

- the client still treated the optional GET SSE listener as part of the normal response path for fresh Streamable HTTP POST requests, which is not the intended 2025-11-25 behavior
- that routing ambiguity could surface duplicate or misrouted responses when both GET and POST paths were active
- POST-scoped SSE streams that terminated before a response quietly left requests hanging until timeout, so the transport needed prompt cancellation semantics for that failure mode

## Major files changed

- `Mcp.Net.Client/Transport/SseClientTransport.cs`
- `Mcp.Net.Tests/Client/SseClientTransportTests.cs`
- `docs/vnext.md`
- `docs/vnext/client.md`
- `docs/roadmap.md`
- `docs/dev-history/2026-03-09_streamable-http-request-routing.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Client.SseClientTransportTests.SendRequestAsync_ShouldFailPromptly_WhenPostScopedSseStreamEndsBeforeResponse"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Client"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Integration.ServerClientIntegrationTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release`
