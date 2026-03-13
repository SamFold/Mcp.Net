using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

public sealed record RunShellCommandToolOptions(
    string? ShellPath = null,
    ShellKind? PreferredShell = null
);

public sealed class RunShellCommandTool : LocalToolBase<RunShellCommandTool.Arguments>
{
    private readonly ProcessToolPolicy _policy;
    private readonly ProcessRunner _runner;
    private readonly ResolvedShell _shell;

    private RunShellCommandTool(ProcessToolPolicy policy, ResolvedShell shell)
        : base(CreateDescriptor())
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _runner = new ProcessRunner(policy);
    }

    public static bool TryCreate(
        ProcessToolPolicy policy,
        out RunShellCommandTool? tool,
        out string? unavailableReason,
        RunShellCommandToolOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (
            !ShellCommandResolver.TryResolve(
                options?.ShellPath,
                options?.PreferredShell,
                out var shell,
                out unavailableReason
            )
        )
        {
            tool = null;
            return false;
        }

        tool = new RunShellCommandTool(policy, shell!);
        return true;
    }

    protected override async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        Arguments arguments,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments.Command))
            {
                return invocation.CreateErrorResult(
                    "The 'command' argument is required. Provide a shell command string to run inside the bounded local root."
                );
            }

            if (arguments.TimeoutSeconds is <= 0)
            {
                return invocation.CreateErrorResult(
                    "The 'timeoutSeconds' argument must be greater than zero when provided."
                );
            }

            var workingDirectory = _policy.ResolveWorkingDirectory(arguments.WorkingDirectory);

            if (!Directory.Exists(workingDirectory.FullPath))
            {
                if (File.Exists(workingDirectory.FullPath))
                {
                    return invocation.CreateErrorResult(
                        $"Path '{workingDirectory.DisplayPath}' is not a directory."
                    );
                }

                return invocation.CreateErrorResult(
                    $"Path '{workingDirectory.DisplayPath}' does not exist."
                );
            }

            var effectiveTimeoutSeconds = Math.Min(
                arguments.TimeoutSeconds ?? _policy.DefaultTimeoutSeconds,
                _policy.MaxTimeoutSeconds
            );

            var result = await _runner.RunShellCommandAsync(
                _shell,
                workingDirectory,
                arguments.Command,
                TimeSpan.FromSeconds(effectiveTimeoutSeconds),
                cancellationToken
            );

            var metadata = JsonSerializer.SerializeToElement(
                new
                {
                    command = arguments.Command,
                    workingDirectory = workingDirectory.DisplayPath,
                    timeoutSeconds = effectiveTimeoutSeconds,
                    exitCode = result.ExitCode,
                    durationMs = GetDurationMilliseconds(result.Duration),
                    timedOut = result.TimedOut,
                    truncated = result.Truncated,
                    truncatedByLines = result.TruncatedByLines,
                    truncatedByBytes = result.TruncatedByBytes,
                    outputLineCount = result.OutputLineCount,
                    stdoutBytes = result.StdoutBytes,
                    stderrBytes = result.StderrBytes,
                    shellUsed = result.ShellPath,
                }
            );

            return invocation.CreateResult(
                text:
                [
                    FormatTextResult(
                        workingDirectory.DisplayPath,
                        result
                    ),
                ],
                metadata: metadata,
                isError: result.TimedOut
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return invocation.CreateErrorResult(ex.Message);
        }
    }

    public sealed record Arguments(
        string Command,
        string? WorkingDirectory = null,
        int? TimeoutSeconds = null
    );

    private static string FormatTextResult(
        string workingDirectory,
        ProcessRunResult result
    )
    {
        var lines = new List<string>();

        if (result.TimedOut)
        {
            lines.Add("Command timed out.");
        }

        lines.Add($"exitCode: {(result.ExitCode is { } exitCode ? exitCode : "null")}");
        lines.Add($"durationMs: {GetDurationMilliseconds(result.Duration)}");
        lines.Add($"timedOut: {result.TimedOut.ToString().ToLowerInvariant()}");
        lines.Add($"workingDirectory: {workingDirectory}");
        lines.Add($"shellUsed: {result.ShellPath}");

        if (result.Truncated)
        {
            lines.Add("output: truncated");
        }

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrEmpty(result.Output) ? "(no output)" : result.Output);
        return string.Join('\n', lines);
    }

    private static long GetDurationMilliseconds(TimeSpan duration) =>
        Math.Max(1, (long)Math.Round(duration.TotalMilliseconds));

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "run_shell_command",
            Description =
                "Runs a shell command on the host within a bounded local working directory. Use this for CLI workflows like git, dotnet, npm, cargo, or test commands when file-specific tools are not enough. Output is bounded and commands time out automatically.",
            InputSchema = JsonSerializer.SerializeToElement(
                new
                {
                    @type = "object",
                    properties = new
                    {
                        command = new
                        {
                            type = "string",
                            minLength = 1,
                            description =
                                "Required. Shell command string to run. The tool executes it using the configured host shell.",
                        },
                        workingDirectory = new
                        {
                            type = "string",
                            description =
                                "Optional. Directory path relative to the local root to run the command in. Defaults to '.'.",
                        },
                        timeoutSeconds = new
                        {
                            type = "integer",
                            minimum = 1,
                            description =
                                "Optional. Maximum execution time in seconds. Defaults to the policy timeout and is always clamped to that bound.",
                        },
                    },
                    required = new[] { "command" },
                    additionalProperties = false,
                }
            ),
        };
}
