# 2026-03-10 - OpenAI streaming chat updates

## Change summary
- Updated the OpenAI chat adapter to use the SDK streaming chat-completions API when assistant-turn updates are requested.
- Added accumulation logic for partial assistant text and streamed tool-call argument fragments so one in-flight assistant turn can be reported progressively with stable identifiers.
- Added focused OpenAI regression coverage for partial text snapshots and streamed tool-call reconstruction.
- Updated the LLM track doc so Anthropic streaming is the next active provider slice.

## Why
- The previous slice landed the session, Web UI, and persistence seam for transcript updates, but OpenAI still only produced final assistant turns.
- Without provider-side streaming, the new transcript update path could not be exercised end to end by a real adapter.
- Landing OpenAI first keeps the provider work incremental and gives Anthropic a concrete streaming pattern to match next.

## Major files changed
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `docs/vnext/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.OpenAI.OpenAiChatClientTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
