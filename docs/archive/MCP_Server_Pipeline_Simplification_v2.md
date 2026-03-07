# Mcp.Net.Server Pipeline Simplification Plan (v2)

**Status**: Ready for Implementation
**Updated**: 2025-11-24
**Changes from v1**: Incorporated review feedback - firmer decisions, clearer scope, explicit DI changes

---

## Goal

Replace the event-first transport flow with a linear request → handler → response pipeline (host → server → services) so transport/session routing is debuggable, AsyncLocal goes away, and transports stay thin adapters.

---

## Why (Current Pain and Motivation)

- We hit transport misrouting bugs and found them hard to debug because SSE ingress bounces through multiple layers (`SseTransportHost` → `SseJsonRpcProcessor` → `SseTransport` events → `McpServer`). Call stacks don't show which transport/session actually handled the request.
- Events invert control and obscure the session/transport used to complete server-initiated calls; `_pendingClientRequests` hangs if responses don't re-surface.
- AsyncLocal plumbing (`ToolInvocationContextAccessor`) exists only to recover session ids—an anti-pattern compared to explicit parameters.
- A straightforward host → server → service flow matches typical API/controller mental models and makes tracing transport use and failures much easier.

---

## Target Architecture Overview

### 1. Host-Level Dispatch

Hosts parse JSON-RPC, resolve session id, build `ServerRequestContext`, and call server entry points directly. **No events.**

### 2. Server Entry Points

**McpServer** exposes:
```csharp
Task<JsonRpcResponseMessage> HandleRequestAsync(ServerRequestContext context)
Task HandleNotificationAsync(ServerRequestContext context)
Task HandleClientResponseAsync(string sessionId, JsonRpcResponseMessage response)
Task HandleTransportClosedAsync(string sessionId)
Task HandleTransportErrorAsync(string sessionId, Exception error)
```

### 3. Server-Initiated Calls

Keep `_pendingClientRequests` but drive it from host-delivered responses and transport close/error callbacks, not events.

### 4. Context Propagation

Services take **explicit `sessionId`** (not full `ServerRequestContext`) to avoid coupling to transport/HTTP concerns. No AsyncLocal.

### 5. SseJsonRpcProcessor Removal

**Decision: Remove entirely.** Move parsing and dispatch logic into `SseTransportHost`. Hosts own the full HTTP → JSON-RPC → Server flow.

---

## Core Types

### ServerRequestContext

```csharp
/// <summary>
/// Encapsulates the context for a single server request.
/// </summary>
public sealed record ServerRequestContext
{
    /// <summary>
    /// The unique session identifier for this request.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// The transport identifier that received this request.
    /// </summary>
    public required string TransportId { get; init; }

    /// <summary>
    /// The JSON-RPC request message.
    /// </summary>
    public required JsonRpcRequestMessage Request { get; init; }

    /// <summary>
    /// Cancellation token for this request (tied to HTTP request lifetime).
    /// </summary>
    public CancellationToken CancellationToken { get; init; } = default;

    /// <summary>
    /// Optional metadata (e.g., "UserId", "IP", "Claim_*" for auth).
    /// Services should NOT depend on this; it's for logging/auditing.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
```

**Key Points**:
- Immutable `record` for value semantics
- `CancellationToken` enables proper cancellation propagation
- `Metadata` is optional and for observability—services must not couple to it

---

## Major Code Changes

### Files Touched

