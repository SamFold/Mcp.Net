namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Shared bounded process-execution policy for built-in local shell tools.
/// </summary>
public sealed class ProcessToolPolicy
{
    public ProcessToolPolicy(
        string rootPath,
        int defaultTimeoutSeconds = 120,
        int maxTimeoutSeconds = 300,
        int maxOutputBytes = 64 * 1024,
        int maxOutputLines = 2000,
        int maxConcurrentProcesses = 4
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (defaultTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultTimeoutSeconds));
        }

        if (maxTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTimeoutSeconds));
        }

        if (defaultTimeoutSeconds > maxTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultTimeoutSeconds),
                "The default timeout must be less than or equal to the maximum timeout."
            );
        }

        if (maxOutputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));
        }

        if (maxOutputLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputLines));
        }

        if (maxConcurrentProcesses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentProcesses));
        }

        RootPath = Path.GetFullPath(rootPath);
        DefaultTimeoutSeconds = defaultTimeoutSeconds;
        MaxTimeoutSeconds = maxTimeoutSeconds;
        MaxOutputBytes = maxOutputBytes;
        MaxOutputLines = maxOutputLines;
        MaxConcurrentProcesses = maxConcurrentProcesses;
    }

    public string RootPath { get; }

    public int DefaultTimeoutSeconds { get; }

    public int MaxTimeoutSeconds { get; }

    public int MaxOutputBytes { get; }

    public int MaxOutputLines { get; }

    public int MaxConcurrentProcesses { get; }

    public FileSystemToolPath ResolveWorkingDirectory(string? path)
    {
        var requestedPath = string.IsNullOrWhiteSpace(path) ? "." : path;
        var candidatePath = Path.IsPathRooted(requestedPath)
            ? requestedPath
            : Path.Combine(RootPath, requestedPath);
        var fullPath = Path.GetFullPath(candidatePath);
        var relativePath = Path.GetRelativePath(RootPath, fullPath);

        if (IsOutsideRoot(relativePath))
        {
            throw new InvalidOperationException(
                $"Path '{requestedPath}' is outside the configured root."
            );
        }

        return new FileSystemToolPath(fullPath, NormalizeDisplayPath(relativePath));
    }

    private static bool IsOutsideRoot(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return true;
        }

        if (relativePath == "..")
        {
            return true;
        }

        if (
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        )
        {
            return true;
        }

        return Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar
            && relativePath.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal
            );
    }

    private static string NormalizeDisplayPath(string relativePath)
    {
        if (relativePath == ".")
        {
            return ".";
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
