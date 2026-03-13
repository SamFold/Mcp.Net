# Agent Run Shell Command Tool

## Change summary

- added `run_shell_command` as the first bounded local process-execution tool
- added a separate `ProcessToolPolicy`, shell resolution, bounded combined output capture, and process lifecycle handling
- wired the tool into the console sample and updated planning/docs to point the next slice back at `WriteFileTool`

## Why

- agents needed a practical way to run real host CLI workflows such as `git`, `dotnet`, `npm`, and `cargo`
- a bounded shell-command tool unlocks realistic coding-agent flows without exposing a broader process-session surface yet
- separate process policy keeps shell execution concerns isolated from the filesystem tool policy

## Major files changed

- `Mcp.Net.Agent/Tools/RunShellCommandTool.cs`
- `Mcp.Net.Agent/Tools/ProcessToolPolicy.cs`
- `Mcp.Net.Agent/Tools/ProcessRunner.cs`
- `Mcp.Net.Agent/Tools/ShellCommandResolver.cs`
- `Mcp.Net.Agent/Tools/BoundedOutputCapture.cs`
- `Mcp.Net.Examples.LLMConsole/Program.cs`
- `Mcp.Net.Tests/Agent/Tools/RunShellCommandToolTests.cs`
- `docs/vnext/agent.md`
- `docs/roadmap/agent.md`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~RunShellCommandToolTests`
- `dotnet build Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj -c Release`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent.Tools`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~Mcp.Net.Tests.Agent`
