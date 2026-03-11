# ChatCompletionStream Tests

## Change Summary

- Added direct `ChatCompletionStream` regression tests for result-only execution, stream-then-result consumption, result-then-stream rejection, single-enumerator enforcement, cancellation propagation, early-exit cancellation, and streaming failure surfacing.
- Updated the `Mcp.Net.LLM` vnext track to record the completed test slice and point the next slice back to the streamed payload-shape decision.

## Why

- `ChatCompletionStream` now carries the provider transport contract for both direct callers and downstream session/UI consumers.
- The class had meaningful lifecycle and cancellation behavior but only indirect coverage, so direct tests reduce the risk of transport regressions before any further API work above the stream boundary.

## Major Files Changed

- `Mcp.Net.Tests/LLM/Models/ChatCompletionStreamTests.cs`
- `docs/vnext/llm.md`

## Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter ChatCompletionStreamTests`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.LLM|FullyQualifiedName~Mcp.Net.Tests.Agent.Core.ChatSessionTests|FullyQualifiedName~Mcp.Net.Tests.WebUi.Adapters.SignalR.SignalRChatAdapterTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release` still reports two unrelated pre-existing failures in `ChatFactoryTests` and `StdioTransportTests`.
