using System.Diagnostics;

namespace Mcp.Net.Agent.Tools;

internal static class RipgrepCommandResolver
{
    private const string ExecutableName = "rg";
    private const int ValidationTimeoutMilliseconds = 2000;

    public static bool TryResolve(
        string? configuredPath,
        out string? resolvedPath,
        out string? unavailableReason
    )
    {
        resolvedPath = null;

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            foreach (var candidate in EnumerateConfiguredCandidates(configuredPath))
            {
                if (
                    TryValidateCandidate(
                        candidate,
                        out resolvedPath,
                        out unavailableReason
                    )
                )
                {
                    return true;
                }
            }

            unavailableReason =
                $"Configured ripgrep executable '{configuredPath}' could not be executed.";
            return false;
        }

        foreach (var candidate in EnumeratePathCandidates())
        {
            if (
                TryValidateCandidate(
                    candidate,
                    out resolvedPath,
                    out unavailableReason
                )
            )
            {
                return true;
            }
        }

        unavailableReason =
            "ripgrep (rg) is not available. Install it on PATH or configure an explicit executable path.";
        return false;
    }

    private static IEnumerable<string> EnumerateConfiguredCandidates(string configuredPath)
    {
        var fullPath = Path.GetFullPath(configuredPath);
        foreach (var candidate in ExpandExecutableExtensions(fullPath))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> EnumeratePathCandidates()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        var seen = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal
        );

        foreach (
            var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var trimmedDirectory = directory.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDirectory))
            {
                continue;
            }

            foreach (
                var candidate in ExpandExecutableExtensions(
                    Path.Combine(trimmedDirectory, ExecutableName)
                )
            )
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> ExpandExecutableExtensions(string candidatePath)
    {
        if (!OperatingSystem.IsWindows() || !string.IsNullOrWhiteSpace(Path.GetExtension(candidatePath)))
        {
            yield return candidatePath;
            yield break;
        }

        foreach (var extension in GetWindowsExecutableExtensions())
        {
            yield return candidatePath + extension;
        }
    }

    private static IEnumerable<string> GetWindowsExecutableExtensions()
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
        {
            yield return ".exe";
            yield return ".cmd";
            yield return ".bat";
            yield return ".com";
            yield break;
        }

        foreach (
            var extension in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            if (extension.StartsWith(".", StringComparison.Ordinal))
            {
                yield return extension;
            }
            else
            {
                yield return "." + extension;
            }
        }
    }

    private static bool TryValidateCandidate(
        string candidatePath,
        out string? resolvedPath,
        out string? unavailableReason
    )
    {
        resolvedPath = null;
        unavailableReason = null;

        if (!File.Exists(candidatePath))
        {
            return false;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = candidatePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("--version");

            if (!process.Start())
            {
                unavailableReason = $"Failed to start ripgrep executable '{candidatePath}'.";
                return false;
            }

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ValidationTimeoutMilliseconds))
            {
                TryKill(process);
                unavailableReason =
                    $"Timed out while validating ripgrep executable '{candidatePath}'.";
                return false;
            }

            if (process.ExitCode != 0)
            {
                unavailableReason = string.IsNullOrWhiteSpace(standardError)
                    ? $"ripgrep validation failed for '{candidatePath}' with exit code {process.ExitCode}."
                    : standardError.Trim();
                return false;
            }

            if (
                !standardOutput.Contains("ripgrep", StringComparison.OrdinalIgnoreCase)
                && !standardError.Contains("ripgrep", StringComparison.OrdinalIgnoreCase)
            )
            {
                unavailableReason =
                    $"Executable '{candidatePath}' did not identify itself as ripgrep.";
                return false;
            }

            resolvedPath = Path.GetFullPath(candidatePath);
            return true;
        }
        catch (Exception ex)
            when (
                ex
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            unavailableReason = ex.Message;
            return false;
        }
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
