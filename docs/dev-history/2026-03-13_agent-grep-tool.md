# Agent Grep Tool

## Change summary

- added `grep_files` as a bounded local content-search tool backed by ripgrep when available
- added ripgrep resolution and streaming result parsing for deterministic root-relative search output
- added focused tool coverage for formatting, truncation, scope handling, and validation

## Why

- agents needed a fast content-search primitive between `glob_files` discovery and `read_file` inspection
- ripgrep provides better performance and search semantics than a managed reimplementation for this use case
- bounded output and deterministic ordering keep the tool practical for agent loops

## Major files changed

- `Mcp.Net.Agent/Tools/GrepTool.cs`
- `Mcp.Net.Agent/Tools/RipgrepCommandResolver.cs`
- `Mcp.Net.Agent/Tools/RipgrepSearch.cs`
- `Mcp.Net.Agent/Tools/FileSystemToolPolicy.cs`
- `Mcp.Net.Tests/Agent/Tools/GrepToolTests.cs`

## Verification notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter FullyQualifiedName~GrepToolTests`
