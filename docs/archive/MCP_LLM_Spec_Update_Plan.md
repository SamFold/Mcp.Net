# MCP LLM Spec Alignment Plan

This plan captures the work required to bring `Mcp.Net.LLM` and its console/web examples up to the 2025‑06‑18 MCP specification alongside the refreshed server and client libraries. Each issue is scoped for a focused change set with clear test coverage so we can iterate safely.

## Issue 1: Structured Tool Results & Error Semantics
- **Findings:** `ChatSession.ExecuteToolCall` converts results into a loose `Dictionary<string, object>` (`Mcp.Net.LLM/Core/ChatSession.cs:342`) and the local `ToolCall`/`ToolCallResult` models still mirror the pre‑2025 payload (`Mcp.Net.LLM/Models/ToolCall.cs:6`, `Mcp.Net.LLM/Models/ToolCallResult.cs:6`). This discards the server’s new `content`, `structured`, `resourceLinks`, and `_meta` fields and forces higher layers to reason about raw dictionaries.
- **Impact:** Rich tool output (text vs. resource links, structured JSON) never reaches OpenAI/Anthropic adapters, and the ChatSession surface becomes harder to read because it juggles low-level MCP details.
- **Updates:** Replaced the bespoke models with `ToolInvocation`/`ToolInvocationResult`, introduced a translation layer in `ChatSession.ExecuteToolCall`, and updated all surfaces (event args, DTOs, OpenAI/Anthropic adapters, console UI, stub/web clients) to consume the new abstraction. Structured payloads, resource links, and `_meta` now flow as first-class concepts while keeping high-level code self-documenting.
- **Testing:** Added `ToolInvocationResultTests` to validate content extraction/serialisation and reran the integration suite to confirm both SSE/stdio test harnesses continue to pass with the new model.
- **Status:** ✅ Completed — high-level layers now treat tool invocations/results via the simplified domain model, with rich MCP data preserved behind the scenes.

## Issue 2: Elicitation Workflow Support
- **Findings:** The LLM stack never registers an elicitation handler; `_mcpClient` is only used for `tools/list` and `tools/call` (`Mcp.Net.LLM/Core/ChatSession.cs:390`). There is no UI bridge for the server’s `elicitation/create` requests introduced in the current spec.
- **Impact:** Any server that relies on elicitation fails against the LLM console/WebUI today, blocking compliance scenarios such as credential prompts or user confirmation loops.
- **Updates:**
  1. Introduced `ElicitationCoordinator` + `IElicitationPromptProvider` so ChatSession consumers can register UI-specific handlers without juggling MCP plumbing. The coordinator bridges `McpClientBuilder.WithElicitationHandler` and the active provider.
  2. Console experience: added `ConsoleElicitationPromptProvider` and new `ChatUI` helpers that render schema details, collect responses, and enforce spec constraints (enum options, min/max length, numeric bounds, email/URI/date formats). Console wiring now registers the provider and coordinator prior to connecting the MCP client.
  3. WebUI experience: SignalR adapter now implements `IElicitationPromptProvider`, tracks pending requests, and broadcasts `ElicitationRequested` / `ElicitationCancelled` / resolve events so the browser can show a dialog and respond. `ChatHub.SubmitElicitationResponse` routes replies back through the coordinator and guards failure paths. `ChatAdapterManager` + `ChatFactory` clean up coordinators when sessions end to avoid leaks.
- **Testing:**
  - Added unit coverage for the coordinator, the new web hub path, and the adapter manager release hook; integration suite already covers end-to-end accept/decline flows over SSE/stdio. `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release` exercises the new tests.
- **Status:** ✅ Completed — console and web clients honour `elicitation/create`, enforce schema validation, handle cancellation, and clean up session resources.

## Issue 3: Completion & Prompt/Resource Surfacing
- **Findings:** Neither the core library nor the sample apps call `IMcpClient.CompleteAsync`, list prompts, or read resources—everything is hard-coded around tools (`Mcp.Net.Examples.LLMConsole/Program.cs:255`). The UIs therefore cannot exercise the new completion capability or show server-provided prompts/resources.
- **Impact:** Users lose out on auto-complete flows the server now supports, and we cannot validate prompt/resource negotiations end-to-end, leaving gaps noted in `MCP_2025-06-18_Compliance_NextSteps.md`.
- **Updates:**
  1. Added `CompletionService` (+cache invalidation) and `PromptResourceCatalog` to `Mcp.Net.LLM`, wiring catalog refresh to MCP `prompts/resources/list_changed` notifications.
  2. Console app gains prompt/resource explorers and completion commands (`:prompts`, `:resources`, `:complete`) with live catalog updates emitted through the chat UI.
  3. Web backend now exposes prompt/resource listings and completion endpoints through `SignalRChatAdapter`/`ChatHub`, broadcasting catalog changes to browsers via new DTOs.
- **Testing:** New unit suites cover completion caching/invalidations and catalog refresh, while hub tests exercise prompt surfacing and completion routing. `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release` passes.
- **Status:** ✅ Completed — completions, prompts, and resources surface end-to-end with notification-driven refreshes in both console and web experiences.

