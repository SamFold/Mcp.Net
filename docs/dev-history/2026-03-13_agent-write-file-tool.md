## Change Summary

- Added `write_file` as the whole-file text creation and overwrite primitive for the built-in local filesystem tool set.
- Extended `FileSystemToolPolicy` with writable-size and directory-creation controls.
- Extracted the atomic file commit path into shared text-file support so `edit_file` and `write_file` use the same sibling-temp commit behavior.
- Wired `write_file` into the local tool bundle and advanced the agent planning/docs to the next `IMcpClient` ergonomics slice.

## Why

- Agents could read, grep, glob, edit, and run shell commands, but they still could not create new files or intentionally replace entire text files through the bounded filesystem seam.
- `write_file` closes that gap while preserving the same policy, encoding, and atomic-write discipline as the rest of the local file tool stack.

## Major Files Changed

- `Mcp.Net.Agent/Tools/WriteFileTool.cs`
- `Mcp.Net.Agent/Tools/FileSystemToolPolicy.cs`
- `Mcp.Net.Agent/Tools/TextFileSupport.cs`
- `Mcp.Net.Agent/Tools/EditFileTool.cs`
- `Mcp.Net.Tests/Agent/Tools/FileSystemToolsTests.cs`
- `Mcp.Net.Examples.LLMConsole/Program.cs`
- `Mcp.Net.Agent/README.md`
- `Mcp.Net.Examples.LLMConsole/README.md`
- `docs/vnext.md`
- `docs/vnext/agent.md`
- `docs/roadmap.md`
- `docs/roadmap/agent.md`

## Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~FileSystemToolPolicyTests|FullyQualifiedName~ReadFileToolTests|FullyQualifiedName~WriteFileToolTests|FullyQualifiedName~EditFileToolTests|FullyQualifiedName~ListFilesToolTests|FullyQualifiedName~GlobToolTests|FullyQualifiedName~GrepToolTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent.Tools`
- `dotnet build Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj -c Release`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent` still hits the existing unrelated timeout in `ChatSessionTests`.
