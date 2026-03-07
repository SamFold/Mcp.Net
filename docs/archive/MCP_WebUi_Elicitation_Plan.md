# Web UI Elicitation Integration Plan

1. **Backend Readiness Audit** ✅
   - Reviewed elicitation plumbing in `ChatFactory`, `SignalRChatAdapter`, and `ChatHub` to confirm request broadcast, cancellation handling, and response forwarding.
   - Added structured logging around request lifecycle (creation, cancellation, completion, and response actions) so dropped or timed-out elicitation requests surface before the UI work begins.

2. **SignalR Client Wiring** ✅
   - Updated the React SignalR service to listen for `ElicitationRequested`/`ElicitationCancelled` events and expose a `submitElicitationResponse` hub call.
   - Zustand chat store now tracks `pendingElicitations` keyed by `requestId`, removing entries on cancellation, response submission, or session deletion.

3. **Schema-Driven Form Rendering** ✅
   - Added a reusable `ElicitationForm` component that maps schema primitives (string/number/integer/boolean/enum) to Tailwind-styled controls with per-field validation hooks.
   - Defaults and validation cover required rules, length/format checks, numeric ranges, and easily swappable renderers so future UX tweaks stay isolated from transport logic.

4. **Modal UX Flow** ✅
   - Active session prompts now surface in a slide-in drawer that keeps the transcript visible, blocks input only for that session, and supports dismiss/reopen so users can temporarily shelve prompts.
   - Session badges and welcome-screen banners highlight outstanding prompts across sessions; dismissed prompts queue a “Review” toast for quick reopening.

5. **State Management & Resilience** ✅
   - Zustand store tracks pending prompts per session, handles dismiss/reopen, and prunes stale IDs on reconnect so the UI survives reloads and SignalR reconnects.
   - Input controls remain disabled until all prompts for the active session are resolved; background prompts remain queued for later review.

6. **Testing & Validation**
   - Add unit tests for the schema mapper and validation helpers.
   - Introduce Playwright/Cypress smoke tests that exercise accept/decline paths using the SimpleServer Warhammer elicitation tool.
   - Manually verify concurrency scenarios (multiple prompts, prompt + tool call) before release.

**Known Issues & Fixes**

- **Cross-session prompt leakage (discovered 2025-10-30)**  
  Repro: trigger delayed tools (`wh40k_extraction_order`) in two chats, then open a third chat. After the delay expires both elicitation prompts surface in the *third* chat, because the earlier requests hit the client-side timeout and were re-routed when the SSE connection reinitialised for the new session. Server logs show `Client request 'elicitation/create' timed out.` followed by the prompts landing under the wrong session ID.
  - ✅ **Mitigation**: SSE request timeout is now configurable via `McpServer:SseRequestTimeoutSeconds` (default 120 s) and propagated through `SseMcpClient`, preventing the 30 s cutoff for long-running tools.
  - 🛠️ **Next**: extend the server-side `ClientRequestTimeout` (currently 60 s) and expose it via config so the server keeps listening while the user completes elicitation.
  - 🔍 **Fix in progress**: audit MPC client/session mapping so outstanding elicitation IDs remain tied to the originating session even after reconnects; introduce targeted tests to prevent cross-session leakage.

7. **Next UX Enhancements (todo)**
   - **Form polish & validation**: add inline status (required indicators, success ticks, error banner on submit failure) and automatically restore schema defaults when a prompt is dismissed/reopened.
   - **Queue management UI**: surface the full prompt stack inside the drawer (scrollable cards) so users can switch between multiple requests without leaving the session.
   - **Notifications & reminders**: toast/badge when a deferred prompt ages out, optional auto-restore timer, and a lightweight inbox of outstanding prompts across sessions.
   - **Accessibility**: trap focus in the drawer, emit ARIA-live updates for prompt arrival, and add keyboard shortcuts (Esc to dismiss, Ctrl/⌘+Enter to submit).
   - **Testing**: add unit coverage for renderers/validators and an integration suite (Playwright/Cypress) that walks accept/decline/cancel flows using the sample Warhammer tools.
   - **Future UX consideration**: optionally allow chat input while prompts are pending if server semantics permit concurrent user messages; otherwise keep the current lock.