## Issue 4: Notification-Driven Catalog Refresh & Metadata Adoption
- **Findings:** Tool metadata is loaded once at startup and categorised by hard-coded prefixes (`Mcp.Net.LLM/Tools/ToolRegistry.cs:27`). `IMcpClient.OnNotification` is never observed, so `notifications/tools/list_changed`, `resources/list_changed`, and `prompts/list_changed` are ignored.
- **Impact:** The LLM UI drifts out of sync when new tools/prompts/resources are added or removed, undermining hot-reload flows the server now supports. We also ignore server-provided annotations and `_meta` used for UX hints.
- **Updates:**
  1. Replaced prefix heuristics with metadata-aware `ToolRegistry`, preserving fallback categories but folding in server-provided display names/orders.
  2. Wired console and web adapters to auto-refresh on `tools/list_changed` and re-register tools with active chat clients, surfacing change notifications to the UI.
  3. Centralised tool refresh logic via `ToolRegistry.RefreshAsync` and added `ToolsUpdated` events so higher layers stay in sync.
  4. Refactored the registry into immutable snapshots backed by `ToolCategoryMetadataParser`/`ToolCategoryCatalog`, clarifying responsibilities, improving concurrency safety, and making category/prefix handling independently testable.
  5. Extended `[McpTool]` so servers can declare categories/metadata directly in annotations, ensuring clients receive spec-compliant hints without manual wiring.
- **Testing:** Expanded registry unit tests to cover metadata parsing, enabled-state persistence, fallback categories, event emission, and category lookups; hub tests continue to assert tool routing, and console/web wiring exercises refresh paths.
- **Status:** ✅ Completed — tool inventory now responds to MCP notifications, respects server annotations, and keeps the console/web experiences current without restarts.

## Issue 5: Transport & Authentication Flexibility
- **Findings:** The console sample always boots SSE against `http://localhost:5000/` with no OAuth support (`Mcp.Net.Examples.LLMConsole/Program.cs:245`). There is no path to run against stdio transports, dynamic registration, or bearer-token protected servers shipped with the updated samples.
- **Impact:** The application cannot connect to production-style deployments or exercise stdio regressions already covered in the server/client test suites.
- **Updates:** Promote configuration (CLI/env) for transport selection, OAuth mode (PKCE/client credentials/device), and negotiated protocol logging. Reuse `DemoOAuthDefaults` from `Mcp.Net.Examples.Shared` so the console sample mirrors SimpleClient. Ensure instructions returned during initialization are displayed to the operator for parity with other clients.
- **Updates (2025-??-??):**
  1. Console sample now accepts `--url`, `--command`, and `--auth-mode`/`--no-auth` switches, enabling SSE with PKCE/client-credentials and stdio fallback.
  2. Reused `DemoOAuthDefaults` and dynamic registration helper from the shared library; instructions and negotiated protocol are logged on connect.
- **Notes:** Manual end-to-end verification against the demo server is still recommended (403 should now clear once OAuth is configured). Consider follow-up automation that exercises the new CLI surface.
- **Testing:** Add smoke tests that spin up both transports via the integration harness, exercising OAuth-disabled and token-protected setups, and verify session headers/logging behave as expected.

## Issue 6: Test Coverage & Documentation Refresh
- **Findings:** We still lack targeted coverage for the core conversation orchestrator (`ChatSession`) and the two production LLM adapters (`OpenAiChatClient`, `AnthropicChatClient`). Each class now hides a lot of MCP-vs-provider translation logic (tool batching, thinking-state events, structured results) yet has little documentation, no contract-level tests, and duplicated glue that could drift. Docs (`Mcp.Net.LLM/README.md`) also lag behind the new behaviours (system prompts, tool retries, OAuth-aware console flow).
- **Impact:** These three components are the crux of the LLM stack; regressions there would break every client/front-end. Without tests we can’t refactor safely, and the current lack of XML docs or developer notes makes maintenance risky.
- **Updates:** Introduce a dedicated workstream to harden and document these classes:
  1. **ChatSession hardening**
     - Add XML summaries for the public surface, clarifying threading guarantees, event ordering, and tool batching semantics.
     - Break out private helpers (e.g., user vs. tool response batching) where it simplifies testing.
     - Add unit tests that cover: batched tool+assistant responses, error propagation when `IMcpClient` fails, thinking-state change notifications, and session reset flows. Mock `IChatClient`, `IMcpClient`, and `IToolRegistry` to assert event sequencing.
     - ✅ Initial coverage landed (`ChatSessionTests`) for text-only messages, tool execution success, and missing-tool failure paths.
  2. **LLM adapter coverage (OpenAI + Anthropic)**
     - Document the message-history handling (system prompt updates, tool result serialization) with XML docs/private comments.
     - Pull shared parsing/wire-format helpers into a common utility to reduce duplication (e.g., argument parsing, tool-result serialization).
     - Add adapter-level tests that simulate provider responses: tool-call payloads, multi-message completions, error cases, and `SendToolResultsAsync` batching. Use fixture responses rather than live SDK hits.
     - ✅ OpenAI adapter refactored to centralise model resolution, completion options, and tool-call parsing helpers.
     - ✅ Anthropic adapter now wraps the SDK behind `IAnthropicMessageClient`, adds XML docs/logging, and ships with unit coverage via `AnthropicChatClientTests`.
  3. **Documentation refresh**
     - Update `Mcp.Net.LLM/README.md` and the console README to describe the new session/auth flows, testing strategy, and how to run the new adapter tests.
     - Link the above work into `MCP_2025-06-18_Compliance_NextSteps.md` so cross-repo owners can track adapter resiliency efforts.
- **Testing:** Create targeted unit suites (e.g., `ChatSessionTests`, `OpenAiChatClientTests`, `AnthropicChatClientTests`) using test doubles for transports and provider SDKs. Where useful, add integration smoke tests that run the full chat loop via the existing harness. Ensure `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release` covers the additions (✅ ChatSession, ✅ Anthropic adapter in place; OpenAI adapter tests still pending).

---

Tracking the above issues alongside `MCP_Server_Spec_Update_Plan.md` and `MCP_Client_Spec_Update_Plan.md` keeps the LLM layer aligned with ongoing compliance work while giving us clear, reviewable steps to deliver parity.
