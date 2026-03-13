using System.Diagnostics;

namespace Mcp.Net.Agent.Tools;

internal static class ShellCommandResolver
{
    private const int ValidationTimeoutMilliseconds = 2000;

    public static bool TryResolve(
        string? configuredPath,
        ShellKind? preferredShell,
        out ResolvedShell? resolvedShell,
        out string? unavailableReason
    )
    {
        resolvedShell = null;

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            foreach (var candidate in EnumerateConfiguredCandidates(configuredPath, preferredShell))
            {
                if (
                    TryValidateCandidate(
                        candidate,
                        out resolvedShell,
                        out unavailableReason
                    )
                )
                {
                    return true;
                }
            }

            unavailableReason =
                $"Configured shell '{configuredPath}' could not be executed.";
            return false;
        }

        foreach (var candidate in EnumerateDefaultCandidates(preferredShell))
        {
            if (
                TryValidateCandidate(
                    candidate,
                    out resolvedShell,
                    out unavailableReason
                )
            )
            {
                return true;
            }
        }

        unavailableReason = OperatingSystem.IsWindows()
            ? "No supported shell is available. Configure pwsh, powershell, or cmd.exe explicitly, or make one available on PATH."
            : "No supported shell is available. Configure bash or sh explicitly, or make one available on PATH.";
        return false;
    }

    private static IEnumerable<ShellCandidate> EnumerateConfiguredCandidates(
        string configuredPath,
        ShellKind? preferredShell
    )
    {
        if (preferredShell is { } configuredKind)
        {
            foreach (var candidatePath in ExpandConfiguredCandidates(configuredPath))
            {
                yield return new ShellCandidate(candidatePath, configuredKind);
            }

            yield break;
        }

        foreach (var candidatePath in ExpandConfiguredCandidates(configuredPath))
        {
            if (TryInferShellKind(candidatePath, out var inferredKind))
            {
                yield return new ShellCandidate(candidatePath, inferredKind);
            }
        }
    }

    private static IEnumerable<ShellCandidate> EnumerateDefaultCandidates(ShellKind? preferredShell)
    {
        foreach (var candidate in EnumerateCandidatesForOperatingSystem(preferredShell))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<ShellCandidate> EnumerateCandidatesForOperatingSystem(
        ShellKind? preferredShell
    )
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in EnumerateWindowsCandidates(preferredShell))
            {
                yield return candidate;
            }

            yield break;
        }

        foreach (var candidate in EnumerateUnixCandidates(preferredShell))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<ShellCandidate> EnumerateUnixCandidates(ShellKind? preferredShell)
    {
        if (preferredShell is null or ShellKind.Bash)
        {
            if (File.Exists("/bin/bash"))
            {
                yield return new ShellCandidate("/bin/bash", ShellKind.Bash);
            }

            foreach (var candidatePath in EnumeratePathCandidates("bash"))
            {
                yield return new ShellCandidate(candidatePath, ShellKind.Bash);
            }
        }

        if (preferredShell is null or ShellKind.Sh)
        {
            if (File.Exists("/bin/sh"))
            {
                yield return new ShellCandidate("/bin/sh", ShellKind.Sh);
            }

            foreach (var candidatePath in EnumeratePathCandidates("sh"))
            {
                yield return new ShellCandidate(candidatePath, ShellKind.Sh);
            }
        }
    }

    private static IEnumerable<ShellCandidate> EnumerateWindowsCandidates(ShellKind? preferredShell)
    {
        if (preferredShell is null or ShellKind.PowerShell)
        {
            foreach (var candidatePath in EnumeratePathCandidates("pwsh"))
            {
                yield return new ShellCandidate(candidatePath, ShellKind.PowerShell);
            }

            foreach (var candidatePath in EnumeratePathCandidates("powershell"))
            {
                yield return new ShellCandidate(candidatePath, ShellKind.PowerShell);
            }
        }

        if (preferredShell is null or ShellKind.Cmd)
        {
            var commandShell = Environment.GetEnvironmentVariable("ComSpec");
            if (!string.IsNullOrWhiteSpace(commandShell))
            {
                foreach (var candidatePath in ExpandExecutableExtensions(commandShell))
                {
                    yield return new ShellCandidate(candidatePath, ShellKind.Cmd);
                }
            }

            var systemCommandShell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            foreach (var candidatePath in ExpandExecutableExtensions(systemCommandShell))
            {
                yield return new ShellCandidate(candidatePath, ShellKind.Cmd);
            }

            foreach (var candidatePath in EnumeratePathCandidates("cmd"))
            {
                yield return new ShellCandidate(candidatePath, ShellKind.Cmd);
            }
        }
    }

    private static IEnumerable<string> ExpandConfiguredCandidates(string configuredPath)
    {
        if (
            Path.IsPathRooted(configuredPath)
            || configuredPath.Contains(Path.DirectorySeparatorChar)
            || configuredPath.Contains(Path.AltDirectorySeparatorChar)
        )
        {
            var fullPath = Path.GetFullPath(configuredPath);
            foreach (var candidatePath in ExpandExecutableExtensions(fullPath))
            {
                yield return candidatePath;
            }

            yield break;
        }

        foreach (var candidatePath in EnumeratePathCandidates(configuredPath))
        {
            yield return candidatePath;
        }
    }

    private static IEnumerable<string> EnumeratePathCandidates(string executableName)
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
                var candidatePath in ExpandExecutableExtensions(
                    Path.Combine(trimmedDirectory, executableName)
                )
            )
            {
                if (seen.Add(candidatePath))
                {
                    yield return candidatePath;
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
            yield return extension.StartsWith(".", StringComparison.Ordinal)
                ? extension
                : "." + extension;
        }
    }

    private static bool TryValidateCandidate(
        ShellCandidate candidate,
        out ResolvedShell? resolvedShell,
        out string? unavailableReason
    )
    {
        resolvedShell = null;
        unavailableReason = null;

        if (!File.Exists(candidate.Path))
        {
            return false;
        }

        try
        {
            var shell = new ResolvedShell(Path.GetFullPath(candidate.Path), candidate.Kind);
            using var process = new Process
            {
                StartInfo = shell.CreateStartInfo(
                    "exit 0",
                    Environment.CurrentDirectory
                ),
            };

            if (!process.Start())
            {
                unavailableReason = $"Failed to start shell '{candidate.Path}'.";
                return false;
            }

            process.StandardInput.Close();

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ValidationTimeoutMilliseconds))
            {
                TryKill(process);
                unavailableReason = $"Timed out while validating shell '{candidate.Path}'.";
                return false;
            }

            if (process.ExitCode != 0)
            {
                unavailableReason = string.IsNullOrWhiteSpace(standardError)
                    ? $"Shell validation failed for '{candidate.Path}' with exit code {process.ExitCode}."
                    : standardError.Trim();
                return false;
            }

            resolvedShell = shell;
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

    private static bool TryInferShellKind(string candidatePath, out ShellKind shellKind)
    {
        var fileName = Path.GetFileNameWithoutExtension(candidatePath);

        if (string.Equals(fileName, "bash", StringComparison.OrdinalIgnoreCase))
        {
            shellKind = ShellKind.Bash;
            return true;
        }

        if (string.Equals(fileName, "sh", StringComparison.OrdinalIgnoreCase))
        {
            shellKind = ShellKind.Sh;
            return true;
        }

        if (
            string.Equals(fileName, "pwsh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "powershell", StringComparison.OrdinalIgnoreCase)
        )
        {
            shellKind = ShellKind.PowerShell;
            return true;
        }

        if (string.Equals(fileName, "cmd", StringComparison.OrdinalIgnoreCase))
        {
            shellKind = ShellKind.Cmd;
            return true;
        }

        shellKind = default;
        return false;
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

    private readonly record struct ShellCandidate(string Path, ShellKind Kind);
}

internal sealed record ResolvedShell(string Path, ShellKind Kind)
{
    public ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        switch (Kind)
        {
            case ShellKind.Bash:
            case ShellKind.Sh:
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(command);
                break;
            case ShellKind.PowerShell:
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(command);
                break;
            case ShellKind.Cmd:
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/s");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(command);
                break;
            default:
                throw new InvalidOperationException($"Unsupported shell kind '{Kind}'.");
        }

        return startInfo;
    }
}
