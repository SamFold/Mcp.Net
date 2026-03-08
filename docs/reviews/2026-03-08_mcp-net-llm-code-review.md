# 2026-03-08 Mcp.Net LLM Review

## Scope

Reviewed the current repository with emphasis on `Mcp.Net.LLM`, plus the adjacent `Mcp.Net.WebUi`, `Mcp.Net.Client`, and `Mcp.Net.Server` integration points that affect LLM message flow, tool execution, and agent behavior.

## Verification

- Ran `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "LLM|ChatFactory|ChatHub"`.
- Result: Passed `122`, Failed `0`, Skipped `0`.

## Findings

### High

#### 1. Agent-configured system prompts are ignored on the library path

`Mcp.Net.LLM/Agents/AgentFactory.cs:95-99` passes `SystemPrompt` into `ChatClientOptions`, but neither provider constructor consumes it in `Mcp.Net.LLM/OpenAI/OpenAiClient.cs:26-41` or `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs:28-63`. `Mcp.Net.LLM/Core/ChatSession.cs:110-169` also does not call `SetSystemPrompt` when creating a session from an agent.

Impact: sessions created through `Mcp.Net.LLM` run with the provider hard-coded default prompt instead of the agent prompt.

#### 2. Complex tool arguments are broken in the provider adapters

`Mcp.Net.LLM/OpenAI/OpenAiClient.cs:156-173` parses tool arguments by coercing any non-number and non-boolean JSON value through `GetString()`. That throws for JSON objects and arrays. `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs:223-255` converts objects and arrays into JSON strings instead of preserving them as structured values.

`Mcp.Net.Server/Tools/ToolInvocationFactory.cs:72-84` expects real JSON and deserializes arguments into the target parameter type.

Impact: nested object and array arguments either fail before the MCP call or bind incorrectly on the server.

#### 3. Provider failures are silently dropped by `ChatSession`

Both providers catch exceptions and convert them into `MessageType.System` responses in `Mcp.Net.LLM/OpenAI/OpenAiClient.cs:206-217` and `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs:167-173`.

`Mcp.Net.LLM/Core/ChatSession.cs:255-273` only drains `Assistant` and `Tool` responses from the queue. `System` and `Error` responses are ignored.

Impact: API failures and unexpected provider finish states can become user-visible no-ops instead of surfaced errors.

#### 4. Agent registry startup is racy and appears to have already produced duplicate default agents

`Mcp.Net.LLM/Agents/AgentRegistry.cs:22-29` kicks off `ReloadAgentsAsync()` in the constructor without awaiting it. `Mcp.Net.WebUi/Infrastructure/DefaultAgentInitializer.cs:27-37` immediately checks `GetAllAgentsAsync()` to decide whether defaults should be created.

That check can observe an empty cache before the background reload completes. The current repo contents look consistent with that failure mode: there are many persisted default-agent records under `Mcp.Net.WebUi/Data/Agents`, including multiple global defaults such as `Mcp.Net.WebUi/Data/Agents/25d5734e-acb4-41ed-9569-afd01c4d443f.json:2-21` and `Mcp.Net.WebUi/Data/Agents/a8534e95-b9be-4801-ab28-793bde28e6de.json:2-23`.

Impact: startup can create duplicate system defaults, and the repo already contains evidence that it has happened repeatedly.

### Medium

#### 5. Tool re-registration is append-only, so tool refreshes can duplicate provider tool definitions

`Mcp.Net.LLM/OpenAI/OpenAiClient.cs:101-108` and `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs:95-102` append to internal tool collections every time `RegisterTools` is called.

The current integrations re-register tools on updates in `Mcp.Net.WebUi/Adapters/SignalR/SignalRChatAdapter.cs:360-365` and `Mcp.Net.Examples.LLMConsole/Program.cs:93-111`.

Impact: after one or more `tools/list_changed` refreshes, the model-facing tool inventory can contain duplicates.

#### 6. Persisted agent parameters are not reliably applied, and Anthropic ignores key settings anyway

Persisted agent settings are stored as `Dictionary<string, object>` in `Mcp.Net.LLM/Models/AgentDefinition.cs:45` and deserialized from disk in `Mcp.Net.LLM/Agents/Stores/FileSystemAgentStore.cs:40-42`.

`Mcp.Net.LLM/Agents/AgentFactory.cs:101-108` only applies `temperature` when the runtime value is exactly `float`, which is brittle for JSON-deserialized values. Separately, `Mcp.Net.LLM/Anthropic/AnthropicChatClient.cs:150-158` hard-codes `Temperature = 1.0m` and `MaxTokens = 1024`.

Impact: persisted tuning values such as `temperature` and `max_tokens` are not consistently honored on the library path.

#### 7. `CloneAgentAsync` reports success even when persistence fails

`Mcp.Net.LLM/Agents/AgentManager.cs:156-165` awaits `_registry.RegisterAgentAsync(clone, userId)` but ignores the returned `bool`, then logs success and returns the clone unconditionally.

Impact: a failed save can still produce a clone object and success log entry, leaving the caller with a phantom agent that was never persisted.

## Testing Gaps

- No direct unit coverage for `OpenAiChatClient`.
- No tests found that verify effective system-prompt application through `Mcp.Net.LLM` agent/session creation.
- No tests found for repeated `RegisterTools` calls on provider clients.
- No tests found for nested tool argument payloads.
- No tests found for the `AgentRegistry` constructor warm-up and default-agent startup interaction.

## Notes

This document captures findings only. No code changes were made as part of this review artifact.
