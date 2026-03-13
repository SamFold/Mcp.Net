# Change Summary

Added the local `WriteBenchmark` harness for `WriteFileTool` and tightened its CLI behavior and reporting.

- added benchmark scenarios for create and overwrite paths across file sizes and encoding-preservation cases
- added comprehensive local correctness checks for `WriteFileTool`
- fixed invalid CLI handling so unknown modes and invalid iteration counts fail cleanly
- fixed benchmark process exit behavior so test failures propagate via exit code
- expanded the benchmark table to report `P50` and `P95`
- changed the benchmark byte column to report encoded bytes written instead of source string length

# Why

This gives the agent toolchain a local smoke/performance harness for `write_file` that is useful for before/after comparisons and avoids misleading success or crash behavior from the benchmark runner itself.

# Major Files Changed

- `scripts/WriteBenchmark/Program.cs`
- `scripts/WriteBenchmark/WriteBenchmark.csproj`
- `docs/dev-history/2026-03-13_write-benchmark-harness.md`

# Verification Notes

- `dotnet run --project scripts/WriteBenchmark/WriteBenchmark.csproj`
- `dotnet run --project scripts/WriteBenchmark/WriteBenchmark.csproj -- bogus`
- `dotnet run --project scripts/WriteBenchmark/WriteBenchmark.csproj -- bench --iterations 0`

Observed:

- full benchmark run completed successfully with `37/37` local tests passing
- unknown mode now exits with usage text and a non-zero exit code
- `--iterations 0` now exits with usage text and a non-zero exit code instead of crashing
