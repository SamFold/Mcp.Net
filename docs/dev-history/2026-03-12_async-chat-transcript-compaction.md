# 2026-03-12 - Async chat transcript compaction

## Change summary
- Changed `IChatTranscriptCompactor` from a synchronous `Compact(...)` contract to `CompactAsync(...)` with cancellation support.
- Updated `ChatSession` to await transcript compaction before each provider request and to propagate turn cancellation through compaction.
- Kept the default `EntryCountChatTranscriptCompactor` behavior the same behind the new async contract.
- Added regressions proving provider requests wait for async compaction and that cancellation during compaction returns a cancelled turn summary.
- Advanced the agent planning docs so reset/load transcript notifications are now the active next slice, and recorded token-aware context budgeting as a later roadmap item.

## Why
- The compactor seam was still young, so this was the lowest-cost point to make it async before external consumers depend on a sync-only contract.
- Future compaction strategies may need async work such as tokenization services or summarization, and the runtime should not force blocking wrappers later.
- `ChatSession` should treat compaction as part of the turn lifecycle so cancellation and request shaping stay consistent.

## Major files changed
- `Mcp.Net.Agent/Interfaces/IChatTranscriptCompactor.cs`
- `Mcp.Net.Agent/Compaction/EntryCountChatTranscriptCompactor.cs`
- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Tests/Agent/Compaction/EntryCountChatTranscriptCompactorTests.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `docs/vnext/agent.md`
- `docs/vnext.md`
- `docs/roadmap/agent.md`
- `docs/roadmap.md`
- `docs/dev-history/2026-03-12_async-chat-transcript-compaction.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Mcp.Net.Tests.Agent.Compaction.EntryCountChatTranscriptCompactorTests|FullyQualifiedName~Mcp.Net.Tests.Agent.Core.ChatSessionTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Mcp.Net.Tests.Agent"`
