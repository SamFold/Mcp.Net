# Change Summary

- added multimodal user-content support across the LLM/session boundary so `ChatSession` can send ordered text-and-image user turns to provider adapters
- enabled OpenAI and Anthropic adapters to accept shared multimodal user input, while keeping OpenAI image generation available through the provider layer
- centralized refreshed default model IDs through `ProviderModelDefaults` and aligned examples, settings, probes, and readmes with `claude-sonnet-4-6`, `gpt-5.4`, and `gpt-image-1.5`

# Why

- the previous string-only user-message path prevented the shared runtime from carrying image input cleanly into provider requests
- provider defaults were drifting across Web UI settings, examples, and probing scripts, which made the repo advertise stale model IDs
- the runtime needed one coherent foundation before the Web UI could expose image input and generated-image output

# Major Files Changed

- `Mcp.Net.Agent/Core/ChatSession.cs`
- `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs`
- `Mcp.Net.LLM/OpenAI/OpenAiClient.cs`
- `Mcp.Net.LLM/Models/ProviderModelDefaults.cs`
- `Mcp.Net.WebUi/LLM/DefaultLlmSettings.cs`
- `Mcp.Net.WebUi/Startup/Factories/LlmSettingsFactory.cs`
- `Mcp.Net.WebUi/appsettings.json`
- `Mcp.Net.WebUi/appsettings.Development.json`
- `Mcp.Net.Examples.LLMConsole/Program.cs`
- `scripts/LlmSdkProbe/Program.cs`

# Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.WebUi|FullyQualifiedName~OpenAiChatClientTests|FullyQualifiedName~AnthropicChatClientTests|FullyQualifiedName~ChatSessionTests"`
