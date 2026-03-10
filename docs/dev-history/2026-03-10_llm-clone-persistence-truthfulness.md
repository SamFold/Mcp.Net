# 2026-03-10 - LLM clone persistence truthfulness

## Change summary
- Updated `AgentManager.CloneAgentAsync` to check the `RegisterAgentAsync` result instead of assuming clone persistence succeeded.
- Added a regression proving clone requests throw when agent persistence fails.
- Advanced the LLM planning docs so the remaining review-follow-on slice now focuses on the startup-race and persisted-settings issues.

## Why
- Clone requests could return an `AgentDefinition` that was never persisted when the registry/store path failed.
- `CreateAgentAsync` already treated failed registration as an error, so clone behavior needed to match that truthfulness contract.
- The active LLM plan needed to stop advertising clone persistence as unresolved once the fix landed.

## Major files changed
- `Mcp.Net.LLM/Agents/AgentManager.cs`
- `Mcp.Net.Tests/LLM/Agents/AgentManagerTests.cs`
- `docs/vnext.md`
- `docs/vnext/llm.md`
- `docs/roadmap.md`
- `docs/roadmap/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Agents.AgentManagerTests"`
- `git diff --check`
