# MCP 2025-06-18 Compliance Next Steps

## 1. Production-Ready OAuth Resource Server Hardening
**Status:** In progress.  
**Completed:**  
- Added configuration binding (`AuthenticationConfiguration`) so operators can supply authority, resource identifiers, audiences, issuers, signing keys, and discovery metadata via configuration rather than code.  
- Enriched `AuthResult`, tightened `OAuthAuthenticationHandler`, and updated `McpAuthenticationMiddleware` so 401/403 responses emit spec-compliant `WWW-Authenticate` headers and structured JSON errors.  
- Added unit tests for configuration binding and middleware error handling to lock in the new behaviour.  
- Updated `README.md`, `AGENTS.md`, and `MCP_Server_Future_Work_Plan.md` with concrete integration guidance for Auth0, Entra ID, Supabase, and Clerk, plus advice on enforcing user-level authorization inside tools.

**Still to do:**  
- Build integration tests that drive the end-to-end discovery and validation pipeline (401 challenge → metadata fetch → token validation) and cover failure paths such as audience mismatch and expired tokens.  
- Provide sample configuration snippets or scripts that show live tokens flowing from each listed provider, including instructions for Supabase token exchange when JWKS is unavailable.  
- Consider telemetry enhancements (token validation logs/metrics) and rate-limiting hooks once the functional work is complete.

## 2. Client HTTP Guardrails for Refresh & Error Propagation
**Status:** Completed. 
- Added a refresh failure ceiling inside `OAuthTokenManager` so the client stops attempting to reuse an expired token after three failed refreshes, preventing loops that spam the authorization server. Tests cover the cap to ensure it cannot regress silently and confirm successful refreshes populate the cache once a valid token arrives.
- Replaced the generic `HttpRequestException` usage with a purpose-built `McpClientHttpException` that surfaces status code, response body, content type, and request metadata. The SSE transport now parses JSON error payloads for concise summaries and logs the details before raising the exception.
- Expanded transport and token-manager tests to validate retry behaviour, unauthorized edge cases, JSON error parsing, and successful refresh caching. Documentation (README/AGENTS) calls out the new exception type so integrators know what to catch.

**Next enhancements:** consider exposing configurable retry/backoff settings and emitting diagnostics via `DiagnosticSource` or ILogger scopes so hosts can plug into richer telemetry. If additional transports are added, apply the same exception pattern for consistency.

## 3. STDIO Transport Parity Across Client and Server
**Status:** Completed. 
- `StdioClientTransport` now captures the negotiated MCP revision for diagnostics, emits it with every request log, and exposes the value via a public accessor so hosts can trace protocol drift. The server logs the negotiated revision during `initialize`, aligning stdio and HTTP instrumentation.
- Client transports raise notification events (including `notifications/progress`) and surface them through `IMcpClient`, enabling heartbeat/progress hooks. Additional unit coverage asserts notification dispatch, invalid JSON handling (malformed payloads now trigger deterministic errors), and negotiated-version propagation.
- Existing newline framing, timeout, and shutdown behaviour remain intact; the new tests complement the earlier fragmentation/timeouts suite to guard regressions.

**Next enhancements:** consider optional heartbeat emission helpers for long-running tool invocations and shared telemetry for negotiated metadata if future diagnostics warrant it.

## 4. Implement Elicitation Feature
The 2025-06-18 spec introduces elicitation (Client Features §elicitation), allowing servers to request additional user input mid-flow. We now have end-to-end support; remaining work focuses on polish, documentation depth, and multi-transport validation.

