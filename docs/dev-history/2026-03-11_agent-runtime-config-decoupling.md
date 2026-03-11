# Agent Runtime Config Decoupling

## Change summary

- Made `ChatSession` runtime-first by introducing `ChatSessionConfiguration`.
- Replaced agent-shaped request-default state inside `ChatSession` with `ChatRequestOptions`.
- Removed agent-based static creation from `ChatSession` and moved agent-to-session translation into agent-layer helpers and callers.
- Updated focused agent and Web UI chat tests to cover the runtime-first surface.
- Advanced the planning docs so `Mcp.Net.Agent` points back to abort plumbing as the next slice.

## Why

- `ChatSession` is the core runtime loop and should not depend on management/persistence concepts like `AgentDefinition` or `AgentExecutionDefaults`.
- Cleaning the runtime boundary now keeps later abort and orchestration work from hardening the wrong API shape.
- Agent-based session creation still exists, but it is now clearly translation glue outside the runtime type.

## Major files changed

- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Agent/Models/ChatSessionConfiguration.cs`
- `Mcp.Net.Agent/Agents/AgentExtensions.cs`
- `Mcp.Net.Agent/Extensions/AgentExtensions.cs`
- `Mcp.Net.WebUi/Chat/Factories/ChatFactory.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/Agent/Agents/AgentExtensionsTests.cs`
- `Mcp.Net.Tests/Agent/Extensions/AgentServiceExtensionsTests.cs`
- `docs/vnext/agent.md`
- `docs/roadmap/agent.md`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~ChatSessionTests|FullyQualifiedName~AgentExtensionsTests|FullyQualifiedName~AgentServiceExtensionsTests|FullyQualifiedName~ChatFactoryTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent|FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat"`
