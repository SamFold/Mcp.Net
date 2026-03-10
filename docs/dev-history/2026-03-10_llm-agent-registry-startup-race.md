# 2026-03-10 - LLM agent registry startup race

## Change summary
- Updated `AgentRegistry` to track initialization with a replaceable task and await it before public cache access.
- Preserved recovery after a failed first load by having `ReloadAgentsAsync` replace the tracked initialization task.
- Kept `RegisterAgentAsync` and `UpdateAgentAsync` validating `userId` before any initialization wait.
- Added regressions covering startup blocking, recovery after failed initialization, and pre-initialization `userId` validation.
- Advanced the LLM planning docs so the remaining review lane now points only at persisted agent settings round-trip verification.

## Why
- The registry constructor previously fired `ReloadAgentsAsync()` and discarded the task, so early callers could observe an empty cache and create duplicate default agents.
- A naive tracked-task fix could permanently brick the registry after a transient startup load failure unless the reload path could replace the faulted task.
- Validation behavior for invalid register/update user IDs needed to stay truthful and immediate rather than being masked by startup initialization waits.

## Major files changed
- `Mcp.Net.LLM/Agents/AgentRegistry.cs`
- `Mcp.Net.Tests/LLM/Agents/AgentRegistryTests.cs`
- `docs/vnext.md`
- `docs/vnext/llm.md`
- `docs/roadmap.md`
- `docs/roadmap/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Agents.AgentRegistryTests"`
- `git diff --check`
