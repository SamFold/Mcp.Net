# Server Transport Write Serialization

## Change summary
- Added transport-level regressions proving a second outbound send could enter the same server transport writer while the first send was still in progress.
- Updated `SseTransport` to serialize outbound writes before touching the shared SSE response writer.
- Updated `StdioTransport` to serialize outbound sends before writing newline-delimited JSON-RPC frames to the shared stdio writer.
- Moved the planning docs to the next review item: logging capability truthfulness.

## Why
- The server can emit normal responses, server-initiated requests, and `notifications/.../list_changed` from different call paths on the same connection.
- Before this change, neither server transport serialized those outbound sends.
- That allowed concurrent entry into the same writer on one connection, which risks interleaved bytes, invalid JSON, or broken SSE framing.

## Major files changed
- `Mcp.Net.Server/Transport/Sse/SseTransport.cs`
- `Mcp.Net.Server/Transport/Stdio/StdioTransport.cs`
- `Mcp.Net.Tests/Transport/ServerTransportWriteSerializationTests.cs`
- `docs/vnext.md`
- `docs/roadmap.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~ServerTransportWriteSerializationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore --filter "FullyQualifiedName~McpServerTests|FullyQualifiedName~ServerClientIntegrationTests|FullyQualifiedName~StdioTransportTests|FullyQualifiedName~ServerTransportWriteSerializationTests" -m:1`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --no-restore -m:1`
