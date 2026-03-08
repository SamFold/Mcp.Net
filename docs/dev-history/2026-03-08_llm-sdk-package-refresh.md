# Change Summary

- Updated `OpenAI` from `2.5.0` to `2.9.1` in `Mcp.Net.LLM`.
- Updated `Anthropic.SDK` from `5.8.0` to `5.10.0` in `Mcp.Net.LLM`.
- Aligned `Microsoft.Extensions.Configuration.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` to `10.0.3` so the refreshed OpenAI dependency graph restores cleanly.

# Why

- The LLM layer was behind the latest available NuGet packages for both provider SDKs.
- Refreshing those packages reduces drift against the current provider surfaces and verifies that the existing adapters and tests still hold on newer SDK versions.
- The OpenAI update introduced higher `Microsoft.Extensions.*` requirements through `System.ClientModel`, so the direct references needed to be raised to avoid downgrade errors with warnings treated as errors.

# Major Files Changed

- `Mcp.Net.LLM/Mcp.Net.LLM.csproj`

# Verification Notes

- `dotnet list Mcp.Net.LLM/Mcp.Net.LLM.csproj package --outdated`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~OpenAiChatClientTests|FullyQualifiedName~AnthropicChatClientTests|FullyQualifiedName~ChatSessionTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.WebUi"`
