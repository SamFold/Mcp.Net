# Testing Strategy (Mcp.Net)

## Goals

- Keep correctness regressions cheap to detect.
- Keep protocol and transport regressions visible before release.
- Keep core logic testable as server, client, and LLM integrations evolve.

## Test Layers

1. Unit tests
- Focus on pure logic, parameter binding, protocol helpers, serialization, and state transitions.
- Use when behavior can be verified without a real transport or host.
- Command:
  - `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --filter "<TestNameOrClass>"`

2. Integration tests
- Use for behavior that crosses component boundaries:
  - SSE and stdio routing
  - auth and origin validation
  - session isolation
  - server-initiated requests
  - negotiation and transport teardown
- Prefer the existing harness in `Mcp.Net.Tests/Integration/TestServerHarness.cs`.
- Command:
  - `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --filter "FullyQualifiedName~Integration"`

3. Full suite
- Run when a change affects shared infrastructure, public behavior, or multiple subsystems.
- Command:
  - `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj`

4. Manual verification
- Use for sample apps, OAuth flows, or UI-facing behavior that is not fully covered by automated tests.
- Typical commands:
  - `dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj`
  - `dotnet run --project Mcp.Net.Examples.SimpleClient/Mcp.Net.Examples.SimpleClient.csproj -- --url http://localhost:5000 --auth-mode pkce`

## Non-Negotiable Contracts

- Bug fixes require a regression test unless there is a documented reason they cannot.
- Protocol, transport, auth, and routing changes require integration coverage when behavior crosses boundaries.
- New core logic should be extracted into testable seams instead of being buried in middleware or host glue.
- A change is not complete until the targeted test passes and the next broader relevant scope has been verified.

## Standard Workflow

1. Write or update a failing test first for non-trivial changes.
2. Implement the smallest code change that makes the test pass.
3. Run the targeted test again.
4. Run the next broader relevant test scope.
5. If the change affects shared infrastructure or behavior across subsystems, run the full suite.

## Expansion Rules

- Prefer adding pure helpers or focused collaborators when logic needs tests.
- Keep transport and middleware layers thin where possible; move behavior into testable components.
- For server bugs involving live session behavior, prefer an integration regression over a narrow mock-based test alone.
- For public API changes, add tests that preserve expected behavior and edge cases.

## Useful Commands

```bash
dotnet restore Mcp.Net.sln
dotnet build Mcp.Net.sln -c Release
dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj
dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj --filter "FullyQualifiedName~ServerClientIntegrationTests"
dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
dotnet format
```

## Notes

- Prefer precise filtered test runs while iterating, then broaden before finishing.
- Document meaningful verification in the corresponding `docs/dev-history/` entry when you commit.
