## Change Summary

- Added focused regression and descriptor coverage for `GlobTool`.
- Covered deterministic ordering, bounded-depth matching, limit clamping, explicit skipped-directory access, and out-of-root rejection.

## Why

- The initial glob tool implementation landed before the dedicated focused test file was committed.
- These tests lock down the agent-facing behavior that matters most for repeatable tool use.

## Major Files Changed

- `Mcp.Net.Tests/Agent/Tools/GlobToolTests.cs`

## Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~GlobToolTests`
