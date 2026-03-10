# 2026-03-10 - LLM persisted agent settings roundtrip

## Change summary
- Added a regression proving `AgentFactory` handles `temperature` and `max_tokens` after a real `FileSystemAgentStore` save/load round-trip.
- Verified that persisted `AgentDefinition.Parameters` values deserialize as `JsonElement` and still map into `ChatClientOptions`.
- Advanced the LLM planning docs from the completed review-follow-on lane to the next post-parity API-shape decision around `IChatClient` statefulness.

## Why
- The production path persists agent settings through JSON, where `Dictionary<string, object>` values come back as `JsonElement` rather than the original CLR numeric types.
- Existing tests covered only in-memory numeric values and did not prove the real file-backed path that the app depends on.
- With the review follow-ons now complete, the LLM plan needed to move on to the next concrete slice before `Mcp.Net.Agent` extraction.

## Major files changed
- `Mcp.Net.Tests/LLM/Agents/AgentFactoryTests.cs`
- `docs/vnext.md`
- `docs/vnext/llm.md`
- `docs/roadmap.md`
- `docs/roadmap/llm.md`

## Verification notes
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Agents.AgentFactoryTests"`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release -m:1 /nodeReuse:false --filter "FullyQualifiedName~Mcp.Net.Tests.LLM.Agents"`
- `git diff --check`
