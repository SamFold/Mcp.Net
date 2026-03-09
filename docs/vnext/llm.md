# VNext: Mcp.Net.LLM

## Current status

- Provider clients now honor `ChatClientOptions.SystemPrompt` during construction.
- The library-path agent system-prompt regression is covered at both the agent factory layer and the provider-request layer.
- Nested tool arguments now survive the OpenAI and Anthropic adapter paths as structured values, with server-binding compatibility coverage in place.
- `ChatSession` now surfaces provider `System` and `Error` responses through the existing assistant-message event flow on both initial and post-tool turns.
- `Mcp.Net.LLM` now builds and passes the focused LLM/Web UI slice on `OpenAI` `2.9.1` and `Anthropic.SDK` `5.10.0`.
- The standalone SDK probe now captures live OpenAI and Anthropic text, tool, reasoning, and error shapes under `artifacts/llm-probe/`.
- The `ChatSession` spec has been revised away from the earlier flat five-kind item proposal to a block-based transcript model:
  - transcript entries: `User`, `Assistant`, `ToolResult`, `Error`
  - assistant blocks: `Text`, `Reasoning`, `ToolCall`
  - typed replay/history transforms are now considered required architecture
- The 2026-03-08 LLM review still has unresolved issues around tool re-registration, agent registry startup behavior, and persisted agent settings.

## Goal

- Replace the current text-first `ChatSession`/`LlmResponse` model with a contract-breaking block-based transcript and event model that matches modern provider behavior.
- Delete superseded `Mcp.Net.LLM` files and abstractions when the cleaner replacement exists instead of carrying tech-debt shims.

## Scope

- In scope:
  - add transcript entry records for `User`, `Assistant`, `ToolResult`, and `Error`
  - add assistant content blocks for `Text`, `Reasoning`, and `ToolCall`
  - replace `IChatSessionEvents` with typed transcript/activity events
  - replace `LlmResponse` and `MessageType` with a typed provider-output model
  - add an explicit replay/history transform seam for provider/model-safe transcript replay
  - migrate the Web UI DTO/adapter/storage slice to the new discriminated model as part of the same vertical slice
  - delete obsolete `Mcp.Net.LLM` files and adapters once their replacements are in place
- Out of scope:
  - preserving compatibility with the current Web UI contracts
  - preserving `AssistantMessageReceived`, `ToolExecutionUpdated`, or `ThinkingStateChanged`
  - a fully designed streaming transport protocol
  - provider `RegisterTools` idempotency

## Current slice

1. Replace the current `ChatSession` message/event contracts with the block-based transcript model described in `docs/llm-chat-session-item-model.md`.
2. Replace `LlmResponse`/`MessageType` with typed provider outputs that can express reasoning, text, tool calls, and typed failures directly.
3. Add focused regressions for transcript entry emission, tool result/error handling, and same-model replay safety.
4. Migrate the Web UI adapter and persisted message shape in the same slice instead of adding compatibility shims.

## Next slices

1. Add streaming block-delta support on top of the new transcript model without changing transcript semantics.
2. Harden replay transforms for model-switch and cross-provider handoff cases using captured probe fixtures and targeted integration tests.
3. Make provider `RegisterTools` behavior idempotent so refreshes cannot duplicate model-facing tool definitions.

## Open decisions

- Should `IChatClient` remain conversation-stateful after the typed output refactor, or should a later slice move it to an explicit context-driven request API?

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Add regressions for ordered assistant blocks containing reasoning, text, and tool calls.
- Add regressions for tool execution appending `ToolResult` entries instead of mutating transcript state.
- Add regressions for provider failures surfacing as `Error` entries instead of assistant text.
- Add replay/history transform coverage for same-model preservation and degraded cross-model/provider replay.