**Progress:**
- Reworked the server transport pipeline so SSE/stdio endpoints can emit server-initiated requests and receive responses. Pending requests are tracked with timeouts and error propagation, enabling `elicitation/create` flows without custom plumbing per transport.
- Added the `IElicitationService` façade plus typed schema/result models. SimpleServer’s Warhammer tools now prompt users to tweak generated Inquisitors, and the strict stdio host wires the service through DI without polluting stdout.
- Tests cover server-side happy path, declines, error surfaces, and timeout cancellation (`Mcp.Net.Tests/Server/Elicitation/ElicitationServiceTests`). Transport unit tests already ensure responses are dispatched correctly.
- Implemented client-side elicitation handling (`Mcp.Net.Client/McpClient.cs:140-643`, `Mcp.Net.Client/Interfaces/IMcpClient.cs:101-108`) so transports surface server requests and hosts can register an `IElicitationRequestHandler`. Supporting types (`Mcp.Net.Client/Elicitation/*`) expose handler-facing context models and helper responses.
- Added transport changes so clients can receive requests and post responses (`Mcp.Net.Core/Transport/IClientTransport.cs:13-49`, `ClientTransportBase.cs:20-188`, `ClientMessageTransportBase.cs:17-168`, `Mcp.Net.Client/Transport/SseClientTransport.cs:220-271`).
- Unit coverage verifies accept/decline/error flows and schema hydration edge cases (`Mcp.Net.Tests/Client/McpClientElicitationTests.cs:22-266`). `EnsureSchemaHydrated` uses raw JSON payloads to populate `ElicitationSchema.Properties` when deserialization skips getter-only dictionaries (`Mcp.Net.Client/Elicitation/ElicitationRequestContext.cs:6-89`).
- Added delegate-friendly APIs and samples: `McpClientBuilder.WithElicitationHandler` and the `IMcpClient.SetElicitationHandler(Func<…>)` extension wire handlers with one line, while the SimpleClient console handler (`Mcp.Net.Examples.SimpleClient/Elicitation/ConsoleElicitationHandler.cs`) demonstrates a full end-to-end flow with prompts, validation, and accept/decline/cancel outcomes.
- Client now advertises the `elicitation` capability only after a handler is registered, aligning with the spec. Tests cover both advertised and non-advertised cases (`Mcp.Net.Tests/Client/McpClientInitializationTests.cs`).

**Follow-up / gaps:**
- Expand documentation with richer UX guidance (screenshots, GUI-host patterns) building on the newly added README and sample updates that cover handler registration.
- Revisit handler ergonomics (e.g., cancellation propagation, validation helpers) once sample feedback is incorporated.

Integration validation (delivered):
- `Mcp.Net.Tests/Integration/TestServerHarness.cs` now spins up real SSE (ASP.NET TestServer) and stdio transports so tests can drive authentic framing, timeouts, and request plumbing.
- `ServerClientIntegrationTests` exercises elicitation and completion flows end-to-end over both transports, covering accept/decline handling, schema payloads, capability negotiation, and completion filtering (`Total`/`HasMore`).

Future work may include pluggable completion engines or host-provided UI hooks once baseline coverage is in place.

## 5. Implement Completion Feature
**Status:** Completed.  
- Added completion DTOs (`Mcp.Net.Core/Models/Completion/*`) and wired `CompletionCompleteParams` into the generic request pipeline so the server can deserialize `completion/complete` payloads without custom glue.
- McpServer now exposes `RegisterPromptCompletion`/`RegisterResourceCompletion`, toggles the capability automatically, and dispatches to registered handlers. Unit tests cover happy path, capability gating, and handler failures (`Mcp.Net.Tests/Server/Completions/McpServerCompletionTests.cs`).
- IMcpClient gains `CompleteAsync`, validates capability advertisement, and surfaces `CompletionValues`; client-side tests assert success and error flows (`Mcp.Net.Tests/Client/McpClientCompletionTests.cs`).
- SimpleServer seeds completions for the `draft-follow-up-email` prompt and SimpleClient prints the suggestions. `AGENTS.md`, sample READMEs, and the compliance plan now document the workflow.

**Follow-up / gaps:**
- Expand resource-oriented examples (URI templates, filesystem aliases) once real-world consumers surface requirements, and consider rate-limiting or batching guidance for large suggestion sets.

