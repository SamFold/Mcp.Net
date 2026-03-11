# 2026-03-11 agent-single-active-turn-lifecycle

## Change summary

- Added a single-active-turn contract to `ChatSession`.
- Added `IsProcessing`, `AbortCurrentTurn()`, and `WaitForIdleAsync(...)` to expose session-owned turn lifecycle behavior.
- Guarded transcript and configuration mutators so session state cannot change while a turn is active.
- Added focused `ChatSession` regressions for overlap rejection, busy-state reporting, abort/wait behavior, and mutation guards.
- Updated agent planning docs so the next slice moves to a library-first session factory and ownership model.

## Why

- `ChatSession` had shared mutable state and no reentrancy guard, so overlapping turns and mid-turn configuration changes were unsafe.
- Consumers needed a session-owned lifecycle surface on top of the existing cancellation-token plumbing.
- The next public construction/factory work needs a clear runtime ownership model before more API surface is added.

## Major files changed

- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/WebUi/Adapters/SignalR/SignalRChatAdapterTests.cs`
- `docs/vnext.md`
- `docs/vnext/agent.md`
- `docs/roadmap.md`
- `docs/roadmap/agent.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent.Core.ChatSessionTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent."`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.WebUi.Adapters.SignalR.SignalRChatAdapterTests"`
