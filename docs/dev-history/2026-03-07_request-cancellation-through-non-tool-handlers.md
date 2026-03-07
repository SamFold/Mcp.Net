# 2026-03-07 - Request cancellation through non-tool handlers

## Change summary
- Added regression tests proving that `HandleRequestAsync` must preserve the request cancellation token for:
  - `resources/read`
  - `prompts/get`
  - `completion/complete`
- Updated the `McpServer` request dispatcher to keep request execution context alive internally instead of collapsing it to `sessionId`.
- Threaded request cancellation through prompt, resource, and completion execution paths.
- Updated planning docs to reflect that cancellation is fixed and metadata/session-context exposure remains the next slice.

## Why
- The review found that non-tool handlers were running with `CancellationToken.None` even when hosts supplied a real `ServerRequestContext.CancellationToken`.
- That meant aborted requests could continue running unnecessarily, and it left the context-aware entrypoint effectively weaker than intended.
- The fix needed to be test-first because the break was in the central request dispatcher and easy to regress later.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Server/Services/IPromptService.cs`
- `Mcp.Net.Server/Services/IResourceService.cs`
- `Mcp.Net.Server/Services/PromptService.cs`
- `Mcp.Net.Server/Services/ResourceService.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `Mcp.Net.Tests/Server/Completions/McpServerCompletionTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~HandleRequestAsync_Should_PassRequestCancellationToken_ToResourceReader|FullyQualifiedName~HandleRequestAsync_Should_PassRequestCancellationToken_ToPromptFactory|FullyQualifiedName~HandleRequestAsync_Should_PassRequestCancellationToken_ToCompletionHandler" -m:1`
- `dotnet build Mcp.Net.Server/Mcp.Net.Server.csproj --no-restore -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~McpServerCompletionTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
- Result: passed (`281/281`)
