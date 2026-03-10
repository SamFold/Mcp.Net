# 2026-03-10 - LLM usage and stop-reason propagation

## Change summary
- Added shared `ChatUsage` metadata and `StopReason` propagation for assistant turns so OpenAI and Anthropic responses carry provider usage details through `IChatClient`, transcript persistence, and Web UI transcript DTOs.
- Updated both provider adapters to populate the metadata on non-streaming and streaming paths, including Anthropic's final metadata-only stream update.
- Added the standalone `scripts/LlmSdkProbe` project and supporting `.gitignore` entries for capturing live provider response shapes under `artifacts/llm-probe/`.
- Split planning into repo indexes plus per-project `docs/vnext/*.md` and `docs/roadmap/*.md` tracks, and advanced the LLM track to the next option-cleanup slice.

## Why
- The transcript and Web UI now need durable provider metadata so usage accounting and finish reasons survive persistence and streaming updates.
- Anthropic and OpenAI expose result metadata on different seams, so the adapters need explicit normalization to keep the shared model truthful.
- A local SDK probe makes it easier to capture real provider payloads for future streaming, reasoning, and error-shape regressions.
- The planning split keeps commit-sized execution slices and medium-term sequencing concrete now that multiple components are moving in parallel.

## Major files changed
- `Mcp.Net.LLM/Models/ChatUsage.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.LLM/Core/ChatSession.cs`
- `Mcp.Net.WebUi/DTOs/ChatTranscriptEntryDto.cs`
- `Mcp.Net.WebUi/Chat/ChatTranscriptEntryMapper.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Core/ChatSessionTests.cs`
- `Mcp.Net.Tests/WebUi/Chat/ChatRepositoryTranscriptDtoTests.cs`
- `scripts/LlmSdkProbe/Program.cs`
- `docs/vnext/llm.md`
- `docs/roadmap/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
- `dotnet build scripts/LlmSdkProbe/LlmSdkProbe.csproj -c Release`
- `git diff --check`