| File | Change | Reason |
|------|--------|--------|
| **Mcp.Net.Server/McpServer.cs** | Add new entry points; remove event wiring | Linear dispatch |
| **Mcp.Net.Server/Transport/Sse/SseTransportHost.cs** | Parse JSON-RPC and call server directly | Host owns flow |
| **Mcp.Net.Server/Transport/Sse/SseJsonRpcProcessor.cs** | **Delete** | Logic moved to host |
| **Mcp.Net.Server/Transport/Sse/SseTransport.cs** | Remove `OnRequest/OnNotification/OnResponse` events | Outbound-only |
| **Mcp.Net.Server/Transport/Stdio/StdioTransport.cs** | Mirror SSE changes | Consistency |
| **Mcp.Net.Server/Services/ToolService.cs** | Drop AsyncLocal wrapper; explicit `sessionId` | No hidden context |
| **Mcp.Net.Server/Services/ToolInvocationContextAccessor.cs** | **Delete** | AsyncLocal removed |
| **Mcp.Net.Server/Elicitation/ElicitationService.cs** | Require explicit `sessionId` (no fallback) | No hidden context |
| **Mcp.Net.Server/Extensions/CoreServerExtensions.cs** | Stop registering accessor; add new DI entries | DI cleanup |
| **Mcp.Net.Core/Transport/IServerTransport.cs** | Remove event declarations | **Breaking change** |
| **Mcp.Net.Tests/Integration/TestServerHarness.cs** | Use new entry points | Test updates |
| **Mcp.Net.Tests/Server/*** | Update to direct invocation | Test updates |

---

## Work Breakdown (Incremental)

### Phase 1: Foundation (Must-Haves)

**1.1 Define ServerRequestContext**
- Location: `Mcp.Net.Server/Models/ServerRequestContext.cs`
- Add record with required properties + `CancellationToken` + optional `Metadata`
- Document metadata conventions (UserId, IP, Claim_*)
- ✅ Unit tests

**1.2 Add Server Entry Points**
- Location: `Mcp.Net.Server/McpServer.cs`
- Add:
  ```csharp
  public Task<JsonRpcResponseMessage> HandleRequestAsync(ServerRequestContext context)
  public Task HandleNotificationAsync(ServerRequestContext context)
  public Task HandleClientResponseAsync(string sessionId, JsonRpcResponseMessage response)
  public Task HandleTransportClosedAsync(string sessionId)
  public Task HandleTransportErrorAsync(string sessionId, Exception error)
  ```
- Keep old event handlers temporarily for parallel operation
- Route existing logic through new methods
- ✅ Unit tests for new entry points

**1.3 Implement Pending Request Completion**
- Update `HandleClientResponseAsync` to complete `_pendingClientRequests[requestId]`
- Implement `HandleTransportClosedAsync` to cancel all pending for that session
- Implement `HandleTransportErrorAsync` to cancel/fail pending for that session
- ✅ Test that pending requests complete/cancel properly

---

### Phase 2: SSE Host Refactor (Must-Haves)

**2.1 Move Parsing into SseTransportHost**
- Location: `Mcp.Net.Server/Transport/Sse/SseTransportHost.cs`
- In `HandleMessageAsync`:
  - Read body (already done)
  - Parse JSON-RPC payload (move from `SseJsonRpcProcessor`)
  - Validate protocol version (move from processor)
  - Build `ServerRequestContext` with sessionId, transportId, request, cancellation token
  - Call `_server.HandleRequestAsync(context)` for requests
  - Call `_server.HandleNotificationAsync(context)` for notifications
  - Call `_server.HandleClientResponseAsync(sessionId, response)` for responses
  - Return 202 Accepted
- ✅ Handle errors and return appropriate HTTP status codes

**2.2 Wire Close/Error Handlers**
- In `HandleSseConnectionAsync`:
  - On connection close: `await _server.HandleTransportClosedAsync(sessionId)`
  - On error: `await _server.HandleTransportErrorAsync(sessionId, exception)`
- Ensures pending requests don't hang

**2.3 Delete SseJsonRpcProcessor**
- Location: `Mcp.Net.Server/Transport/Sse/SseJsonRpcProcessor.cs`
- **Delete file entirely**
- Remove from DI registration
- Remove from host constructor

**2.4 Integration Tests**
- Test: Request → Response flow
- Test: Notification handling (no response)
- Test: Server-initiated request completes on client response
- Test: Pending requests cancelled on transport close
- Test: Pending requests failed on transport error
- Test: Multiple concurrent requests

---

### Phase 3: Service Layer Cleanup (Must-Haves)

**3.1 Update ToolService**
- Location: `Mcp.Net.Server/Services/ToolService.cs`
- Remove `_contextAccessor` field and constructor parameter
- Remove `using var scope = _contextAccessor.Push(sessionId)` wrapper
- Handler signature already accepts `sessionId` explicitly—keep as-is
- ✅ Update tests

**3.2 Update ElicitationService**
- Location: `Mcp.Net.Server/Elicitation/ElicitationService.cs`
- Remove `_contextAccessor` field and constructor parameter
- Change `RequestAsync` signature:
  ```csharp
  // OLD: string? sessionId = null
  // NEW: string sessionId (required)
  Task<ElicitationResult> RequestAsync(
      ElicitationPrompt prompt,
      string sessionId,
      CancellationToken cancellationToken = default)
  ```
- Remove fallback: `sessionId ?? _contextAccessor.SessionId`
- Update error messages to remove mention of "tool invocation context"
- ✅ Update tests

**3.3 Delete ToolInvocationContextAccessor — COMPLETED**
- Location: `Mcp.Net.Server/Services/ToolInvocationContextAccessor.cs`
- Deleted file (interface + implementation); removed all `using` references.
- DI updated: removed `services.AddSingleton<IToolInvocationContextAccessor, ToolInvocationContextAccessor>()` (and builder/example registrations).
- Elicitation/tool services now require explicit `sessionId`; no AsyncLocal fallback.
- ✅ Compilation/tests updated.

**3.4 Update DI Registration — COMPLETED**
- Location: `Mcp.Net.Server/Extensions/CoreServerExtensions.cs`
- Removed accessor registration; no replacement required.

---

### Phase 4: Stdio Parity (Must-Haves)

**4.1 Implement StdioTransportHost**
- Location: `Mcp.Net.Server/Transport/Stdio/StdioTransportHost.cs` (or update existing)
- Mirror SSE approach:
  - Read from stdin
  - Parse JSON-RPC
  - Build `ServerRequestContext`
  - Call appropriate server method
  - Write response to stdout
- Wire close/error handlers

**4.2 Integration Tests**
- Test: Stdio request handling
- Test: Stdio notifications
- Test: Server-initiated requests via stdio
- Test: Clean shutdown

---

### Phase 5: Transport Cleanup (Breaking Changes) — COMPLETED

**5.1 Remove Events from IServerTransport**
- Location: `Mcp.Net.Core/Transport/IServerTransport.cs`
- DONE: Events removed; interface is outbound-only. **Breaking change** for custom transports.
- Action: Add CHANGELOG entry and migration note for downstream implementers.

**5.2 Remove Event Handlers from McpServer**
- Location: `Mcp.Net.Server/McpServer.cs`
- DONE: ConnectAsync no longer wires inbound events; `HandleRequestWithTransport` removed. Tests green.

**5.3 Remove Event Publishing from Transports**
- Location: `Mcp.Net.Server/Transport/Sse/SseTransport.cs`
- DONE: SSE and stdio transports are outbound-only; ingress handled by hosts (`SseJsonRpcProcessor`, `StdioIngressHost`).
- ✅ Tests updated.

**5.4 Update All Tests**
- Location: `Mcp.Net.Tests/**/*`
- DONE: Tests now call server entry points; event mocks removed. Coverage maintained.

---

### Phase 6: Documentation & Cleanup (Must-Haves)

**6.1 Update Documentation**
- TODO: add migration note for custom transports (events removed; host-driven ingress required). Update README/implementation docs as needed.

**6.2 Add CHANGELOG Entry**
- TODO: Document breaking change (event removal) and migration guidance; note ToolInvocationContextAccessor removal when complete; version bump recommendation.

---

## Deferred / Nice-to-Have (Not in Scope)

### Router Abstraction
**Deferred**: Keep dispatch logic inside `McpServer` for now. A separate `JsonRpcRouter` class adds indirection without clear reuse pressure. Revisit if we add multiple server implementations.

### Middleware Pipeline
**Deferred**: Cross-cutting concerns (logging, auth, rate limiting) can be handled in hosts or via existing ASP.NET Core middleware. Adding a custom pipeline increases complexity. Revisit after core refactor proves stable.

### Enhanced Metadata Usage
**Deferred**: Keep metadata dictionary simple. Document conventions (UserId, IP, Claim_*) but don't build metadata-dependent features yet. Let real-world usage guide enhancements.

---

## DI Changes Summary

### Services to Remove
```csharp
// DELETE
services.AddSingleton<IToolInvocationContextAccessor, ToolInvocationContextAccessor>();
```

### Services to Keep (No Changes Needed)
```csharp
services.AddSingleton<IConnectionManager, ConnectionManager>();
services.AddSingleton<McpServer>();
services.AddSingleton<IToolService, ToolService>();
services.AddSingleton<IResourceService, ResourceService>();
services.AddSingleton<IPromptService, PromptService>();
services.AddSingleton<ICompletionService, CompletionService>();
services.AddSingleton<IElicitationService, ElicitationService>();
```

### Constructor Changes
```csharp
// OLD: ToolService constructor
public ToolService(
    ServerCapabilities capabilities,
    ILogger<ToolService> logger,
    IToolInvocationContextAccessor contextAccessor)  // REMOVE THIS

