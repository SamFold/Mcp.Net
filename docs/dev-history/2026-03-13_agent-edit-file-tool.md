# Agent Edit File Tool

## Change summary

- added `edit_file` as a bounded local tool for editing existing text files
- added shared text-file snapshot and encoding-preservation support
- extended `read_file` metadata with content-hash and file-shape information for optimistic concurrency

## Why

- agents needed a safe mutation primitive for existing files before broader shell-based workflows
- `edit_file` enables targeted file updates while preserving BOM, newline style, and bounded filesystem policy constraints
- `read_file` needed to expose metadata that lets later edits detect stale content reliably

## Major files changed

- `Mcp.Net.Agent/Tools/EditFileTool.cs`
- `Mcp.Net.Agent/Tools/TextFileSupport.cs`
- `Mcp.Net.Agent/Tools/ReadFileTool.cs`
- `Mcp.Net.Agent/Tools/FileSystemToolPolicy.cs`
- `Mcp.Net.Tests/Agent/Tools/FileSystemToolsTests.cs`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~ReadFileToolTests|FullyQualifiedName~EditFileToolTests"`
