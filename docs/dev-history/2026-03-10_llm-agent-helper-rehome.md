# 2026-03-10 - LLM agent helper re-home

## Change summary
- Moved prompt/resource catalog, completion, and elicitation helper contracts and implementations from `Mcp.Net.LLM` into `Mcp.Net.Agent`.
- Repointed Web UI and console consumers to the new `Mcp.Net.Agent` namespaces.
- Added a boundary regression proving `Mcp.Net.LLM` no longer references `Mcp.Net.Client`.
- Updated the LLM planning docs to point at the remaining `ToolInvocationResult` conversion cleanup.

## Why
- These helpers orchestrate MCP-backed session behavior rather than provider execution, so they belong on the agent side.
- Removing them from `Mcp.Net.LLM` narrows the provider boundary and reduces cross-project coupling.

## Major files changed
- `Mcp.Net.Agent/Catalog/PromptResourceCatalog.cs`
- `Mcp.Net.Agent/Completions/CompletionService.cs`
- `Mcp.Net.Agent/Elicitation/ElicitationCoordinator.cs`
- `Mcp.Net.Agent/Interfaces/IPromptResourceCatalog.cs`
- `Mcp.Net.Agent/Interfaces/ICompletionService.cs`
- `Mcp.Net.Agent/Interfaces/IElicitationPromptProvider.cs`
- `Mcp.Net.LLM/Mcp.Net.LLM.csproj`
- `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
- `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs`
- `Mcp.Net.Tests/LLM/ProjectBoundary/McpNetLlmProjectBoundaryTests.cs`
- `docs/vnext/llm.md`
- `docs/roadmap/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.ProjectBoundary.McpNetLlmProjectBoundaryTests|FullyQualifiedName~Mcp.Net.Tests.Agent.Catalog.PromptResourceCatalogTests|FullyQualifiedName~Mcp.Net.Tests.Agent.Completions.CompletionServiceTests|FullyQualifiedName~Mcp.Net.Tests.Agent.Elicitation.ElicitationCoordinatorTests|FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat.ChatFactoryTests|FullyQualifiedName~Mcp.Net.Tests.WebUi.Adapters.SignalR.SignalRChatAdapterTests"`
- `dotnet build Mcp.Net.sln -c Release --no-restore`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore`
