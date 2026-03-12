namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Shared bounded filesystem policy for built-in local tools.
/// </summary>
public sealed class FileSystemToolPolicy
{
    public FileSystemToolPolicy(
        string rootPath,
        int maxReadBytes = 32 * 1024,
        int maxReadLines = 500,
        int maxDirectoryEntries = 200
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (maxReadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReadBytes));
        }

        if (maxReadLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReadLines));
        }

        if (maxDirectoryEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDirectoryEntries));
        }

        RootPath = Path.GetFullPath(rootPath);
        MaxReadBytes = maxReadBytes;
        MaxReadLines = maxReadLines;
        MaxDirectoryEntries = maxDirectoryEntries;
    }

    public string RootPath { get; }

    public int MaxReadBytes { get; }

    public int MaxReadLines { get; }

    public int MaxDirectoryEntries { get; }

    public FileSystemToolPath Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var candidatePath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(RootPath, path);
        var fullPath = Path.GetFullPath(candidatePath);
        var relativePath = Path.GetRelativePath(RootPath, fullPath);

        if (IsOutsideRoot(relativePath))
        {
            throw new InvalidOperationException(
                $"Path '{path}' is outside the configured root."
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

        return relativePath.Replace('\\', '/');
    }
}
