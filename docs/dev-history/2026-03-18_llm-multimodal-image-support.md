# Change Summary

- Added typed multimodal user content to `Mcp.Net.LLM` with text and inline image parts.
- Added provider-neutral assistant image blocks plus shared image-generation request options.
- Wired OpenAI multimodal chat input, OpenAI image-generation output through Responses, and Anthropic multimodal user input.
- Updated replay behavior, Web UI transcript mapping, tests, and LLM planning docs for the new multimodal/image slice.

# Why

- `Mcp.Net.LLM` could previously only carry plain-text user input and text/reasoning/tool-call assistant output.
- OpenAI and Anthropic SDKs both support multimodal user input, and OpenAI supports generated image output through the Responses API.
- This slice establishes the provider-neutral transcript model needed for image support without widening the final turn/result contract.

# Major Files Changed

- `Mcp.Net.LLM/Models/UserContentPart.cs`
- `Mcp.Net.LLM/Models/ChatImageGenerationOptions.cs`
- `Mcp.Net.LLM/Models/ChatTranscriptEntry.cs`
- `Mcp.Net.LLM/Models/AssistantContentBlock.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/OpenAI/IOpenAiResponsesInvoker.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.LLM/Replay/ChatTranscriptReplayTransformer.cs`
- `Mcp.Net.Tests/LLM/OpenAI/OpenAiChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Anthropic/AnthropicChatClientTests.cs`
- `Mcp.Net.Tests/LLM/Replay/ChatTranscriptReplayTransformerTests.cs`
- `Mcp.Net.WebUi/Chat/ChatTranscriptEntryMapper.cs`
- `Mcp.Net.WebUi/DTOs/ChatTranscriptEntryDto.cs`
- `docs/vnext/llm.md`
- `docs/roadmap/llm.md`

# Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~LLM.OpenAI.OpenAiChatClientTests|FullyQualifiedName~LLM.Anthropic.AnthropicChatClientTests|FullyQualifiedName~LLM.Models.ChatClientRequestTests|FullyQualifiedName~LLM.Replay.ChatTranscriptReplayTransformerTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release`
