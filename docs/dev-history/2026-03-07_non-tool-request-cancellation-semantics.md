# 2026-03-07 - Non-tool request cancellation semantics

## Change summary
- Added regression tests proving that canceled `resources/read`, `prompts/get`, and `completion/complete` requests must surface as cancellation instead of returning a JSON-RPC `InternalError`.
- Updated prompt, resource, and completion services to rethrow `OperationCanceledException` when the request token was actually canceled.
- Updated the central `McpServer` request dispatcher to preserve real caller cancellation instead of converting it into an error response.
- Hardened SSE and stdio ingress so canceled requests are treated as cancellation rather than logged or returned as server faults.
- Updated planning docs to restore the next slice to request metadata/session-context exposure for non-tool handlers.

## Why
- The previous cancellation-token fix only forwarded the token to non-tool handlers.
- Once those handlers started honoring the token, the request path still translated cancellation into `InternalError`, which is the wrong runtime contract and would produce misleading failures.
- This needed to be closed before adding more handler context surface area, otherwise the next slice would build on top of still-broken cancellation semantics.

## Major files changed
- `Mcp.Net.Server/Services/PromptService.cs`
- `Mcp.Net.Server/Services/ResourceService.cs`
- `Mcp.Net.Server/Services/CompletionService.cs`
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Server/Transport/Stdio/StdioIngressHost.cs`
- `Mcp.Net.Server/Transport/Sse/SseTransportHost.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `Mcp.Net.Tests/Server/Completions/McpServerCompletionTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~HandleRequestAsync_Should_Propagate_RequestCancellation_FromResourceReader|FullyQualifiedName~HandleRequestAsync_Should_Propagate_RequestCancellation_FromPromptFactory|FullyQualifiedName~HandleRequestAsync_Should_Propagate_RequestCancellation_FromCompletionHandler" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~McpServerCompletionTests|FullyQualifiedName~SseConnectionManagerTests|FullyQualifiedName~StdioTransportTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
- Result: passed (`284/284`)
