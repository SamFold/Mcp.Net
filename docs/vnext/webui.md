# VNext: Mcp.Net.WebUi

## Current status

- The primary chat experience already works without `AgentDefinition` selection by using `DefaultLlmSettings`, per-session MCP clients, and `ChatSession`.
- The legacy agent-driven endpoints, DTOs, startup wiring, and chat-factory branches have been removed.
- The SignalR chat adapter now owns session-start notification directly after the `ChatSession` session-start seam was removed.
- The standalone TypeScript demo client has been realigned with the surviving session/transcript/tool contract and no longer carries the dead agent-era browser shell.

## Goal

- Harden the end-to-end Web UI smoke harness so it truly verifies the surviving REST, SignalR, transcript, and tool-update surface with real provider-backed sessions.

## What

- Tighten `scripts/SmokeTest` so it asserts the current Web UI contract instead of only observing best-effort behavior.
- Verify abort, leave-session, and tools-updated flows against the live SignalR adapter behavior.
- Keep the current non-agent chat flow, session history, transcript DTOs, and local-tool execution path green while hardening the harness.

## Why

- The browser realignment is only useful if the integration harness actually catches regressions in the surviving Web UI runtime surface.
- The current smoke test passes while still treating some important flows as advisory, especially abort, group leave, and tool updates.
- A trustworthy smoke harness is the fastest way to validate Web UI against real Anthropic/OpenAI-backed sessions while the backend seam decisions continue.

## How

### 1. Tighten the smoke assertions

- Turn `ToolsUpdated`, `AbortTurn`, and `LeaveSession` from informational logging into real pass/fail assertions.
- Assert the current transcript and tool-execution payload shape, not just message counts.
- Keep provider-backed runs deterministic enough that failures point at real regressions rather than harness flakiness.

### 2. Keep the chat path stable

- Preserve the existing metadata/history behavior and adapter lifecycle.
- Keep the default-model flow and explicit `anthropic`/`openai` runs green while tightening assertions.
- Avoid accidental behavior changes in tool refresh or prompt/resource/completion features.

### 3. Revisit factory composition after the harness is trustworthy

- Revisit whether `ChatFactory` should move onto `IChatSessionFactory` once the Web UI regression harness is stronger.
- Keep SignalR adapter, session host, and tool-refresh behavior green.

## Scope

- In scope:
  - tighten `scripts/SmokeTest` coverage for the current REST, SignalR, transcript, abort, leave, and tool-update surface
  - preserve the default-model and explicit-provider chat paths while increasing assertion quality
  - keep local tool execution and transcript DTO behavior covered by a real end-to-end harness
  - stay aligned with the current `ChatSession` runtime API while the shared runtime evolves
- Out of scope:
  - redesigning the Web UI chat UX
  - reintroducing agent-definition concepts
  - changing the MCP transport/auth flow
  - moving Web UI session construction onto `IChatSessionFactory` in this slice

## Current slice

1. Move the browser model picker off its duplicated local catalog and onto a server-backed chat-model endpoint.
2. Keep the React settings store, session creation flow, and session-config modal stable while model metadata starts loading asynchronously.
3. Keep the current multimodal transcript and tool-produced image rendering path stable while the picker source-of-truth moves.

## Next slices

1. Move the browser model picker off its duplicated local catalog and onto a server-backed chat-model endpoint.
2. Tighten `scripts/SmokeTest` so `AbortTurn`, `LeaveSession`, and `ToolsUpdated` are real assertions.
3. Add browser-level smoke coverage for multimodal input and tool-produced image output.
4. Revisit whether chat/session history abstractions belong in `Mcp.Net.Agent` or should live closer to Web UI.

## Recently completed

- Added a local `generate_image` tool backed by OpenAI image generation so normal chat models can choose image creation through the existing tool-call loop.
- Added a generated-image artifact store plus `/api/generated-images/{artifactId}` so tool results can return stable image resource links instead of raw inline bytes.
- Updated the React transcript and tool-execution renderers to display tool-produced image resource links inline and resolve relative API paths correctly.
- Refreshed the Web UI default provider/model settings to the current Anthropic and OpenAI baselines.
- Aligned sample config, smoke-test inputs, and user-facing docs so the app no longer advertises stale model IDs.
- Added typed user-content transport to Web UI transcript DTOs so history and live SignalR delivery preserve text-plus-image user turns.
- Updated the React client store, transcript normalization, and SignalR service to send typed user content parts and render assistant image blocks.
- Added browser-side image upload previews plus inline rendering for both user images and OpenAI-generated assistant images.
- Added typed multimodal user-input DTOs plus `ChatHub.SendMessageParts(...)` so the surviving SignalR chat surface can forward ordered text+image input to `ChatSession`.
- Kept existing text-only hub callers intact while adding DTO-to-domain conversion for `UserContentPart`.
- Guarded session-title generation so image-only first turns do not trigger blank title requests.