## 6. Enforce Tool Output Schemas & Annotation Propagation
Structured tool results (`structuredContent`, output schemas, annotations) are mandatory for reliable downstream parsing (Server Tools §structured-content). Our server currently emits structured payloads but does not validate them against declared schemas or persist annotations through invocation pipelines. Adding validation at invocation time will protect clients from malformed responses and ensure tool authors honour their contracts.

Testing should include schema mismatch detection, annotation round-tripping, and coverage for resource links embedded in tool results. Documentation updates ought to guide tool authors on defining schemas and communicating error semantics. As a future extension, we could add developer tooling to auto-generate schemas from C# types and integrate with IDE analyzers to catch mismatches at build time.

## 7. Streamable HTTP Hardening & Resumability
The Streamable HTTP transport must enforce `Accept` headers, provide optional SSE event IDs with `Last-Event-ID` support, and honour DELETE requests for session shutdown (MCP Transports §streamable-http). Our current implementation omits some checks and lacks resumability, which risks interoperability with more resilient clients.

Integration tests should simulate clients reconnecting with `Last-Event-ID`, invalid `Accept` headers, and DELETE-based session termination to confirm compliance. Documentation must explain resumable flows, the importance of unique event IDs per session, and operational tuning (timeouts, retries). Potential future work includes back-pressure controls, transport metrics, and user-configurable keep-alive intervals.

## 8. `_meta` Validation Helpers
While `_meta` fields now round-trip, the spec reserves prefixes for MCP use (Basic §_meta). Adding reusable validation helpers will prevent accidental collisions and provide consistent error messages across tools, prompts, and resources.

Testing should cover allowed versus disallowed prefixes, empty keys, and mixed-case handling. We should extend developer docs to demonstrate correct `_meta` usage and warn against reserved namespaces. This foundation could enable future linting analyzers or sample extensions that leverage `_meta` for audience routing or provenance tracking.

## 9. Structured Logging Capability Delivery
The server advertises logging capability but lacks a structured notification channel (Server Utilities §logging). We need to design and implement an MCP-compliant logging stream (likely `logging/message` notifications) with severity levels, correlation IDs, and optional annotations. Only after delivery should the server advertise logging support in initialization responses.

Acceptance criteria include unit tests for serialization, integration tests verifying clients receive logs over both transports, and careful documentation on enabling logging in production. Future enhancements might include log filtering, batching, or integration with existing .NET logging providers so host applications can pipe messages into central observability stacks.

## 10. Schema Delta Audit & Remediation
Beyond headline features, the 2024-11-05 → 2025-06-18 schema delta introduces smaller changes (progress messages, audio content, batch removal). A systematic audit ensures no lingering gaps that might trip interoperability. This involves comparing the TypeScript schema to our models, verifying we reject JSON-RPC batching, and supporting new content types where required.

Testing must include serialization/deserialization coverage for new content types, progress notification propagation, and rejection of unsupported batch payloads. Documenting the outcomes in the spec update plans will help stakeholders understand compliance status. Future work may automate schema diff checks so future protocol releases trigger alerts before drift occurs.

## 11. Security, Performance, and Observability Expansion
With OAuth flows in place, we should broaden negative-path coverage (audience validation, token replay, refresh rotation) and add load/performance smoke tests to validate throughput claims. MCP’s security guidance (Authorization §Security Considerations) emphasises auditing and telemetry, so we should instrument transports with structured logs/metrics and ensure exceptions surface actionable messages.

Testing implications include scripted stress tests for SSE and stdio transports, fuzzing of OAuth inputs, and coverage for refresh token rotation behaviours. Documentation updates should catalogue new diagnostics, recommended monitoring dashboards, and guidance for secure token storage on clients. Future opportunities include integrating with OpenTelemetry, offering configurable rate limits, and documenting reference deployment topologies for enterprises.
