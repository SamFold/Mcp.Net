# 2026-03-12 - Typed local tool binding

## Change summary
- Added `ToolInvocation.BindArguments<TArgs>()` for typed argument binding from local tool invocations.
- Added `LocalToolBase<TArgs>` as the reusable typed authoring path for local tools.
- Added a transport-neutral `LocalToolSchemaGenerator` so typed local tools generate input schema without depending on MCP discovery attributes.
- Updated agent docs to mark typed local-tool binding as completed and advance the next slice to filesystem policy plus built-in file tools.

## Why
- Local tools should not parse raw argument dictionaries manually when a shared typed binding path can handle that once.
- Built-in tools should establish a clean local-tool authoring model before `ReadFileTool` and `ListFilesTool` land.
- Local tools should not depend on MCP-branded attributes or MCP-oriented schema generation behavior.

## Major files changed
- `Mcp.Net.Agent/Tools/ToolInvocation.cs`
- `Mcp.Net.Agent/Tools/LocalToolBase.cs`
- `Mcp.Net.Agent/Tools/LocalToolSchemaGenerator.cs`
- `Mcp.Net.Tests/Agent/Tools/LocalToolBaseTests.cs`
- `Mcp.Net.Tests/Agent/Tools/LocalToolSchemaGeneratorTests.cs`
- `Mcp.Net.Agent/README.md`
- `docs/vnext/agent.md`
- `docs/vnext.md`
- `docs/roadmap/agent.md`
- `docs/dev-history/2026-03-12_typed-local-tool-binding.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~LocalToolBaseTests|FullyQualifiedName~LocalToolSchemaGeneratorTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent|FullyQualifiedName~ToolDiscoveryServiceTests"`
