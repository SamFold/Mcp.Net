# 2026-03-07 - Missing list_changed notification regression

## Change summary
- Added a targeted server regression test proving that post-initialize tool, prompt, and resource registrations do not notify an active client.
- Updated `docs/vnext.md` so the next slice is the production fix for the missing notification path.
- Kept `docs/roadmap.md` aligned with the review findings and noted that the regression is now pinned by a failing test.

## Why
- The latest `Mcp.Net.Server` review found that runtime tool/prompt/resource mutations never emit refresh notifications to connected clients.
- Downstream client code already relies on `list_changed` notifications to refresh tool, prompt, and resource catalogs.
- Before changing production code, we needed a focused failing regression that proves the bug and defines the expected notification contract.

## Major files changed
- `Mcp.Net.Tests/Server/McpServerTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests.RegisteringServerPrimitives_AfterInitialize_Should_Notify_Connected_Client_With_ListChangedNotifications" -m:1`
- Result: failed as expected
- Failure: expected three outbound notifications, observed zero
