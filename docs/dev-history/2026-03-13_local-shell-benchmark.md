## Change Summary

- Added a local benchmark and comprehensive smoke-test harness for `RunShellCommandTool`.
- Covered common shell-agent scenarios including cwd override, truncation, environment shaping, pipes, chaining, file I/O, and concurrency behavior.

## Why

- The new shell tool needs fast local validation for both correctness and end-to-end overhead.
- Benchmarking the real shell path is the quickest way to catch regressions in process startup, output capture, truncation, and timeout handling.

## Major Files Changed

- `scripts/ShellBenchmark/ShellBenchmark.csproj`
- `scripts/ShellBenchmark/Program.cs`

## Verification Notes

- `dotnet run --project scripts/ShellBenchmark/ShellBenchmark.csproj -- test`
- `dotnet run --project scripts/ShellBenchmark/ShellBenchmark.csproj -- bench --warmup 0 --iterations 1`
