using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Tools;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Tools;

public class RunShellCommandToolTests
{
    [Fact]
    public void TryCreate_ShouldReturnFalseWhenConfiguredShellIsMissing()
    {
        using var root = new TemporaryDirectory();
        var policy = new ProcessToolPolicy(root.Path);

        var created = RunShellCommandTool.TryCreate(
            policy,
            out var tool,
            out var unavailableReason,
            new RunShellCommandToolOptions(Path.Combine(root.Path, "missing-shell"), GetConfiguredShell().Kind)
        );

        created.Should().BeFalse();
        tool.Should().BeNull();
        unavailableReason.Should().Contain("shell");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnCombinedOutputAndNonZeroExitCodeAsSuccessfulResult()
    {
        using var root = new TemporaryDirectory();
        var tool = CreateTool(root.Path);

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "run_shell_command",
                new Dictionary<string, object?> { ["command"] = CreateStdoutStderrExitCommand() }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("exitCode: 7");
        result.Text[0].Should().Contain("stdout-line");
        result.Text[0].Should().Contain("stderr-line");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("exitCode").GetInt32().Should().Be(7);
        result.Metadata!.Value.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseRequestedWorkingDirectoryWithinRoot()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        var tool = CreateTool(root.Path);

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-2",
                "run_shell_command",
                new Dictionary<string, object?>
                {
                    ["command"] = CreatePrintWorkingDirectoryCommand(),
                    ["workingDirectory"] = "src",
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain(Path.Combine(root.Path, "src"));
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("workingDirectory").GetString().Should().Be("src");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectMissingOrOutOfBoundsWorkingDirectory()
    {
        using var root = new TemporaryDirectory();
        var tool = CreateTool(root.Path);

        var missingDirectoryResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-3a",
                "run_shell_command",
                new Dictionary<string, object?>
                {
                    ["command"] = CreatePrintWorkingDirectoryCommand(),
                    ["workingDirectory"] = "missing",
                }
            )
        );

        missingDirectoryResult.IsError.Should().BeTrue();
        missingDirectoryResult.Text.Should().ContainSingle().Which.Should().Contain("does not exist");

        var outsideRootResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-3b",
                "run_shell_command",
                new Dictionary<string, object?>
                {
                    ["command"] = CreatePrintWorkingDirectoryCommand(),
                    ["workingDirectory"] = "../outside",
                }
            )
        );

        outsideRootResult.IsError.Should().BeTrue();
        outsideRootResult.Text.Should().ContainSingle().Which.Should().Contain("outside the configured root");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTimeOutAndReturnErrorResult()
    {
        using var root = new TemporaryDirectory();
        var tool = CreateTool(
            root.Path,
            defaultTimeoutSeconds: 1,
            maxTimeoutSeconds: 1
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-4",
                "run_shell_command",
                new Dictionary<string, object?>
                {
                    ["command"] = CreateSleepWithInitialOutputCommand(seconds: 2),
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("timed out");
        result.Text[0].Should().Contain("start");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("timedOut").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTruncateOutputUsingHeadAndTail()
    {
        using var root = new TemporaryDirectory();
        var tool = CreateTool(
            root.Path,
            maxOutputBytes: 32 * 1024,
            maxOutputLines: 4
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-5",
                "run_shell_command",
                new Dictionary<string, object?>
                {
                    ["command"] = CreateMultiLineOutputCommand("line1", "line2", "line3", "line4", "line5", "line6"),
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("line1");
        result.Text[0].Should().Contain("line2");
        result.Text[0].Should().Contain("line5");
        result.Text[0].Should().Contain("line6");
        result.Text[0].Should().Contain("truncated");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldValidateArguments()
    {
        using var root = new TemporaryDirectory();
        var tool = CreateTool(root.Path);

        var missingCommandResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-6a",
                "run_shell_command",
                new Dictionary<string, object?> { ["command"] = "" }
            )
        );

        missingCommandResult.IsError.Should().BeTrue();
        missingCommandResult.Text.Should().ContainSingle().Which.Should().Contain("command");

        var invalidTimeoutResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-6b",
                "run_shell_command",
                new Dictionary<string, object?>
                {
                    ["command"] = CreatePrintWorkingDirectoryCommand(),
                    ["timeoutSeconds"] = 0,
                }
            )
        );

        invalidTimeoutResult.IsError.Should().BeTrue();
        invalidTimeoutResult.Text.Should().ContainSingle().Which.Should().Contain("timeoutSeconds");
    }

    private static RunShellCommandTool CreateTool(
        string rootPath,
        int defaultTimeoutSeconds = 120,
        int maxTimeoutSeconds = 300,
        int maxOutputBytes = 64 * 1024,
        int maxOutputLines = 2000
    )
    {
        var shell = GetConfiguredShell();
        var policy = new ProcessToolPolicy(
            rootPath,
            defaultTimeoutSeconds: defaultTimeoutSeconds,
            maxTimeoutSeconds: maxTimeoutSeconds,
            maxOutputBytes: maxOutputBytes,
            maxOutputLines: maxOutputLines
        );

        var created = RunShellCommandTool.TryCreate(
            policy,
            out var tool,
            out var unavailableReason,
            new RunShellCommandToolOptions(shell.Path, shell.Kind)
        );

        created.Should().BeTrue(unavailableReason);
        return tool!;
    }

    private static ConfiguredShell GetConfiguredShell()
    {
        if (OperatingSystem.IsWindows())
        {
            var commandShellPath =
                Environment.GetEnvironmentVariable("ComSpec")
                ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return new ConfiguredShell(commandShellPath, ShellKind.Cmd);
        }

        return new ConfiguredShell(File.Exists("/bin/sh") ? "/bin/sh" : "sh", ShellKind.Sh);
    }

    private static string CreateStdoutStderrExitCommand() =>
        OperatingSystem.IsWindows()
            ? "echo stdout-line & echo stderr-line 1>&2 & exit /b 7"
            : "printf 'stdout-line\\n'; printf 'stderr-line\\n' 1>&2; exit 7";

    private static string CreatePrintWorkingDirectoryCommand() =>
        OperatingSystem.IsWindows() ? "cd" : "pwd";

    private static string CreateSleepWithInitialOutputCommand(int seconds) =>
        OperatingSystem.IsWindows()
            ? $"echo start & ping -n {seconds + 2} 127.0.0.1 > nul"
            : $"printf 'start\\n'; sleep {seconds}";

    private static string CreateMultiLineOutputCommand(params string[] lines) =>
        OperatingSystem.IsWindows()
            ? string.Join(" & ", lines.Select(line => $"echo {line}"))
            : $"printf '%s\\n' {string.Join(" ", lines.Select(ToSingleQuotedShellLiteral))}";

    private static string ToSingleQuotedShellLiteral(string value) =>
        $"'{value.Replace("'", "'\"'\"'")}'";

    private sealed record ConfiguredShell(string Path, ShellKind Kind);
}
