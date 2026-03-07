# Bug Report: MCP Responses Follow “Last Connected” Session

## Summary
- Incoming traffic (browser → server) always carries `Mcp-Session-Id`, so **POST** requests like `tools/call` are dispatched to the correct per-session `SseTransport`.
- Outbound traffic no longer relies on a global `_transport`. `McpServer` now captures the active `IServerTransport` in an `AsyncLocal` scope for each in-flight request, and `SendClientRequestAsync` resolves the transport from that scope.
- Tool and elicitation responses triggered while the original request is still executing now route back to the correct session. Server-initiated work launched outside of that scope (background timers, cross-session notifications, etc.) still lacks a transport lookup and will throw.

## Incoming Request Path (Browser → MCP Server)
1. React store calls `signalRService.sendMessage(sessionId, content)`; `sessionId` comes from the current route.
2. `ChatHub.SendMessage` forwards to the session’s `SignalRChatAdapter`.
3. `SignalRChatAdapter` forwards to its `ChatSession`, which invokes the tool through its dedicated `McpClient`.
4. `McpClient` POSTs a JSON-RPC `tools/call`, including `Mcp-Session-Id`.
5. `SseTransportHost.HandleMessageAsync` grabs that header, uses `InMemoryConnectionManager` to fetch the correct `SseTransport`, and hands it the payload.
6. The transport feeds the request into `McpServer.HandleMessageAsync`, so the call stays tied to the originating chat.

## Outbound Request Path (MCP Server → Browser)
1. Tool execution needs user input; `ElicitationService` calls `_server.SendClientRequestAsync("elicitation/create", …)`.
2. Each SSE connection (one per chat session) calls `SseTransportHost.HandleSseConnectionAsync`, which invokes `McpServer.ConnectAsync(transport)`.
3. `ConnectAsync` captures the transport in an ambient scope instead of overwriting a singleton field. Events (`OnRequest`, `OnNotification`, etc.) are still wired per transport, so inbound traffic keeps working.
4. `SendClientRequestAsync` resolves the transport from the current scope and calls `transport.SendRequestAsync(...)`. Requests run on background threads (outside the captured scope) now fail fast with an explicit exception, preventing silent misrouting.
5. Assistant fallback messages are generated entirely within the chat session and do not call `SendClientRequestAsync`, so they appear in the correct chat while the prompt appears in a different one.

## Impact
- Tool responses and elicitations triggered by the active request now go back to the correct session; the customer-facing timeout we observed is resolved.
- Server pushes that run outside an active request surface an `InvalidOperationException` (“No active transport context...”) until we provide a session-aware send API. This makes the failure obvious but still blocks those scenarios.

## Status
- ✅ **Inbound request path fixed**: `McpServer.ConnectAsync` captures the specific transport that raised `OnRequest`, and `ProcessRequestAsync` sends the response back over that transport. Tool replies once again return to the originating chat.
- ✅ **Outbound routing during active requests fixed**: `SendClientRequestAsync` now uses the captured transport scope, so elicitations and tool results reply to the session that triggered them.
- ✅ **Transport registry centralised**: `SseTransportHost` now delegates registration/lookup/cleanup to the shared `IConnectionManager`, so there is a single authoritative session map.
- ✅ **Session-aware send exposed**: `SendClientRequestAsync(sessionId, …)` resolves transports via the shared connection manager (or direct fallback), so background jobs can target the right client explicitly.
- ✅ **Ambient session scope removed**: `PushSessionContext`/`EnterSessionScope` are gone; tool invocations flow the session identifier via `ToolInvocationContextAccessor`, and services like `IElicitationService` opt into it explicitly.
- ✅ **SSE host slimmed**: origin/auth checks now live in `SseRequestSecurity`, and JSON-RPC parsing/dispatch moved to `SseJsonRpcProcessor`, leaving `SseTransportHost` as a lightweight coordinator.
- ✅ **Server registries extracted**: tools, resources, prompts, and completions now live in dedicated services so `McpServer` focuses on transport orchestration.
- ✅ **Transport stack flattened**: `SseTransport` now implements `IServerTransport` directly, removing the `ServerTransportBase` layer.
- 🚧 **Out-of-band server pushes still need adoption**: Callers that previously relied on the implicit scope must be updated to pass session ids; until then they will hit the explicit `InvalidOperationException` guard.

## Why Assistant Responses Behave
- Assistant replies are emitted by `ChatSession` as normal `assistant` messages.
- Those messages never touch `McpServer.SendClientRequestAsync`; they flow through the SignalR adapter for the originating session, so they appear in the correct chat.
- Only server-initiated JSON-RPC requests (`elicitation/create`, future notifications, etc.) use `_transport`, exposing the routing bug.

## Root Cause in Plain Terms
- `InMemoryConnectionManager` keeps a dictionary of session → transport and is consulted for inbound POSTs.
- `McpServer` now records the active transport per request via `AsyncLocal`, so replies stay with the caller.
- There is still no session-aware lookup for server pushes that originate outside the request scope.

## Current Transport Components
- **`InMemoryConnectionManager`** (DI singleton implementing `IConnectionManager`)
  - Keeps the authoritative map of `sessionId → IServerTransport`.
  - Used by both the POST handler and server-initiated flows to locate transports.
- **`SseTransportHost`**
  - ASP.NET middleware that accepts SSE `GET`/`POST`, creates `SseTransport`, and immediately registers each transport with the shared connection manager before handing it to `McpServer`.
  - Looks up transports through the connection manager when dispatching incoming HTTP POSTs.

## Next Steps
1. **Finish transport/server simplification**
   - Push more orchestration into DI: register tool/resource/prompt/completion services in the builders and remove remaining legacy hooks (old logging scopes) once unused.
   - Collapse remaining logging helpers (`JsonRpcLoggingExtensions`, redundant scopes) or move them into the services where they’re needed. Delete unused options/wrappers that the new services replaced.
   - Audit the stdio path to reuse the shared connection manager and surface only the primitives `McpServer` needs (less `async` local plumbing).

2. **Continue transport flattening**
   - Mirror the SSE refactor for stdio: drop `ServerMessageTransportBase`, keep only the minimal parsing/sending logic, and confirm no client transports depend on the removed base classes.
   - Split `SseTransportHost` further if needed (handshake vs. dispatch) now that the transport itself takes on more responsibility.

3. **Regression coverage**
   - Add integration tests that spin up multiple SSE sessions and assert both tool responses and server-driven messages return only to the session that triggered them.
   - Keep a repro for elicitations specifically to ensure the bug stays closed.
