# 2026-03-12 - Transcript reset/load notifications

## Change summary
- Extended `ChatTranscriptChangeKind` with `Reset` and `Loaded`.
- Updated `ChatTranscriptChangedEventArgs` so whole-transcript changes can carry a snapshot while entry-based changes continue to carry a single entry.
- Updated `ChatSession.ResetConversation()` and `ChatSession.LoadTranscriptAsync(...)` to raise transcript-change events after the session state changes.
- Added agent regressions covering reset/load notifications and updated example consumers to tolerate whole-transcript events.
- Advanced the agent planning docs to move the next slice to the first bounded built-in filesystem tools.

## Why
- Resetting or loading a transcript changed session state without notifying observers, which left event-driven consumers with stale local state.
- The event surface needed a first-class way to represent whole-transcript mutations instead of pretending every change was a single entry add/update.
- With this lifecycle gap closed, the next useful agent slice is validating the runtime with concrete read-only tools.

## Major files changed
- `Mcp.Net.Agent/Events/ChatSessionEventArgs.cs`
- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `Mcp.Net.Examples.LLMConsole/UI/ChatUIHandler.cs`
- `Mcp.Net.WebUi/Sessions/SessionHost.cs`
- `docs/vnext/agent.md`
- `docs/vnext.md`
- `docs/roadmap/agent.md`
- `docs/roadmap.md`
- `docs/dev-history/2026-03-12_transcript-reset-load-notifications.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Mcp.Net.Tests.Agent.Core.ChatSessionTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Mcp.Net.Tests.Agent|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
