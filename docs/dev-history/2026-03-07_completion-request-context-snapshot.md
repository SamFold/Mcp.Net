# 2026-03-07 - Completion request context snapshot

## Change summary
- Added a regression test proving that completion handlers invoked through `HandleRequestAsync` must receive session, transport, and metadata context from the originating request.
- Introduced `HandlerRequestContext` as an immutable snapshot of request-scoped session and transport metadata.
- Exposed that snapshot through `CompletionRequestContext.RequestContext`.
- Updated the completion service and `McpServer` dispatcher so the context snapshot flows into registered completion handlers.
- Updated planning docs so the next slice narrows to prompt/resource handler context exposure.

## Why
- Completion handlers already had a typed request context object, which made them the smallest safe seam for exposing transport metadata without widening the prompt/resource delegates yet.
- Before this change, completion handlers could see completion parameters and cancellation, but not the request session or transport metadata captured upstream by SSE or stdio.
- This closes one concrete part of the remaining non-tool handler context gap and establishes the shared snapshot shape for the next slices.

## Major files changed
- `Mcp.Net.Server/Models/HandlerRequestContext.cs`
- `Mcp.Net.Server/Completions/CompletionRequestContext.cs`
- `Mcp.Net.Server/Services/ICompletionService.cs`
- `Mcp.Net.Server/Services/CompletionService.cs`
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Tests/Server/Completions/McpServerCompletionTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~HandleRequestAsync_Should_Expose_RequestMetadata_And_SessionContext_ToCompletionHandler" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerCompletionTests|FullyQualifiedName~McpServerTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
- Result: passed (`285/285`)
