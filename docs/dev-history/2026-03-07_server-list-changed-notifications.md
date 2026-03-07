# 2026-03-07 - Server list_changed notifications

## Change summary
- Added the missing server-side broadcast path for post-initialize tool, prompt, and resource mutations.
- Limited broadcasts to initialized sessions and to capabilities that advertise `listChanged`.
- Updated LLM and WebUI refresh listeners to accept the spec notification names:
  - `notifications/tools/list_changed`
  - `notifications/prompts/list_changed`
  - `notifications/resources/list_changed`
- Updated tests and planning docs to reflect the completed slice and the next review focus.

## Why
- The review found that runtime server primitive mutations only updated in-memory catalogs and never notified connected clients.
- A regression test already pinned the bug by proving that an active client observed zero refresh notifications after post-initialize mutations.
- The existing client refresh path also used non-spec notification names, so sending the correct server notifications would not have refreshed prompt/resource/tool state consistently.

## Major files changed
- `Mcp.Net.Server/McpServer.cs`
- `Mcp.Net.LLM/Catalog/PromptResourceCatalog.cs`
- `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
- `Mcp.Net.Tests/LLM/Catalog/PromptResourceCatalogTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests.RegisteringServerPrimitives_AfterInitialize_Should_Notify_Connected_Client_With_ListChangedNotifications" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~PromptResourceCatalogTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~PromptResourceCatalogTests|FullyQualifiedName~ServerClientIntegrationTests" -m:1`
- `dotnet build Mcp.Net.WebUi/Mcp.Net.WebUi.csproj --no-restore -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
- Result: passed (`278/278`)
