using System.Text;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Tools;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Tools;

public class GrepToolTests
{
    [Fact]
    public void TryCreate_ShouldReturnFalseWhenConfiguredRipgrepIsMissing()
    {
        using var root = new TemporaryDirectory();
        var policy = new FileSystemToolPolicy(root.Path);

        var created = GrepTool.TryCreate(
            policy,
            out var tool,
            out var unavailableReason,
            new GrepToolOptions(Path.Combine(root.Path, "missing-rg"))
        );

        created.Should().BeFalse();
        tool.Should().BeNull();
        unavailableReason.Should().Contain("ripgrep");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFormattedMatchesAndUseLiteralSearchByDefault()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));

        using var fakeRipgrep = new FakeRipgrepExecutable(
            root.Path,
            stdoutLines:
            [
                CreateBeginEvent(Path.Combine(root.Path, "src", "alpha.cs")),
                CreateMatchEvent(Path.Combine(root.Path, "src", "alpha.cs"), 2, "alpha beta\n"),
                CreateEndEvent(Path.Combine(root.Path, "src", "alpha.cs"), searches: 1, searchesWithMatch: 1, matchedLines: 1),
                CreateSummaryEvent(searches: 1, searchesWithMatch: 1, matchedLines: 1),
            ]
        );

        var tool = CreateTool(root.Path, fakeRipgrep.ExecutablePath);

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "grep_files",
                new Dictionary<string, object?> { ["pattern"] = "alpha beta" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("src/alpha.cs:2: alpha beta");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be(".");
        result.Metadata!.Value.GetProperty("matchCount").GetInt32().Should().Be(1);
        result.Metadata!.Value.GetProperty("filesSearched").GetInt32().Should().Be(1);
        result.Metadata!.Value.GetProperty("filesMatched").GetInt32().Should().Be(1);
        result.Metadata!.Value.GetProperty("truncatedByMatches").GetBoolean().Should().BeFalse();
        result.Metadata!.Value.GetProperty("truncatedByBytes").GetBoolean().Should().BeFalse();
        result.Metadata!.Value.GetProperty("engine").GetString().Should().Be("ripgrep");

        var arguments = await fakeRipgrep.ReadArgumentsAsync();
        arguments.Should().Contain("--fixed-strings");
        arguments.Should().Contain("--sort");
        arguments.Should().Contain("alpha beta");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldEmitContextSeparatorsAndTruncateDisplayedLines()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "one.txt"), "alpha\nbeta\n");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "two.txt"), "0123456789abcdefghij\n");

        using var fakeRipgrep = new FakeRipgrepExecutable(
            root.Path,
            stdoutLines:
            [
                CreateBeginEvent(Path.Combine(root.Path, "one.txt")),
                CreateMatchEvent(Path.Combine(root.Path, "one.txt"), 1, "alpha\n"),
                CreateContextEvent(Path.Combine(root.Path, "one.txt"), 2, "beta\n"),
                CreateEndEvent(Path.Combine(root.Path, "one.txt"), searches: 1, searchesWithMatch: 1, matchedLines: 1),
                CreateBeginEvent(Path.Combine(root.Path, "two.txt")),
                CreateMatchEvent(Path.Combine(root.Path, "two.txt"), 5, "0123456789abcdefghij\n"),
                CreateEndEvent(Path.Combine(root.Path, "two.txt"), searches: 1, searchesWithMatch: 1, matchedLines: 1),
                CreateSummaryEvent(searches: 2, searchesWithMatch: 2, matchedLines: 2),
            ]
        );

        var tool = CreateTool(
            root.Path,
            fakeRipgrep.ExecutablePath,
            maxGrepLineLength: 10,
            maxGrepContextLines: 1
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-2",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "alpha",
                    ["contextLines"] = 1,
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        var lines = result.Text[0].Split('\n');
        lines
            .Take(4)
            .Should()
            .Equal("one.txt:1: alpha", "one.txt-2- beta", "--", "two.txt:5: 0123456789... [truncated]");
        result.Text[0].Should().Contain("Some lines were truncated");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("linesTruncated").GetBoolean().Should().BeTrue();

        var arguments = await fakeRipgrep.ReadArgumentsAsync();
        arguments.Should().Contain("-C");
        arguments.Should().Contain("1");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldClampLimitToPolicyAndReportTruncation()
    {
        using var root = new TemporaryDirectory();
        using var fakeRipgrep = new FakeRipgrepExecutable(
            root.Path,
            stdoutLines:
            [
                CreateBeginEvent(Path.Combine(root.Path, "alpha.txt")),
                CreateMatchEvent(Path.Combine(root.Path, "alpha.txt"), 1, "one\n"),
                CreateMatchEvent(Path.Combine(root.Path, "alpha.txt"), 2, "two\n"),
                CreateMatchEvent(Path.Combine(root.Path, "alpha.txt"), 3, "three\n"),
                CreateSummaryEvent(searches: 1, searchesWithMatch: 1, matchedLines: 3),
            ]
        );

        var tool = CreateTool(root.Path, fakeRipgrep.ExecutablePath, maxGrepMatches: 2);

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-3",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "o",
                    ["limit"] = 50,
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("alpha.txt:1: one");
        result.Text[0].Should().Contain("alpha.txt:2: two");
        result.Text[0].Should().NotContain("alpha.txt:3: three");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("limit").GetInt32().Should().Be(2);
        result.Metadata!.Value.GetProperty("matchCount").GetInt32().Should().Be(2);
        result.Metadata!.Value.GetProperty("truncatedByMatches").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTruncateOutputWhenByteBudgetIsExceeded()
    {
        using var root = new TemporaryDirectory();
        using var fakeRipgrep = new FakeRipgrepExecutable(
            root.Path,
            stdoutLines:
            [
                CreateBeginEvent(Path.Combine(root.Path, "alpha.txt")),
                CreateMatchEvent(Path.Combine(root.Path, "alpha.txt"), 1, "one\n"),
                CreateMatchEvent(Path.Combine(root.Path, "alpha.txt"), 2, "two\n"),
                CreateSummaryEvent(searches: 1, searchesWithMatch: 1, matchedLines: 2),
            ]
        );

        var tool = CreateTool(root.Path, fakeRipgrep.ExecutablePath, maxGrepOutputBytes: 20);

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-3b",
                "grep_files",
                new Dictionary<string, object?> { ["pattern"] = "o" }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle();
        result.Text[0].Should().Contain("alpha.txt:1: one");
        result.Text[0].Should().NotContain("alpha.txt:2: two");
        result.Text[0].Should().Contain("Output truncated to 20 bytes.");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("truncatedByBytes").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassThroughIgnoreCaseWordAndGlobAndClampContextLines()
    {
        using var root = new TemporaryDirectory();
        using var fakeRipgrep = new FakeRipgrepExecutable(root.Path, exitCode: 1);
        var tool = CreateTool(
            root.Path,
            fakeRipgrep.ExecutablePath,
            maxGrepContextLines: 1
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-3c",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "Token",
                    ["glob"] = "*.cs",
                    ["ignoreCase"] = true,
                    ["word"] = true,
                    ["contextLines"] = 5,
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("No matches found");

        var arguments = await fakeRipgrep.ReadArgumentsAsync();
        var argumentLines = arguments.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        arguments.Should().Contain("--ignore-case");
        arguments.Should().Contain("--word-regexp");
        arguments.Should().Contain("--glob");
        arguments.Should().Contain("*.cs");
        argumentLines.Should().Contain("-C");
        argumentLines.Should().Contain("1");
        arguments.Should().NotContain("5\n");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAddSkipGlobsUnlessPathExplicitlyTargetsSkippedDirectory()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));

        using var defaultScopeRipgrep = new FakeRipgrepExecutable(root.Path, exitCode: 1);
        var defaultScopeTool = CreateTool(root.Path, defaultScopeRipgrep.ExecutablePath);

        var defaultScopeResult = await defaultScopeTool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-4",
                "grep_files",
                new Dictionary<string, object?> { ["pattern"] = "alpha" }
            )
        );

        defaultScopeResult.IsError.Should().BeFalse();
        defaultScopeResult.Text.Should().ContainSingle().Which.Should().Be("No matches found");
        var defaultArguments = await defaultScopeRipgrep.ReadArgumentsAsync();
        defaultArguments.Should().Contain("!**/.git");
        defaultArguments.Should().Contain("!**/node_modules");

        using var skippedScopeRipgrep = new FakeRipgrepExecutable(root.Path, exitCode: 1);
        var skippedScopeTool = CreateTool(root.Path, skippedScopeRipgrep.ExecutablePath);

        var skippedScopeResult = await skippedScopeTool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-5",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "alpha",
                    ["path"] = ".git",
                }
            )
        );

        skippedScopeResult.IsError.Should().BeFalse();
        var skippedArguments = await skippedScopeRipgrep.ReadArgumentsAsync();
        skippedArguments.Should().NotContain("!**/.git");
        skippedArguments.Should().Contain("!**/node_modules");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAcceptFilePathScopeAndKeepOutputRootRelative()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "src", "alpha.cs"), "token\n");

        using var fakeRipgrep = new FakeRipgrepExecutable(
            root.Path,
            stdoutLines:
            [
                CreateBeginEvent(Path.Combine(root.Path, "src", "alpha.cs")),
                CreateMatchEvent(Path.Combine(root.Path, "src", "alpha.cs"), 1, "token\n"),
                CreateSummaryEvent(searches: 1, searchesWithMatch: 1, matchedLines: 1),
            ]
        );
        var tool = CreateTool(root.Path, fakeRipgrep.ExecutablePath);

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-5a",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "token",
                    ["path"] = "src/alpha.cs",
                }
            )
        );

        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be("src/alpha.cs:1: token");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value.GetProperty("path").GetString().Should().Be("src/alpha.cs");

        var arguments = await fakeRipgrep.ReadArgumentsAsync();
        arguments.Should().Contain(Path.Combine(root.Path, "src", "alpha.cs"));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnAbsolutePathsForMatchesOutsideBasePathWhenUnbounded()
    {
        using var workspace = new TemporaryDirectory();
        var basePath = Path.Combine(workspace.Path, "repo");
        var externalPath = Path.Combine(workspace.Path, "external");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(externalPath);
        await File.WriteAllTextAsync(Path.Combine(externalPath, "alpha.cs"), "token\n");

        var externalFilePath = Path.Combine(externalPath, "alpha.cs");
        using var fakeRipgrep = new FakeRipgrepExecutable(
            basePath,
            stdoutLines:
            [
                CreateBeginEvent(externalFilePath),
                CreateMatchEvent(externalFilePath, 1, "token\n"),
                CreateSummaryEvent(searches: 1, searchesWithMatch: 1, matchedLines: 1),
            ]
        );
        var tool = CreateTool(
            basePath,
            fakeRipgrep.ExecutablePath,
            scopeMode: FileSystemScopeMode.Unbounded
        );

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-5u",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "token",
                    ["path"] = "../external",
                }
            )
        );

        var expectedPath = Path.GetFullPath(externalFilePath).Replace('\\', '/');
        result.IsError.Should().BeFalse();
        result.Text.Should().ContainSingle().Which.Should().Be($"{expectedPath}:1: token");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Value
            .GetProperty("path")
            .GetString()
            .Should()
            .Be(Path.GetFullPath(externalPath).Replace('\\', '/'));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnErrorWhenRipgrepReportsInvalidPattern()
    {
        using var root = new TemporaryDirectory();
        using var fakeRipgrep = new FakeRipgrepExecutable(
            root.Path,
            stderrText: "regex parse error:",
            exitCode: 2
        );

        var tool = CreateTool(root.Path, fakeRipgrep.ExecutablePath);

        var result = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-6",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "(",
                    ["literal"] = false,
                }
            )
        );

        result.IsError.Should().BeTrue();
        result.Text.Should().ContainSingle().Which.Should().Contain("regex parse error");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldValidateArgumentsBeforeInvokingRipgrep()
    {
        using var root = new TemporaryDirectory();
        using var fakeRipgrep = new FakeRipgrepExecutable(root.Path, exitCode: 1);
        var tool = CreateTool(root.Path, fakeRipgrep.ExecutablePath);

        var missingPatternResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-6a",
                "grep_files",
                new Dictionary<string, object?> { ["pattern"] = "" }
            )
        );

        missingPatternResult.IsError.Should().BeTrue();
        missingPatternResult.Text.Should().ContainSingle().Which.Should().Contain("pattern");

        var invalidLimitResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-6b",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "alpha",
                    ["limit"] = 0,
                }
            )
        );

        invalidLimitResult.IsError.Should().BeTrue();
        invalidLimitResult.Text.Should().ContainSingle().Which.Should().Contain("limit");

        var invalidContextResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-6c",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "alpha",
                    ["contextLines"] = -1,
                }
            )
        );

        invalidContextResult.IsError.Should().BeTrue();
        invalidContextResult.Text.Should().ContainSingle().Which.Should().Contain("contextLines");

        var missingPathResult = await tool.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-6d",
                "grep_files",
                new Dictionary<string, object?>
                {
                    ["pattern"] = "alpha",
                    ["path"] = "missing.txt",
                }
            )
        );

        missingPathResult.IsError.Should().BeTrue();
        missingPathResult.Text.Should().ContainSingle().Which.Should().Contain("does not exist");
    }

    private static GrepTool CreateTool(
        string rootPath,
        string ripgrepPath,
        int maxGrepMatches = 100,
        int maxGrepOutputBytes = 64 * 1024,
        int maxGrepLineLength = 500,
        int maxGrepContextLines = 3,
        FileSystemScopeMode scopeMode = FileSystemScopeMode.BoundedToBasePath
    )
    {
        var policy = new FileSystemToolPolicy(
            rootPath,
            scopeMode: scopeMode,
            maxGrepMatches: maxGrepMatches,
            maxGrepOutputBytes: maxGrepOutputBytes,
            maxGrepLineLength: maxGrepLineLength,
            maxGrepContextLines: maxGrepContextLines
        );

        var created = GrepTool.TryCreate(
            policy,
            out var tool,
            out var unavailableReason,
            new GrepToolOptions(ripgrepPath)
        );

        created.Should().BeTrue(unavailableReason);
        return tool!;
    }

    private static string CreateBeginEvent(string filePath) =>
        JsonSerializer.Serialize(
            new
            {
                type = "begin",
                data = new
                {
                    path = new { text = filePath },
                },
            }
        );

    private static string CreateMatchEvent(string filePath, int lineNumber, string text) =>
        JsonSerializer.Serialize(
            new
            {
                type = "match",
                data = new
                {
                    path = new { text = filePath },
                    lines = new { text },
                    line_number = lineNumber,
                    absolute_offset = 0,
                    submatches = Array.Empty<object>(),
                },
            }
        );

    private static string CreateContextEvent(string filePath, int lineNumber, string text) =>
        JsonSerializer.Serialize(
            new
            {
                type = "context",
                data = new
                {
                    path = new { text = filePath },
                    lines = new { text },
                    line_number = lineNumber,
                    absolute_offset = 0,
                    submatches = Array.Empty<object>(),
                },
            }
        );

    private static string CreateEndEvent(
        string filePath,
        int searches,
        int searchesWithMatch,
        int matchedLines
    ) =>
        JsonSerializer.Serialize(
            new
            {
                type = "end",
                data = new
                {
                    path = new { text = filePath },
                    binary_offset = (int?)null,
                    stats = new
                    {
                        elapsed = new
                        {
                            secs = 0,
                            nanos = 1,
                            human = "0.000001s",
                        },
                        searches,
                        searches_with_match = searchesWithMatch,
                        bytes_searched = 1,
                        bytes_printed = 1,
                        matched_lines = matchedLines,
                        matches = matchedLines,
                    },
                },
            }
        );

    private static string CreateSummaryEvent(int searches, int searchesWithMatch, int matchedLines) =>
        JsonSerializer.Serialize(
            new
            {
                type = "summary",
                data = new
                {
                    elapsed_total = new
                    {
                        human = "0.000001s",
                        nanos = 1,
                        secs = 0,
                    },
                    stats = new
                    {
                        bytes_printed = 1,
                        bytes_searched = 1,
                        elapsed = new
                        {
                            human = "0.000001s",
                            nanos = 1,
                            secs = 0,
                        },
                        matched_lines = matchedLines,
                        matches = matchedLines,
                        searches,
                        searches_with_match = searchesWithMatch,
                    },
                },
            }
        );
}

