# MCP Server Remediation Plan

We will land these changes sequentially (Issue 1 first, etc.), keeping the system healthy by running targeted tests after every fix. Each issue lists the planned work plus validation we’ll add to ensure long-term stability.

## Issue 1: Stdio transport framing bug
- Replace the string-based loop in `Transport/Stdio/StdioTransport.cs` with a `SequenceReader<byte>` (or similar) that looks for newline terminators while tracking raw bytes so we honour the newline framing mandated by [Transports §stdio](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports).
- Guard against partial reads and multibyte UTF-8 characters; advance the pipe by consumed **bytes** only and buffer incomplete frames until the newline arrives.
- Add an integration test that feeds fragmented newline-delimited JSON-RPC messages through the pipe and asserts they surface intact.

## Issue 2: Tool parameter name casing
- Stop lowercasing the method parameter names in `ToolRegistry.cs`; respect attribute overrides and the JSON property casing emitted by clients.
- Add unit tests that call a sample tool with mixed-case argument names to ensure we no longer surface false `InvalidParams`.

## Issue 3: Unobserved task failures in `McpServer`
- Make `HandleRequest` await `ProcessRequestAsync` (or log any background fault) so request failures propagate and the transport sees the response.
- Add a regression test that injects a failing tool and validates the server returns an error frame instead of swallowing the exception.

## Issue 4: Tool discovery and invocation responsibilities
- Split `ToolRegistry` into discrete concerns (discovery, schema generation, invocation) to shrink per-class responsibilities and make behaviour obvious.
- Introduce focused unit tests for each component (e.g., schema generator, invocation pipeline) to cover edge cases such as default values and `Task<T>` returns.

## Issue 5: Documentation & observability polish
- Add XML documentation to public builders/transports so consumers integrating Mcp.Net.Server get “self-documenting” guidance (align with Microsoft platform expectations).
- Expand logging/metrics tests where practical (e.g., verifying connection metrics on SSE shutdown) to lock in current behaviour.
