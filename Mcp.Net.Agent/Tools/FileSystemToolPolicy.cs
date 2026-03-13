using System.Collections.Frozen;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Shared filesystem policy for built-in local tools.
/// </summary>
public sealed class FileSystemToolPolicy
{
    private static readonly StringComparer DirectoryNameComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly string[] DefaultSkippedDirectoryNames =
    {
        ".git",
        "node_modules",
        "bin",
        "obj",
        ".vs",
    };

    private readonly string[] _skippedDirectoryNames;

    public FileSystemToolPolicy(
        string basePath,
        FileSystemScopeMode scopeMode = FileSystemScopeMode.BoundedToBasePath,
        int maxReadBytes = 32 * 1024,
        int maxReadLines = 500,
        int maxDirectoryEntries = 200,
        int maxGlobMatches = 200,
        int maxGlobDepth = 50,
        bool ignoreInaccessible = true,
        bool followReparsePoints = false,
        IEnumerable<string>? skippedDirectoryNames = null,
        int maxEditableBytes = 128 * 1024,
        int maxEditsPerRequest = 16,
        bool requireExpectedContentHashForEdits = true,
        bool allowMutationThroughReparsePoints = false,
        int maxDiffPreviewLines = 24,
        int maxGrepMatches = 100,
        int maxGrepOutputBytes = 64 * 1024,
        int maxGrepLineLength = 500,
        int maxGrepContextLines = 3
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

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

        if (maxGlobMatches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGlobMatches));
        }

        if (maxGlobDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGlobDepth));
        }

        if (maxEditableBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEditableBytes));
        }

        if (maxEditsPerRequest <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEditsPerRequest));
        }

        if (maxDiffPreviewLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDiffPreviewLines));
        }

        if (maxGrepMatches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGrepMatches));
        }

        if (maxGrepOutputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGrepOutputBytes));
        }

        if (maxGrepLineLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGrepLineLength));
        }

        if (maxGrepContextLines < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGrepContextLines));
        }

        BasePath = Path.GetFullPath(basePath);
        ScopeMode = scopeMode;
        MaxReadBytes = maxReadBytes;
        MaxReadLines = maxReadLines;
        MaxDirectoryEntries = maxDirectoryEntries;
        MaxGlobMatches = maxGlobMatches;
        MaxGlobDepth = maxGlobDepth;
        MaxEditableBytes = maxEditableBytes;
        MaxEditsPerRequest = maxEditsPerRequest;
        RequireExpectedContentHashForEdits = requireExpectedContentHashForEdits;
        AllowMutationThroughReparsePoints = allowMutationThroughReparsePoints;
        MaxDiffPreviewLines = maxDiffPreviewLines;
        MaxGrepMatches = maxGrepMatches;
        MaxGrepOutputBytes = maxGrepOutputBytes;
        MaxGrepLineLength = maxGrepLineLength;
        MaxGrepContextLines = maxGrepContextLines;
        IgnoreInaccessible = ignoreInaccessible;
        FollowReparsePoints = followReparsePoints;

        _skippedDirectoryNames = (skippedDirectoryNames ?? DefaultSkippedDirectoryNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(DirectoryNameComparer)
            .ToArray();
        SkippedDirectoryNames = _skippedDirectoryNames.ToFrozenSet(DirectoryNameComparer);
    }

    public string BasePath { get; }

    public string RootPath => BasePath;

    public FileSystemScopeMode ScopeMode { get; }

    public int MaxReadBytes { get; }

    public int MaxReadLines { get; }

    public int MaxDirectoryEntries { get; }

    public int MaxGlobMatches { get; }

    public int MaxGlobDepth { get; }

    public int MaxEditableBytes { get; }

    public int MaxEditsPerRequest { get; }

    public bool RequireExpectedContentHashForEdits { get; }

    public bool AllowMutationThroughReparsePoints { get; }

    public int MaxDiffPreviewLines { get; }

    public int MaxGrepMatches { get; }

    public int MaxGrepOutputBytes { get; }

    public int MaxGrepLineLength { get; }

    public int MaxGrepContextLines { get; }

    public bool IgnoreInaccessible { get; }

    public bool FollowReparsePoints { get; }

    public IReadOnlySet<string> SkippedDirectoryNames { get; }

    public FileSystemToolPath Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var candidatePath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(BasePath, path);
        var fullPath = Path.GetFullPath(candidatePath);
        var relativePath = Path.GetRelativePath(BasePath, fullPath);

        if (
            ScopeMode == FileSystemScopeMode.BoundedToBasePath
            && IsOutsideBasePath(relativePath)
        )
        {
            throw new InvalidOperationException(
                $"Path '{path}' is outside the configured root."
            );
        }

        return new FileSystemToolPath(fullPath, ToDisplayPath(fullPath, relativePath));
    }

    public FileSystemToolPath ResolveOrBase(string? path) =>
        Resolve(string.IsNullOrWhiteSpace(path) ? "." : path);

    public string GetDisplayPath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        var normalizedFullPath = Path.GetFullPath(fullPath);
        var relativePath = Path.GetRelativePath(BasePath, normalizedFullPath);

        return ToDisplayPath(normalizedFullPath, relativePath);
    }

    internal bool ShouldSkipDirectory(ReadOnlySpan<char> directoryName)
    {
        foreach (var skippedDirectoryName in _skippedDirectoryNames)
        {
            if (
                directoryName.Equals(
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

    private string ToDisplayPath(string fullPath, string relativePath)
    {
        if (
            ScopeMode == FileSystemScopeMode.BoundedToBasePath
            && IsOutsideBasePath(relativePath)
        )
        {
            throw new InvalidOperationException(
                $"Path '{fullPath}' is outside the configured root."
            );
        }

        if (
            ScopeMode == FileSystemScopeMode.Unbounded
            && IsOutsideBasePath(relativePath)
        )
        {
            return NormalizeAbsoluteDisplayPath(fullPath);
        }

        return NormalizeRelativeDisplayPath(relativePath);
    }

    private static bool IsOutsideBasePath(string relativePath)
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

    private static string NormalizeRelativeDisplayPath(string relativePath)
    {
        if (relativePath == ".")
        {
            return ".";
        }

        return relativePath.Replace('\\', '/');
    }

    private static string NormalizeAbsoluteDisplayPath(string fullPath) =>
        Path.GetFullPath(fullPath).Replace('\\', '/');
}