internal sealed class FakeRipgrepExecutable : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly string _argumentsPath;

    public FakeRipgrepExecutable(
        string rootPath,
        IReadOnlyList<string>? stdoutLines = null,
        string stderrText = "",
        int exitCode = 0
    )
    {
        ArgumentNullException.ThrowIfNull(rootPath);

        _argumentsPath = Path.Combine(_directory.Path, "args.txt");
        var stdoutPath = Path.Combine(_directory.Path, "stdout.txt");
        var stderrPath = Path.Combine(_directory.Path, "stderr.txt");
        File.WriteAllText(stdoutPath, stdoutLines is null ? string.Empty : string.Join('\n', stdoutLines) + '\n');
        File.WriteAllText(stderrPath, stderrText);

        if (OperatingSystem.IsWindows())
        {
            ExecutablePath = Path.Combine(_directory.Path, "rg.cmd");
            File.WriteAllText(
                ExecutablePath,
                $$"""
                @echo off
                if "%~1"=="--version" (
                  echo ripgrep 15.1.0
                  exit /b 0
                )
                break > "{{_argumentsPath}}"
                :args
                if "%~1"=="" goto afterargs
                >> "{{_argumentsPath}}" echo %~1
                shift
                goto args
                :afterargs
                if exist "{{stdoutPath}}" type "{{stdoutPath}}"
                if exist "{{stderrPath}}" type "{{stderrPath}}" 1>&2
                exit /b {{exitCode}}
                """
            );
        }
        else
        {
            ExecutablePath = Path.Combine(_directory.Path, "rg");
            File.WriteAllText(
                ExecutablePath,
                $$"""
                #!/bin/sh
                if [ "$1" = "--version" ]; then
                  echo "ripgrep 15.1.0"
                  exit 0
                fi
                : > "{{_argumentsPath}}"
                for arg in "$@"; do
                  printf '%s\n' "$arg" >> "{{_argumentsPath}}"
                done
                if [ -f "{{stdoutPath}}" ]; then
                  cat "{{stdoutPath}}"
                fi
                if [ -f "{{stderrPath}}" ]; then
                  cat "{{stderrPath}}" >&2
                fi
                exit {{exitCode}}
                """
            );
            File.SetUnixFileMode(
                ExecutablePath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
        }
    }

    public string ExecutablePath { get; }

    public async Task<string> ReadArgumentsAsync()
    {
        if (!File.Exists(_argumentsPath))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(_argumentsPath);
    }

    public void Dispose() => _directory.Dispose();
}
