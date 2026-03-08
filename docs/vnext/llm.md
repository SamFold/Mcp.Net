# VNext: Mcp.Net.LLM

## Current status

- Provider clients now honor `ChatClientOptions.SystemPrompt` during construction.
- The library-path agent system-prompt regression is covered at both the agent factory layer and the provider-request layer.
- The nested tool-argument regressions for OpenAI and Anthropic are now in place and currently fail on `main`.
- The 2026-03-08 LLM review still has unresolved high-severity issues around nested tool argument handling and dropped provider failures.

## Goal

- Fix complex nested tool arguments in the OpenAI and Anthropic provider adapters so MCP tool calls preserve object and array payloads.

## Scope

- In scope:
  - add regression coverage for nested object and array tool arguments on the OpenAI and Anthropic adapter paths
  - verify the payloads still bind correctly through the existing server-side tool invocation path
  - implement the smallest adapter change that preserves structured argument values
- Out of scope:
  - provider failure surfacing in `ChatSession`
  - tool re-registration idempotency
  - broader agent-management or Web UI changes unless required by the regression path

## Current slice

1. Preserve nested object and array tool arguments in the OpenAI and Anthropic provider adapters as structured JSON values instead of flattening them to strings.
2. Verify the chosen representation still serializes cleanly through `ToolCallRequest` and binds correctly in the existing `ToolInvocationFactory` path.
3. Run the focused LLM regressions first, then the broader adjacent LLM and Web UI tests.

## Next slices

1. Fix dropped provider failures so `ChatSession` surfaces provider errors instead of silently ignoring them.
2. Make provider `RegisterTools` behavior idempotent so refreshes cannot duplicate model-facing tool definitions.

## Open decisions

- Use `JsonElement` for nested object and array values unless a later slice exposes a concrete need for recursive CLR conversion.

## Verification checklist

- Add failing regression tests before implementation when feasible.
- Run focused adapter tests for both providers.
- Run the broader `Mcp.Net.Tests` LLM slice and adjacent Web UI chat tests after the focused pass.