// NEW: ToolService constructor
public ToolService(
    ServerCapabilities capabilities,
    ILogger<ToolService> logger)
```

```csharp
// OLD: ElicitationService constructor
public ElicitationService(
    McpServer server,
    ILogger<ElicitationService> logger,
    IToolInvocationContextAccessor contextAccessor)  // REMOVE THIS

// NEW: ElicitationService constructor
public ElicitationService(
    McpServer server,
    ILogger<ElicitationService> logger)
```

---

## Risks & Mitigations

### Risk 1: Client Response Completion
**Risk**: If hosts don't call `HandleClientResponseAsync`, pending requests hang forever.

**Mitigation**:
- Add integration test that verifies completion
- Add timeout mechanism (already exists: `ClientRequestTimeout`)
- Log warning if pending request count grows unexpectedly
- Test transport close/error cancellation explicitly

### Risk 2: Routing Correctness
**Risk**: Multi-session scenarios might route responses to wrong session.

**Mitigation**:
- Validate sessionId in all server methods
- Integration tests with multiple concurrent sessions
- Add assertions in `HandleClientResponseAsync`:
  ```csharp
  if (!_pendingClientRequests.TryRemove(response.Id, out var tcs))
  {
      _logger.LogWarning("Response for unknown request: {Id}", response.Id);
      return;
  }
  ```

### Risk 3: DI Breakage
**Risk**: Removing `ToolInvocationContextAccessor` breaks existing code.

**Mitigation**:
- Update all services before removing registration
- Compile-time errors will catch references
- Add migration notes in CHANGELOG
- Update all examples and tests

### Risk 4: Breaking Changes to IServerTransport
**Risk**: Custom transport implementations break when events removed.

**Mitigation**:
- Document breaking change in CHANGELOG
- Provide migration guide with before/after examples
- Consider deprecation period if post-1.0 (mark `[Obsolete]` first)
- Show how to convert event-based transport to new pattern

---

## Success Criteria

### Functional Requirements
- ✅ No inbound transport events; hosts call server entry points directly
- ✅ `_pendingClientRequests` completes via `HandleClientResponseAsync`
- ✅ `_pendingClientRequests` cancels via `HandleTransportClosedAsync` / `HandleTransportErrorAsync`
- ✅ No `AsyncLocal` for session scoping; services receive explicit `sessionId`
- ✅ SSE and stdio transports use identical server entry points
- ✅ Notifications handled without generating responses

### Testing Requirements
- ✅ Unit tests for all new server methods
- ✅ Integration test: Request → Response flow
- ✅ Integration test: Server-initiated request completes on client response
- ✅ Integration test: Pending requests cancelled on close
- ✅ Integration test: Pending requests failed on error
- ✅ Integration test: Concurrent multi-session requests
- ✅ Integration test: Notifications don't generate responses

### Code Quality Requirements
- ✅ All compilation warnings resolved
- ✅ All tests pass
- ✅ No AsyncLocal usage remains
- ✅ No event-based dispatch remains
- ✅ Documentation updated
- ✅ CHANGELOG updated with breaking changes

---

## Example: SSE Host Flow (After Refactor)

```csharp
public async Task HandleMessageAsync(HttpContext httpContext)
{
    // 1. Validate origin/auth (existing code)
    if (!await _security.ValidateOriginAsync(httpContext, _logger))
        return;

    var authOutcome = await _security.AuthenticateAsync(httpContext, _logger);
    if (!authOutcome.Success)
        return;

    // 2. Resolve session ID
    var sessionId = httpContext.Request.Headers["Mcp-Session-Id"].ToString();
    if (string.IsNullOrEmpty(sessionId))
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new { error = "Missing session ID" });
        return;
    }

    // 3. Read and parse JSON-RPC payload
    var body = await ReadBodyAsync(httpContext);
    JsonRpcPayload payload;
    try
    {
        payload = ParseJsonRpc(body); // Moved from SseJsonRpcProcessor
    }
    catch (JsonException ex)
    {
        _logger.LogError(ex, "Invalid JSON-RPC payload");
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new JsonRpcError
        {
            Code = (int)ErrorCode.ParseError,
            Message = "Parse error"
        });
        return;
    }

    // 4. Validate protocol version
    if (!ValidateProtocolVersion(httpContext, payload.Method)) // Moved from processor
        return;

    // 5. Build context
    var metadata = BuildMetadata(authOutcome.Result);
    var context = new ServerRequestContext
    {
        SessionId = sessionId,
        TransportId = sessionId, // For SSE, they're the same
        Request = payload.Request,
        CancellationToken = httpContext.RequestAborted,
        Metadata = metadata
    };

    // 6. Dispatch to server
    try
    {
        switch (payload.Kind)
        {
            case JsonRpcPayloadKind.Request:
                var response = await _server.HandleRequestAsync(context);
                httpContext.Response.StatusCode = 202;
                httpContext.Response.Headers["Mcp-Session-Id"] = sessionId;
                break;

            case JsonRpcPayloadKind.Notification:
                await _server.HandleNotificationAsync(context);
                httpContext.Response.StatusCode = 202;
                httpContext.Response.Headers["Mcp-Session-Id"] = sessionId;
                break;

            case JsonRpcPayloadKind.Response:
                await _server.HandleClientResponseAsync(sessionId, payload.Response);
                httpContext.Response.StatusCode = 202;
                httpContext.Response.Headers["Mcp-Session-Id"] = sessionId;
                break;
        }
    }
    catch (McpException mcpEx)
    {
        _logger.LogWarning(mcpEx, "MCP error processing request");
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new JsonRpcError
        {
            Code = (int)mcpEx.Code,
            Message = mcpEx.Message
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unhandled error processing request");
        httpContext.Response.StatusCode = 500;
        await httpContext.Response.WriteAsJsonAsync(new JsonRpcError
        {
            Code = (int)ErrorCode.InternalError,
            Message = "Internal server error"
        });
    }
}
```

**Key Points**:
- Linear flow from HTTP → Server
- No events
- Clear error handling
- Cancellation token propagated
- Metadata built from auth result

---

## Example: Server Entry Point (After Refactor)

```csharp
public async Task<JsonRpcResponseMessage> HandleRequestAsync(ServerRequestContext context)
{
    if (context?.Request == null)
        throw new ArgumentNullException(nameof(context));

    using (_logger.BeginScope(new Dictionary<string, object>
    {
        ["RequestId"] = context.Request.Id,
        ["Method"] = context.Request.Method,
        ["SessionId"] = context.SessionId
    }))
    {
        _logger.LogDebug("Processing request: {Method}", context.Request.Method);

        if (!_methodHandlers.TryGetValue(context.Request.Method, out var handler))
        {
            _logger.LogWarning("Method not found: {Method}", context.Request.Method);
            return CreateErrorResponse(
                context.Request.Id,
                ErrorCode.MethodNotFound,
                "Method not found"
            );
        }

        try
        {
            // Pass sessionId explicitly to handlers that need it
            var paramsJson = JsonSerializer.Serialize(context.Request.Params);
            var result = await handler(paramsJson, context.SessionId);

            _logger.LogDebug("Request {Id} handled successfully", context.Request.Id);
            return new JsonRpcResponseMessage("2.0", context.Request.Id, result, null);
        }
        catch (McpException ex)
        {
            _logger.LogWarning(ex, "MCP exception handling request");
            return CreateErrorResponse(context.Request.Id, ex.Code, ex.Message, ex.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception handling request");
            return CreateErrorResponse(context.Request.Id, ErrorCode.InternalError, ex.Message);
        }
    }
}
```

**Key Points**:
- Synchronous method lookup and dispatch
- Explicit sessionId passing to handlers
- Comprehensive error handling
- Structured logging with context

---

## Example: Tool Handler (After Refactor)

```csharp
// In ToolService
public async Task<ToolCallResult> ExecuteAsync(
    string toolName,
    JsonElement? arguments,
    string sessionId) // Explicit parameter, no AsyncLocal
{
    if (string.IsNullOrWhiteSpace(sessionId))
        throw new ArgumentException("Session identifier required", nameof(sessionId));

    if (string.IsNullOrWhiteSpace(toolName))
        throw new McpException(ErrorCode.InvalidParams, "Tool name cannot be empty");

    Func<JsonElement?, string, Task<ToolCallResult>> handler;
    lock (_sync)
    {
        if (!_handlers.TryGetValue(toolName, out var resolved) || resolved == null)
            throw new McpException(ErrorCode.InvalidParams, $"Tool not found: {toolName}");

        handler = resolved;
    }

    using (_logger.BeginToolScope<string>(toolName))
    using (_logger.BeginTimingScope($"Execute{toolName}Tool"))
    {
        _logger.LogInformation("Executing tool {ToolName}", toolName);

        // Handler already receives sessionId explicitly - no wrapper needed
        var result = await handler(arguments, sessionId).ConfigureAwait(false);

        _logger.LogInformation(
            "Tool {ToolName} executed, IsError={IsError}",
            toolName,
            result.IsError
        );

        return result;
    }
}
```

**Key Points**:
- No AsyncLocal wrapper
- SessionId passed directly to handler
- Clear, linear execution

---

## Example: Elicitation (After Refactor)

```csharp
// In ElicitationService
public async Task<ElicitationResult> RequestAsync(
    ElicitationPrompt prompt,
    string sessionId, // Now required, no fallback
    CancellationToken cancellationToken = default)
{
    if (prompt == null)
        throw new ArgumentNullException(nameof(prompt));

    if (string.IsNullOrWhiteSpace(sessionId))
        throw new ArgumentException("Session identifier required", nameof(sessionId));

    if (string.IsNullOrWhiteSpace(prompt.Message))
        throw new ArgumentException("Elicitation message required", nameof(prompt));

    _logger.LogInformation(
        "Requesting elicitation for session {SessionId}: {Message}",
        sessionId,
        prompt.Message
    );

    var payload = new ElicitationCreateParams
    {
        Message = prompt.Message,
        RequestedSchema = prompt.RequestedSchema
    };

    // Call server method directly with explicit sessionId
    JsonRpcResponseMessage response = await _server
        .SendClientRequestAsync(sessionId, "elicitation/create", payload, cancellationToken)
        .ConfigureAwait(false);

    // ... deserialize and return result
}
```

**Key Points**:
- SessionId is required parameter
- No fallback to AsyncLocal
- Clear error message if missing

---

## Migration Guide for Custom Transports

### Before (Event-Based)

```csharp
public class MyCustomTransport : IServerTransport
{
    public event Action<JsonRpcRequestMessage>? OnRequest;
    public event Action<JsonRpcNotificationMessage>? OnNotification;
    public event Action<JsonRpcResponseMessage>? OnResponse;

    private void ProcessIncomingMessage(string json)
    {
        var request = JsonSerializer.Deserialize<JsonRpcRequestMessage>(json);
        OnRequest?.Invoke(request); // Trigger event
    }
}
```

### After (Direct Invocation)

```csharp
public class MyCustomTransport : IServerTransport
{
    private readonly McpServer _server;

    // Remove events entirely

    private async Task ProcessIncomingMessage(string json)
    {
        var request = JsonSerializer.Deserialize<JsonRpcRequestMessage>(json);

        var context = new ServerRequestContext
        {
            SessionId = this.Id(),
            TransportId = this.Id(),
            Request = request,
            CancellationToken = _cancellationToken
        };

        var response = await _server.HandleRequestAsync(context);
        await SendAsync(response);
    }
}
```

**Key Changes**:
1. Remove event declarations
2. Store reference to `McpServer`
3. Build `ServerRequestContext`
4. Call `HandleRequestAsync` directly
5. Handle response directly

---

## Next Steps

1. **Team Review** (1 day)
   - Review this updated plan
   - Get final sign-off
   - Address any remaining questions

2. **Branch & Spike** (2 days)
   - Create feature branch
   - Implement Phase 1 (Foundation)
   - Validate approach with simple test

3. **Iterate Through Phases** (2-3 sprints)
   - Complete one phase at a time
   - Run tests after each phase
   - Monitor for issues

4. **Documentation & Release** (1 week)
   - Update all docs
   - Write migration guide
   - Prepare release notes
   - Cut release (2.0 if breaking)

---

## Summary of Changes from v1

| Aspect | v1 Plan | v2 Plan |
|--------|---------|---------|
| **SseJsonRpcProcessor** | "likely collapsed/removed" | **Explicitly deleted** |
| **Router abstraction** | Proposed as enhancement | **Deferred** (keep dispatch in McpServer) |
| **Middleware pipeline** | Proposed as optional | **Deferred** (scope creep risk) |
| **Service signatures** | Not specified | **Explicit: sessionId only, not full context** |
| **Notification handling** | Implied | **Explicit HandleNotificationAsync entry point** |
| **CancellationToken** | Not mentioned | **Added to ServerRequestContext** |
| **Metadata usage** | Vague | **Documented conventions, no service coupling** |
| **DI changes** | Not detailed | **Explicit remove/keep list** |
| **Scope management** | Mixed must-have/nice-to-have | **Clear separation: in-scope vs. deferred** |

---

## Conclusion

This refactoring will:
- ✅ Eliminate event-based complexity
- ✅ Remove AsyncLocal anti-pattern
- ✅ Create linear, debuggable flow
- ✅ Improve testability
- ✅ Simplify transport implementations
- ✅ Maintain clear separation of concerns

The updated plan is **ready for implementation** with clear scope, firm decisions, and explicit DI/service changes documented.

---

*Plan v2 - Ready for Implementation - 2025-11-24*
