# Mcp.Net AGENTS.md

## Project Structure
- `Mcp.Net.sln` is the root solution.
- Core libraries live in `Mcp.Net.Core`, `Mcp.Net.Client`, `Mcp.Net.Server`, and `Mcp.Net.LLM`.
- Runnable samples live under `Mcp.Net.Examples.*` and `Mcp.Net.WebUi`.
- Automated tests live in `Mcp.Net.Tests`.
- Ongoing planning and dev history live under `docs/`.

## Build, Test, and Development Commands
- `dotnet restore Mcp.Net.sln`
- `dotnet build Mcp.Net.sln -c Release`
- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release`
- `dotnet format`
- `dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj`
- `dotnet run --project Mcp.Net.Examples.SimpleClient/Mcp.Net.Examples.SimpleClient.csproj -- --url http://localhost:5000 --auth-mode pkce`

## Working Style
- Default to TDD.
- For non-trivial changes, write or update a failing test first, then implement the smallest code change to make it pass.
- After the focused test passes, run the next broader relevant test scope before finishing.
- Keep each change set to one coherent vertical slice. Do not mix unrelated refactors into the same slice.
- Prefer minimal, comprehensible changes over broad rewrites.
- Keep each active `docs/vnext/*.md` slice roughly commit-sized.

## Planning and Docs
- `docs/vnext.md` is the repo-level planning index for active component and system tracks.
- Detailed next-slice planning lives in per-component files under `docs/vnext/`.
- Update the relevant `docs/vnext/*.md` track file or files before substantial implementation when the planned slice changes.
- Update the same `docs/vnext/*.md` track file or files again after completing a slice so they point at the next slice for that area.
- If a change spans multiple components, update each affected track or create a dedicated cross-cutting track.
- `docs/roadmap.md` is the repo-level roadmap index for medium-term sequencing and should be updated whenever priorities, milestones, or major decisions change.
- Detailed medium-term planning lives in project-specific files under `docs/roadmap/`.
- `docs/testing.md` is the canonical testing policy for this repo.
- Keep design notes, technical plans, and deeper writeups in `docs/` rather than scattering new markdown files at the repo root.

## Dev History
- Maintain one dev-history entry per commit in `docs/dev-history/`.
- File name format: `YYYY-MM-DD_short-description.md`.
- Use short kebab-case for the description.
- Each dev-history file must include:
  - change summary
  - why
  - major files changed
  - verification notes
- Commit the dev-history file in the same commit as the related code/docs change.

## Git Discipline
- Do not commit unless the user asks.
- Only commit files changed in the current session.
- Never use `git add -A`, `git add .`, or wildcard staging that can sweep unrelated files.
- Always stage files explicitly by path.
- Run `git status` before staging and again before committing.
- Do not use destructive cleanup commands:
  - `git reset --hard`
  - `git checkout .`
  - `git clean -fd`
  - `git stash`
- Use the repo's existing short-tagged commit format:
  - `[feat]` new functionality
  - `[fix]` bug fix
  - `[test]` tests
  - `[docs]` documentation only
  - `[chore]` housekeeping or non-behavioral refactor
- No emojis in commit messages.

## Docs and Examples
- Do not put real secrets, tokens, private hostnames, or device-specific values in docs, code samples, or tests.
- Use placeholders for sensitive values.
- Keep sample code runnable, but keep library code free of demo-specific shortcuts.

## C# Code Quality
- Use the SDK defaults already established in the repo: nullable enabled, implicit usings, and file-scoped namespaces where appropriate.
- Prefer clear, explicit names over abbreviated ones.
- Keep methods and classes focused. Split behavior into collaborators rather than growing large multi-responsibility types.
- Prefer dependency injection and explicit constructor dependencies over service location and hidden global state.
- Prefer immutable data flow and local reasoning; avoid static mutable state unless there is a strong reason.
- Use existing framework or repo types before introducing ad-hoc abstractions.
- Keep comments sparse and useful. Explain why or non-obvious behavior, not basic mechanics.
- Preserve public API behavior unless the change is intentional and called out.
- For server work, prefer extending existing collaborators such as `ToolDiscoveryService`, `ToolInvocationFactory`, transport helpers, and completion/resource services instead of inflating `ToolRegistry` or `McpServer`.

## Testability Gate
- Core logic must be testable by default.
- Protocol, transport, routing, serialization, negotiation, and auth behavior should live in testable seams rather than being trapped inside middleware glue or oversized orchestration methods.
- Bug fixes require regression tests unless there is a documented reason they cannot.
- Major core changes are not complete unless they have meaningful verification in the same slice.

## Testing
- Test framework stack: xUnit, FluentAssertions, and Moq.
- Place tests in `Mcp.Net.Tests`, mirroring the namespace of the code under test.
- Name tests `MethodUnderTest_ShouldExpectation`.
- For protocol, transport, and routing work, add integration coverage when behavior crosses component boundaries.
- Use the existing integration harness in `Mcp.Net.Tests/Integration/TestServerHarness.cs` and related server/client integration tests instead of building ad-hoc harnesses.
- When fixing a bug, prefer a regression test that fails before the fix and passes after it.
- Follow `docs/testing.md` for test layers, quality gates, and verification expectations.

## Review and Refactoring Expectations
- During code review, prioritize correctness, regressions, edge cases, missing tests, and hidden coupling.
- Refactors must preserve behavior unless the behavioral change is explicit and tested.
- If a refactor makes debugging harder or obscures data flow, it is not an improvement.

## Current Docs Layout
- `docs/vnext.md`
- `docs/vnext/README.md`
- `docs/vnext/<component>.md`
- `docs/roadmap.md`
- `docs/roadmap/README.md`
- `docs/roadmap/<project>.md`
- `docs/testing.md`
- `docs/dev-history/README.md`
- `docs/dev-history/YYYY-MM-DD_short-description.md`
- `docs/archive/`
