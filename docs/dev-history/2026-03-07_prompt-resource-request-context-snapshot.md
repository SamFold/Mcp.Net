# 2026-03-07 - Prompt and resource request context snapshot

## Change summary
- Added regression tests proving that prompt factories and resource readers invoked through `HandleRequestAsync` must receive session, transport, and metadata context from the originating request.
- Extended prompt and resource registration with context-aware overloads that accept `HandlerRequestContext` plus the request cancellation token.
- Updated prompt and resource service execution paths so the existing `HandlerRequestContext` snapshot flows into registered handlers.
- Updated planning docs to close the non-tool handler context gap and move the next slice to remaining builder/DI inconsistencies.

## Why
- Completion handlers already had request-context access, but prompt and resource handlers were still blind to the session and transport metadata captured upstream by SSE or stdio.
- That left the non-tool handler surface inconsistent and made it harder to write session-aware prompt/resource logic without reaching back into transport-specific code.
- The fix was straightforward once `HandlerRequestContext` existed, and it was safest to pin both seams with direct server tests first.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.Server/Services/IPromptService.cs`
- `Mcp.Net.Server/Services/IResourceService.cs`
- `Mcp.Net.Server/Services/PromptService.cs`
- `Mcp.Net.Server/Services/ResourceService.cs`
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~HandleRequestAsync_Should_Expose_RequestMetadata_And_SessionContext_ToResourceReader|FullyQualifiedName~HandleRequestAsync_Should_Expose_RequestMetadata_And_SessionContext_ToPromptFactory" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~McpServerCompletionTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
- Result: passed (`287/287`)
