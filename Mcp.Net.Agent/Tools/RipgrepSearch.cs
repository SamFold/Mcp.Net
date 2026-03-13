using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Mcp.Net.Agent.Tools;

internal sealed class RipgrepSearch
{
    private static readonly string[] NoMatchesText = ["No matches found"];
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly FileSystemToolPolicy _policy;
    private readonly string _ripgrepPath;

    public RipgrepSearch(FileSystemToolPolicy policy, string ripgrepPath)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        ArgumentException.ThrowIfNullOrWhiteSpace(ripgrepPath);
        _ripgrepPath = ripgrepPath;
    }

    public async Task<GrepSearchResult> SearchAsync(
        FileSystemToolPath basePath,
        GrepSearchRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(request);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(basePath, request),
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start ripgrep.");
        }

        using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            var target = (Process)state!;
            TryKill(target);
        }, process);

        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var builder = new StringBuilder();
        var currentOutputBytes = 0;
        var filesMatched = new HashSet<string>(PathComparer);
        // Keep a running begin-event count so killed/short-circuited searches still report
        // useful numbers when ripgrep never emits the final summary event.
        var filesSearched = 0;
        var summaryFilesSearched = -1;
        var summaryFilesMatched = -1;
        var matchCount = 0;
        var truncatedByMatches = false;
        var truncatedByBytes = false;
        var linesTruncated = false;
        string? lastEmittedPath = null;
        var lastEmittedLineNumber = -1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (
                !TryParseEvent(
                    line,
                    out var eventType,
                    out var fullPath,
                    out var lineNumber,
                    out var text,
                    out var parsedFilesSearched,
                    out var parsedFilesMatched
                )
            )
            {
                continue;
            }

            if (eventType == RipgrepEventType.Begin)
            {
                filesSearched++;
                continue;
            }

            if (eventType == RipgrepEventType.Summary)
            {
                summaryFilesSearched = parsedFilesSearched;
                summaryFilesMatched = parsedFilesMatched;
                continue;
            }

            if (eventType is not (RipgrepEventType.Match or RipgrepEventType.Context))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(fullPath) || lineNumber <= 0)
            {
                continue;
            }

            var displayPath = NormalizeDisplayPath(Path.GetRelativePath(_policy.RootPath, fullPath));

            if (eventType == RipgrepEventType.Match)
            {
                matchCount++;
                filesMatched.Add(displayPath);
            }

            var displayText = TrimLineEnding(text ?? string.Empty);
            displayText = TruncateDisplayText(displayText, ref linesTruncated);

            if (
                builder.Length > 0
                && (
                    !string.Equals(lastEmittedPath, displayPath, StringComparison.Ordinal)
                    || lineNumber > lastEmittedLineNumber + 1
                )
            )
            {
                if (!TryAppendLine(builder, "--", ref currentOutputBytes, _policy.MaxGrepOutputBytes))
                {
                    truncatedByBytes = true;
                    break;
                }
            }

            var formattedLine = eventType == RipgrepEventType.Match
                ? $"{displayPath}:{lineNumber}: {displayText}"
                : $"{displayPath}-{lineNumber}- {displayText}";
            if (
                !TryAppendLine(
                    builder,
                    formattedLine,
                    ref currentOutputBytes,
                    _policy.MaxGrepOutputBytes
                )
            )
            {
                truncatedByBytes = true;
                break;
            }

            lastEmittedPath = displayPath;
            lastEmittedLineNumber = lineNumber;

            if (eventType == RipgrepEventType.Match && matchCount >= request.Limit)
            {
                truncatedByMatches = true;
                break;
            }
        }

        if (truncatedByMatches || truncatedByBytes)
        {
            TryKill(process);
        }

        await process.WaitForExitAsync(CancellationToken.None);
        var standardError = await standardErrorTask;
        cancellationToken.ThrowIfCancellationRequested();

        if (!truncatedByMatches && !truncatedByBytes && process.ExitCode is not 0 and not 1)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError)
                    ? $"ripgrep exited with code {process.ExitCode}."
                    : standardError.Trim()
            );
        }

        if (matchCount == 0)
        {
            return new GrepSearchResult(
                FormattedOutput: NoMatchesText[0],
                MatchCount: 0,
                FilesSearched: summaryFilesSearched >= 0 ? summaryFilesSearched : filesSearched,
                FilesMatched: 0,
                TruncatedByMatches: false,
                TruncatedByBytes: false,
                LinesTruncated: false
            );
        }

        AppendNotices(builder, request, matchCount, truncatedByMatches, truncatedByBytes, linesTruncated);

        return new GrepSearchResult(
            FormattedOutput: builder.ToString(),
            MatchCount: matchCount,
            FilesSearched: summaryFilesSearched >= 0 ? summaryFilesSearched : filesSearched,
            FilesMatched: summaryFilesMatched >= 0 ? summaryFilesMatched : filesMatched.Count,
            TruncatedByMatches: truncatedByMatches,
            TruncatedByBytes: truncatedByBytes,
            LinesTruncated: linesTruncated
        );
    }

    private ProcessStartInfo CreateStartInfo(FileSystemToolPath basePath, GrepSearchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ripgrepPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add("--line-number");
        startInfo.ArgumentList.Add("--column");
        startInfo.ArgumentList.Add("--color=never");
        startInfo.ArgumentList.Add("--hidden");
        startInfo.ArgumentList.Add("--threads");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--sort");
        startInfo.ArgumentList.Add("path");
        startInfo.ArgumentList.Add("--no-ignore");
        startInfo.ArgumentList.Add("--no-ignore-parent");
        startInfo.ArgumentList.Add("--no-ignore-dot");

        if (_policy.IgnoreInaccessible)
        {
            startInfo.ArgumentList.Add("--no-messages");
        }

        if (_policy.FollowReparsePoints)
        {
            startInfo.ArgumentList.Add("--follow");
        }

        if (request.Literal)
        {
            startInfo.ArgumentList.Add("--fixed-strings");
        }

        if (request.IgnoreCase)
        {
            startInfo.ArgumentList.Add("--ignore-case");
        }

        if (request.Word)
        {
            startInfo.ArgumentList.Add("--word-regexp");
        }

        if (request.ContextLines > 0)
        {
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(request.ContextLines.ToString());
        }

        if (!string.IsNullOrWhiteSpace(request.Glob))
        {
            startInfo.ArgumentList.Add("--glob");
            startInfo.ArgumentList.Add(request.Glob.Replace('\\', '/'));
        }

        foreach (var skippedDirectoryName in _policy.SkippedDirectoryNames)
        {
            if (ShouldBypassSkippedDirectory(basePath, skippedDirectoryName))
            {
                continue;
            }

            startInfo.ArgumentList.Add("--glob");
            startInfo.ArgumentList.Add($"!**/{skippedDirectoryName}");
        }

        startInfo.ArgumentList.Add(request.Pattern);
        startInfo.ArgumentList.Add(basePath.FullPath);
        return startInfo;
    }

    private static bool TryParseEvent(
        string line,
        out RipgrepEventType eventType,
        out string? fullPath,
        out int lineNumber,
        out string? text,
        out int filesSearched,
        out int filesMatched
    )
    {
        eventType = RipgrepEventType.Unknown;
        fullPath = null;
        lineNumber = 0;
        text = null;
        filesSearched = 0;
        filesMatched = 0;

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeProperty))
        {
            return false;
        }

        var type = typeProperty.GetString();
        eventType = type switch
        {
            "begin" => RipgrepEventType.Begin,
            "match" => RipgrepEventType.Match,
            "context" => RipgrepEventType.Context,
            "summary" => RipgrepEventType.Summary,
            _ => RipgrepEventType.Unknown,
        };

        if (eventType == RipgrepEventType.Unknown)
        {
            return false;
        }

        if (!root.TryGetProperty("data", out var data))
        {
            return true;
        }

        if (eventType == RipgrepEventType.Summary)
        {
            if (
                data.TryGetProperty("stats", out var stats)
                && stats.TryGetProperty("searches", out var searches)
            )
            {
                filesSearched = searches.GetInt32();
            }

            if (
                data.TryGetProperty("stats", out stats)
                && stats.TryGetProperty("searches_with_match", out var searchesWithMatch)
            )
            {
                filesMatched = searchesWithMatch.GetInt32();
            }

            return true;
        }

        if (
            data.TryGetProperty("path", out var path)
            && path.TryGetProperty("text", out var pathText)
        )
        {
            fullPath = pathText.GetString();
        }

        if (eventType is RipgrepEventType.Match or RipgrepEventType.Context)
        {
            if (data.TryGetProperty("line_number", out var lineNumberProperty))
            {
                lineNumber = lineNumberProperty.GetInt32();
            }

            if (
                data.TryGetProperty("lines", out var lines)
                && lines.TryGetProperty("text", out var lineText)
            )
            {
                text = lineText.GetString();
            }
        }

        return true;
    }

    private bool ShouldBypassSkippedDirectory(
        FileSystemToolPath basePath,
        string skippedDirectoryName
    )
    {
        if (basePath.DisplayPath == ".")
        {
            return false;
        }

        foreach (var segment in basePath.DisplayPath.Split('/'))
        {
            if (
                string.Equals(
                    segment,
                    skippedDirectoryName,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDisplayPath(string relativePath) =>
        relativePath.Replace('\\', '/');

    private static string TrimLineEnding(string text) => text.TrimEnd('\r', '\n');

    private string TruncateDisplayText(string text, ref bool linesTruncated)
    {
        if (text.Length <= _policy.MaxGrepLineLength)
        {
            return text;
        }

        const string suffix = "... [truncated]";
        linesTruncated = true;
        return string.Concat(text.AsSpan(0, _policy.MaxGrepLineLength), suffix);
    }

    private static bool TryAppendLine(
        StringBuilder builder,
        string line,
        ref int currentOutputBytes,
        int maxOutputBytes
    )
    {
        var lineBytes = Encoding.UTF8.GetByteCount(line);
        var addedBytes = lineBytes;
        if (builder.Length > 0)
        {
            addedBytes += 1;
        }

        if (currentOutputBytes + addedBytes > maxOutputBytes)
        {
            return false;
        }

        if (builder.Length > 0)
        {
            builder.Append('\n');
            currentOutputBytes++;
        }

        builder.Append(line);
        currentOutputBytes += lineBytes;
        return true;
    }

    private void AppendNotices(
        StringBuilder builder,
        GrepSearchRequest request,
        int matchCount,
        bool truncatedByMatches,
        bool truncatedByBytes,
        bool linesTruncated
    )
    {
        var notices = new List<string>();

        if (truncatedByMatches)
        {
            var suggestedLimit = Math.Min(
                request.Limit * 2,
                _policy.MaxGrepMatches
            );
            notices.Add(
                $"{matchCount} matches shown (limit reached). Use limit={suggestedLimit} for more, or refine the pattern."
            );
        }

        if (truncatedByBytes)
        {
            notices.Add($"Output truncated to {_policy.MaxGrepOutputBytes} bytes.");
        }

        if (linesTruncated)
        {
            notices.Add(
                $"Some lines were truncated to {_policy.MaxGrepLineLength} characters. Use read_file for full lines."
            );
        }

        if (notices.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.Append('[');
        builder.Append(string.Join(" ", notices));
        builder.Append(']');
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

internal sealed record GrepSearchRequest(
    string Pattern,
    string? Glob,
    bool Literal,
    bool IgnoreCase,
    bool Word,
    int ContextLines,
    int Limit
);

internal sealed record GrepSearchResult(
    string FormattedOutput,
    int MatchCount,
    int FilesSearched,
    int FilesMatched,
    bool TruncatedByMatches,
    bool TruncatedByBytes,
    bool LinesTruncated
);

internal enum RipgrepEventType
{
    Unknown,
    Begin,
    Match,
    Context,
    Summary,
}
