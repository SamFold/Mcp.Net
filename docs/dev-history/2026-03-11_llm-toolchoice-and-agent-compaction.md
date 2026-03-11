# 2026-03-11 llm-toolchoice-and-agent-compaction

## Change summary

- Added shared `ToolChoice` support across the LLM request boundary and provider adapters.
- Added agent-owned transcript compaction before provider request build, preserving the in-memory transcript while collapsing older outbound context.
- Pivoted planning docs to treat `Mcp.Net.LLM` as stable/on-demand and introduced dedicated `Mcp.Net.Agent` track files.

## Why

- `ToolChoice` was the first missing shared request-time control after the request/defaults cleanup.
- Long-running sessions needed a first context-window management seam without reopening the provider boundary or mutating persisted/UI transcript state.
- Repo-level planning needed to reflect that the active implementation lane has moved from `Mcp.Net.LLM` to `Mcp.Net.Agent`.

## Major files changed

- `Mcp.Net.LLM/Models/ChatToolChoice.cs`
- `Mcp.Net.LLM/Models/ChatRequestOptions.cs`
- `Mcp.Net.LLM/Models/ChatClientRequest.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.Agent/Models/AgentExecutionDefaults.cs`
- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.Agent/Interfaces/IChatTranscriptCompactor.cs`
- `Mcp.Net.Agent/Compaction/ChatTranscriptCompactionOptions.cs`
- `Mcp.Net.Agent/Compaction/EntryCountChatTranscriptCompactor.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `Mcp.Net.Tests/Agent/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/Agent/Compaction/EntryCountChatTranscriptCompactorTests.cs`
- `docs/vnext.md`
- `docs/vnext/llm.md`
- `docs/vnext/agent.md`
- `docs/roadmap.md`
- `docs/roadmap/llm.md`
- `docs/roadmap/agent.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.Agent|FullyQualifiedName~Mcp.Net.Tests.WebUi.Chat"`
