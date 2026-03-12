# 2026-03-12 - Agent glob tool

## Change summary
- Added a bounded `glob_files` local tool to `Mcp.Net.Agent` for deterministic file discovery with root-relative results.
- Added compiled glob-pattern parsing plus deterministic traversal on top of the shared filesystem policy.
- Extended the filesystem policy with glob-specific match, depth, accessibility, reparse-point, and skipped-directory controls.
- Updated agent planning/docs to mark the bounded glob slice as completed and advance the next slice to `IMcpClient` ergonomics.

## Why
- Agents need a faster file-discovery primitive than `list_files` when they are locating candidate files before bounded reads.
- The existing filesystem policy and local-tool authoring seam were the right bounded surface to validate recursive discovery without reopening write or shell policy work.
- The planning docs needed to advance once bounded read-only file discovery landed.

## Major files changed
- `Mcp.Net.Agent/Tools/GlobTool.cs`
- `Mcp.Net.Agent/Tools/GlobPattern.cs`
- `Mcp.Net.Agent/Tools/GlobSearch.cs`
- `Mcp.Net.Agent/Tools/FileSystemToolPolicy.cs`
- `Mcp.Net.Agent/README.md`
- `docs/vnext.md`
- `docs/vnext/agent.md`
- `docs/roadmap.md`
- `docs/roadmap/agent.md`
- `docs/dev-history/2026-03-12_agent-glob-tool.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter GlobToolTests`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.Agent.Tools"`
