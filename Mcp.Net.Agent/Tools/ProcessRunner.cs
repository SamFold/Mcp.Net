using System.Diagnostics;
using System.Text;

namespace Mcp.Net.Agent.Tools;

internal sealed class ProcessRunner
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(3);

    private readonly ProcessToolPolicy _policy;
    private readonly SemaphoreSlim _concurrencyGate;

    public ProcessRunner(ProcessToolPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _concurrencyGate = new SemaphoreSlim(policy.MaxConcurrentProcesses, policy.MaxConcurrentProcesses);
    }

    public async Task<ProcessRunResult> RunShellCommandAsync(
        ResolvedShell shell,
        FileSystemToolPath workingDirectory,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        await _concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            return await RunShellCommandCoreAsync(
                shell,
                workingDirectory,
                command,
                timeout,
                cancellationToken
            );
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async Task<ProcessRunResult> RunShellCommandCoreAsync(
        ResolvedShell shell,
        FileSystemToolPath workingDirectory,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process { StartInfo = shell.CreateStartInfo(command, workingDirectory.FullPath) };
        ApplyEnvironmentDefaults(process.StartInfo);

        var capture = new BoundedOutputCapture(_policy.MaxOutputBytes, _policy.MaxOutputLines);
        long stdoutBytes = 0;
        long stderrBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start shell '{shell.Path}'.");
            }
        }
        catch (Exception ex)
            when (
                ex
                    is InvalidOperationException
                        or IOException
                        or UnauthorizedAccessException
                        or System.ComponentModel.Win32Exception
            )
        {
            throw new InvalidOperationException($"Failed to start shell '{shell.Path}': {ex.Message}", ex);
        }

        process.StandardInput.Close();

        var stdoutTask = PumpReaderAsync(
            process.StandardOutput,
            capture,
            bytes => Interlocked.Add(ref stdoutBytes, bytes)
        );
        var stderrTask = PumpReaderAsync(
            process.StandardError,
            capture,
            bytes => Interlocked.Add(ref stderrBytes, bytes)
        );

        var timedOut = false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await DrainAsync(process, stdoutTask, stderrTask);
            throw;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TryKill(process);
        }

        await DrainAsync(process, stdoutTask, stderrTask);
        stopwatch.Stop();

        var output = capture.Build();
        return new ProcessRunResult(
            ExitCode: timedOut ? null : process.ExitCode,
            Duration: stopwatch.Elapsed,
            TimedOut: timedOut,
            Output: output.CombinedOutput,
            Truncated: output.Truncated,
            TruncatedByLines: output.TruncatedByLines,
            TruncatedByBytes: output.TruncatedByBytes,
            OutputLineCount: output.TotalLines,
            StdoutBytes: stdoutBytes,
            StderrBytes: stderrBytes,
            ShellPath: shell.Path
        );
    }

    private static async Task PumpReaderAsync(
        StreamReader reader,
        BoundedOutputCapture capture,
        Action<int> countBytes
    )
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                countBytes(Encoding.UTF8.GetByteCount(line) + 1);
                capture.Append(line);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Best-effort drain after the process exits or is killed. Intentionally
    /// non-cancellable: after a kill we still want a bounded window to capture
    /// any final output that was buffered in the OS pipe before the process died.
    /// </summary>
    private static async Task DrainAsync(Process process, Task stdoutTask, Task stderrTask)
    {
        var allTasks = Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask);
        await Task.WhenAny(allTasks, Task.Delay(DrainTimeout));
    }

    private static void ApplyEnvironmentDefaults(ProcessStartInfo startInfo)
    {
        startInfo.Environment["TERM"] = "dumb";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["CLICOLOR"] = "0";
        startInfo.Environment["FORCE_COLOR"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed record ProcessRunResult(
    int? ExitCode,
    TimeSpan Duration,
    bool TimedOut,
    string Output,
    bool Truncated,
    bool TruncatedByLines,
    bool TruncatedByBytes,
    int OutputLineCount,
    long StdoutBytes,
    long StderrBytes,
    string ShellPath
);
