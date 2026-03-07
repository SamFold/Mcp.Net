# Cross-Session Elicitation Prompt Bug

Last reproduced: 2025-10-30  
Status: **Open** (root cause not yet confirmed)

## Summary

Initiating the delayed `wh40k_extraction_order` tool in two distinct chats intermittently causes the resulting `elicitation/create` prompts to surface in a *third* chat session (`chat/410dd312-502f-466a-bdd6-8f7e1e3d3ce5`, `chat/4b435834-e8dd-49cf-b1f2-6b3dde89bdda`, etc.). The affected chat never invoked the tool, yet receives multiple prompts and the UI becomes blocked.

## Repro Steps

1. Launch Web UI (localhost:3000) and create three sessions.  
   - Session A (Anthropic): ask tool question  
   - Session B (OpenAI): ask tool question  
   - Session C (OpenAI): simple greeting
2. In Sessions A & B run:  
   `Can you use wh40k_extraction_order tool to generate me a Warhammer 40k extraction order?`
3. Remain in Session C.  
4. After the tool delay (~60s) prompts intended for A/B appear in Session C. Badge count increases and chat input is disabled.

## Instrumentation Added

### Frontend

- `chat.ts` keeps only one SignalR `ElicitationRequested` / `ElicitationCancelled` handler to avoid duplicate prompts.
- `ChatContainer` logs prompt state transitions.
- `ChatInput` logs disabled state transitions.

### Backend

- `ChatSession.ExecuteToolCall` logs tool invocation start/completion (session id + SSE transport id) and now injects `_meta.clientSessionId` into `tools/call` arguments.
- `McpServer.SendClientRequestAsync` logs every server-initiated RPC with transport name/session id and the `_meta.clientSessionId` extracted from parameters.

## Current Observations

- The “wrong chat” prompts correlate with the *newest* SSE connection (`SseTransport Session` logged in `Dispatching client request …`).
- Despite adding `_meta.clientSessionId`, server logs still report `(unknown)`, implying the client metadata is not being serialized with the request.
- Timeouts (`Request timed out: tools/call`) precede the reassignment: when the original session exceeds the 60s server timeout, the pending `elicitation/create` is reissued on whatever SSE connection is active (usually the chat the user is viewing).

## Remaining Gaps

- Confirm that the `_meta` block is actually serialized on the wire (inspect JSON payload or deserialize on server side before logging).
- Understand why `CallTool` arguments lose the `_meta.path` after cloning (investigate serialization in `McpClient.CallTool` and the JSON encoder).
- Reconcile client/server timeouts: until the hard-coded 60s server timeout is made configurable, long-running tools will continue to reroute.

## Next Actions

1. Capture outgoing JSON (trace `SendJsonRpcPayloadAsync`) to verify `_meta` structure.
2. If metadata is present, adjust `ExtractClientSessionId` to handle the serialized shape.
3. Make server-side `ClientRequestTimeout` configurable and align it with the client’s SSE timeout increase (120s) to prevent reroute caused by timeouts.
4. Once metadata is observed correctly, re-run repro to see if `clientSessionId` matches the original chat. If it does, work back to why transport is still switching (likely reconnection logic).
