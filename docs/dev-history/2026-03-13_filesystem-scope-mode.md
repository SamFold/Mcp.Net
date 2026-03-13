## Change Summary

- Redesigned `FileSystemToolPolicy` so built-in file tools can operate either bounded to the configured base path or unbounded from that same base path.
- Added shared base-path resolution and display-path helpers, then updated the current file tools to use the new scope model.
- Added regression coverage for bounded-mode compatibility plus explicit out-of-tree access in unbounded mode.
- Updated planning docs so `WriteFileTool` is now the next slice on top of the redesigned filesystem seam.

## Why

- The previous policy conflated "default working directory" with "containment boundary".
- Supporting both bounded and unbounded filesystem modes cleanly is easier if all existing file tools share one policy-owned resolution and display model before `WriteFileTool` lands.

## Major Files Changed

- `Mcp.Net.Agent/Tools/FileSystemScopeMode.cs`
- `Mcp.Net.Agent/Tools/FileSystemToolPolicy.cs`
- `Mcp.Net.Agent/Tools/ReadFileTool.cs`
- `Mcp.Net.Agent/Tools/ListFilesTool.cs`
- `Mcp.Net.Agent/Tools/GlobTool.cs`
- `Mcp.Net.Agent/Tools/GrepTool.cs`
- `Mcp.Net.Agent/Tools/RipgrepSearch.cs`
- `Mcp.Net.Agent/Tools/EditFileTool.cs`
- `Mcp.Net.Tests/Agent/Tools/FileSystemToolsTests.cs`
- `Mcp.Net.Tests/Agent/Tools/GlobToolTests.cs`
- `Mcp.Net.Tests/Agent/Tools/GrepToolTests.cs`
- `docs/vnext.md`
- `docs/vnext/agent.md`
- `docs/roadmap.md`
- `docs/roadmap/agent.md`

## Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~FileSystemToolPolicyTests|FullyQualifiedName~ReadFileToolTests|FullyQualifiedName~EditFileToolTests|FullyQualifiedName~ListFilesToolTests|FullyQualifiedName~GlobToolTests|FullyQualifiedName~GrepToolTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent.Tools`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release` is not fully clean because unrelated timeout failures remain in `ChatSessionTests` and `ChatCompletionStreamTests`.
