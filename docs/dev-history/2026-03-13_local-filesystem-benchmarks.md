## Change Summary

- Added local benchmark harnesses for `EditFileTool`, `GlobTool`, and `GrepTool`.
- Added a small `.gitignore` rule for benchmark snapshot `testdata/` trees so realistic local copies stay out of git.
- Included local comprehensive smoke-test modes and quick benchmark output for the filesystem-oriented tools.

## Why

- The new agent tools need cheap local performance and correctness checks outside the main solution test suite.
- A frozen or ad hoc local tree is useful for evaluating realistic ripgrep-backed searches and bounded file operations.

## Major Files Changed

- `.gitignore`
- `scripts/EditBenchmark/EditBenchmark.csproj`
- `scripts/EditBenchmark/Program.cs`
- `scripts/GlobBenchmark/GlobBenchmark.csproj`
- `scripts/GlobBenchmark/Program.cs`
- `scripts/GrepBenchmark/GrepBenchmark.csproj`
- `scripts/GrepBenchmark/Program.cs`
- `scripts/GrepBenchmark/snapshot-testdata.sh`

## Verification Notes

- `dotnet run --project scripts/EditBenchmark/EditBenchmark.csproj -- test`
- `dotnet run --project scripts/EditBenchmark/EditBenchmark.csproj -- bench --warmup 0 --iterations 1`
- `dotnet run --project scripts/GlobBenchmark/GlobBenchmark.csproj -- /Users/sfold/Documents/Source/Mcp.Net --warmup 0 --iterations 1`
- `dotnet run --project scripts/GrepBenchmark/GrepBenchmark.csproj -- test`
- `dotnet run --project scripts/GrepBenchmark/GrepBenchmark.csproj -- bench --warmup 0 --iterations 1`
