## OpenAI Codex MCP Handshake Failure (SimpleServer)

**Reported:** 2025-XX-XX (captured from user session)  
**Location:** `~/.codex/config.toml` (Codex) and `Mcp.Net.Examples.SimpleServer` (this repo)

### Summary
- Codex fails to start the `simple_server` MCP client due to a handshake error: `MCP startup failed: handshaking with MCP server failed: conflict initialized response id: expected 0, got 0`.
- The SimpleServer is launched via `dotnet run --project .../Mcp.Net.Examples.SimpleServer.csproj -- --stdio --no-auth --log-path /tmp/simple_server_stdio_codex.log`.
- MCP feature flags are enabled for the Rust MCP client (`[features].rmcp_client = true`); trust level is set to `trusted` for relevant workspaces.

### Error Details
- Codex log output:
  - `⚠ MCP client for \`simple_server\` failed to start: MCP startup failed: handshaking with MCP server failed: conflict initialized response id: expected 0, got 0`
  - `⚠ MCP startup incomplete (failed: simple_server)`

### Current Codex MCP Configuration (key fields)
- Model: `gpt-5.1-codex-max`
- MCP server block (`simple_server`):
  - `command = "dotnet"`
  - `args = ["run", "--project", "/Users/sfold/Documents/Source/Mcp.Net/Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj", "--", "--stdio", "--no-auth", "--log-path", "/tmp/simple_server_stdio_codex.log"]`
  - `startup_timeout_sec = 20`
  - `env`: `GOOGLE_SEARCH_ENGINE_ID = "6214fa6187a724960"`, `GOOGLE_API_KEY = "REDACTED"` (redacted here to avoid storing secrets)
- `[features]`: `rmcp_client = true`
- Trust level: `trusted` for `/Users/sfold/Documents/Source/Mcp.Net` (and several sibling workspaces).

### Reproduction Steps (as reported)
1. Use Codex (CLI or IDE) with the above `~/.codex/config.toml`.
2. Attempt to connect to MCP server named `simple_server`.
3. Observe handshake failure during startup with the conflict on initialized response id.

### Notes / Open Questions
- Handshake error suggests Codex RMCP client and SimpleServer may both emit an `initialize`/`initialized` response with ID `0`, leading to a conflict.
- Need to inspect `/tmp/simple_server_stdio_codex.log` for server-side frames and confirm whether SimpleServer is sending any extra `initialized` messages or mismatched JSON-RPC IDs.
- Confirm Codex RMCP client expectations for initial `initialize`/`initialized` flow over stdio and whether a transport wrapper is injecting messages.

### Next Steps (proposed)
- Collect the stdio transcript from `/tmp/simple_server_stdio_codex.log` (and Codex debug logs if available).
- Verify SimpleServer stdio transport initialization sequence against MCP spec/Codex expectations (IDs, ordering, single `initialized` response).
- Reproduce with a minimal stdio echo harness or the MCP inspector to isolate whether the issue is on the client or server side.
