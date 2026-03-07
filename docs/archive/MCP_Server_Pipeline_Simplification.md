**Mcp.Net.Server Pipeline Simplification Plan**

- **Goal**  
  Replace the event-first transport flow with a linear request → handler → response pipeline (host → server → services) so transport/session routing is debuggable, AsyncLocal goes away, and transports stay thin adapters.

- **Why (current pain and motivation)**  
  - We hit transport misrouting bugs and found them hard to debug because SSE ingress bounces through multiple layers (`SseTransportHost` → `SseJsonRpcProcessor` → `SseTransport` events → `McpServer`). Call stacks don’t show which transport/session actually handled the request.  
  - Events invert control and obscure the session/transport used to complete server-initiated calls; `_pendingClientRequests` hangs if responses don’t re-surface.  
  - AsyncLocal plumbing (`ToolInvocationContextAccessor`) exists only to recover session ids—an anti-pattern compared to explicit parameters.  
  - A straightforward host → server → service flow matches typical API/controller mental models and makes tracing transport use and failures much easier.

- **Major code files touched by this work (and why)**  
  - `Mcp.Net.Server/McpServer.cs`: new request/response entry points, `_pendingClientRequests` completion, removal of event wiring, context propagation changes.  
  - `Mcp.Net.Server/Transport/Sse/SseTransportHost.cs`: host-level parsing/dispatch, response completion, close/error handling per session.  
  - `Mcp.Net.Server/Transport/Sse/SseJsonRpcProcessor.cs`: remove/retire; host owns parsing/dispatch (may keep a pure parser helper only).  
  - `Mcp.Net.Server/Transport/Sse/SseTransport.cs`: trimmed to outbound send + metadata; remove inbound events.  
  - `Mcp.Net.Server/Transport/Stdio/StdioTransport.cs` (+ builder): mirror SSE changes for stdio ingress dispatch.  
  - `Mcp.Net.Server/Services/ToolService.cs`: accept explicit context/session, drop AsyncLocal accessor.  
  - `Mcp.Net.Server/Elicitation/ElicitationService.cs`: require explicit context/session for client calls.  
  - `Mcp.Net.Server/Extensions/CoreServerExtensions.cs` (and builder/DI wiring): stop registering `ToolInvocationContextAccessor`; register new router/entry points instead.  
  - Tests/harnesses: `Mcp.Net.Tests/Integration/TestServerHarness.cs`, `Mcp.Net.Tests/Server/*` updated to use new entry points and context signatures.

- **Current Pain Points**  
  - Transport hosts bounce through `SseJsonRpcProcessor` → `SseTransport` events → `McpServer` handlers; call graph is inverted and hard to trace.  
  - AsyncLocal/`ToolInvocationContextAccessor` only exists to smuggle session ids into tool/elicitation services.  
  - Server-initiated calls rely on evented responses; a missing response path strands `_pendingClientRequests`.

- **Target Architecture Overview**  
  1. **Host-Level Dispatch**  
     - Hosts parse JSON-RPC, resolve session id, build `ServerRequestContext`, and call `HandleRequestAsync` directly. No `OnRequest/OnNotification/OnResponse`.  
  2. **Server Entry Points**  
     - `HandleRequestAsync(ServerRequestContext context)` replaces `HandleRequestWithTransport`/event wiring.  
     - `HandleClientResponseAsync(sessionId, JsonRpcResponseMessage response)` (or similar) completes `_pendingClientRequests` when clients reply.  
  3. **Server-Initiated Calls**  
     - Keep `_pendingClientRequests` (or a renamed equivalent) but drive it from host-delivered responses and transport close/error callbacks, not events.  
  4. **Context Propagation**  
     - Services take `ServerRequestContext` (or explicit `sessionId`) instead of using AsyncLocal.  
     - Tool handlers and elicitation callers receive the context explicitly; transports stay outbound-only.

- **Work Breakdown (incremental)**

  1. **Add Request Context + Entry Points**  
     - Define `ServerRequestContext(string SessionId, string TransportId, JsonRpcRequestMessage Request, CancellationToken CancellationToken = default, IReadOnlyDictionary<string,string>? Metadata = null)`.  
     - Add `Task<JsonRpcResponseMessage?> HandleRequestAsync(ServerRequestContext context)` (requests) and `Task HandleNotificationAsync(ServerRequestContext context)` (notifications) and route existing logic through them.  
     - Add `HandleClientResponseAsync(sessionId, JsonRpcResponseMessage response)` (and close/error hooks) to complete/cancel `_pendingClientRequests`.  
     - Status: DONE (request entry point, notification routed via SSE, client response entry point, server close handler to cancel pending requests, unit coverage).

  2. **Rewire Hosts (SSE first)**  
     - In `SseTransportHost`, parse the POST body, call `HandleRequestAsync` for requests, send the response immediately (or 202 for notifications).  
     - On client responses, call `HandleClientResponseAsync`; on close/error, cancel pending for that session.  
     - Keep stdio inbound dispatch the same way once SSE parity is verified.  
     - Status: DONE for SSE ingress (requests/notifications/responses use the server entry points, SSE host calls close cancellation). Stdio now mirrors the direct dispatch pattern.

  3. **Trim Transports**  
     - Remove inbound events from `SseTransport`/`StdioTransport`; keep only outbound `SendAsync/SendRequestAsync/SendNotificationAsync` and metadata.  
     - `IConnectionManager` remains the transport lookup for outbound sends.  
     - Status: SSE inbound events removed; stdio inbound events bypassed in the read loop (events no longer used for server dispatch).

  4. **Service Layer Migration**  
     - Keep services transport-agnostic: `ToolService.ExecuteAsync` and handlers accept explicit `sessionId` (minimal context), not full HTTP context; delete `ToolInvocationContextAccessor` usage.  
     - `ElicitationService.RequestAsync` requires explicit `sessionId` (or a minimal context wrapper); callers provide it.  
     - Update DI helpers/tests/samples to drop the accessor registration; register new entry points/router as needed.

  5. **Tests & Harnesses**  
     - Integration harness feeds requests via `HandleRequestAsync` and completes server-initiated calls via `HandleClientResponseAsync`.  
     - Add regression test: server-initiated request completes on matching client response for the same session; timeout/close cancels.

- **Risks & Mitigations**  
  - **Client response completion**: ensure hosts call `HandleClientResponseAsync` and close/error hooks so `_pendingClientRequests` never hang.  
  - **Routing correctness**: validate multi-transport sessions in integration tests after SSE refactor, then mirror for stdio.  
  - **DI breakage**: migrate services/handlers off AsyncLocal before removing the accessor registration.

- **Success Criteria**  
  - No inbound transport events; hosts call server entry points directly.  
  - `_pendingClientRequests` is completed/cancelled via host callbacks, not event wiring.  
  - No `AsyncLocal` for session scoping; services receive context explicitly.  
  - SSE and stdio tests cover request handling and server-initiated response completion on the correct session.

- **Next Step**  
  Start the service-layer/DI cleanup: migrate tool/elicitation services off `AsyncLocal` now that both SSE and stdio ingress use the explicit entry points. The stdio read loop now lives in a host (`StdioIngressHost`); `StdioTransport` is outbound-only, and `McpServer.ConnectAsync` no longer wires inbound events. Breaking change: `IServerTransport` no longer exposes OnRequest/OnNotification/OnResponse—custom transports must route ingress via host components that call the server entry points. Next cleanup is DI/service plumbing once the event API removal is absorbed.
