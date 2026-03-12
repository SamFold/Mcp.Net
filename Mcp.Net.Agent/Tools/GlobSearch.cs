using System.IO.Enumeration;

namespace Mcp.Net.Agent.Tools;

internal sealed class GlobSearch
{
    private static readonly StringComparer ResultPathComparer = StringComparer.Ordinal;
    private static readonly bool IgnoreCase = OperatingSystem.IsWindows();

    private readonly EnumerationOptions _enumerationOptions;
    private readonly FileSystemToolPolicy _policy;

    public GlobSearch(FileSystemToolPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = policy.IgnoreInaccessible,
            ReturnSpecialDirectories = false,
            AttributesToSkip = policy.FollowReparsePoints
                ? FileAttributes.None
                : FileAttributes.ReparsePoint,
        };
    }

    public GlobSearchResult Search(
        FileSystemToolPath basePath,
        GlobPattern pattern,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(pattern);

        var searchRoot = ResolveSearchRoot(basePath, pattern);
        if (!Directory.Exists(searchRoot.FullPath))
        {
            return new GlobSearchResult(
                searchRoot.DisplayPath,
                Array.Empty<string>(),
                false,
                0
            );
        }

        var effectiveMaxDepth = pattern.IsRecursive
            ? _policy.MaxGlobDepth
            : Math.Min(pattern.MaxDepth, _policy.MaxGlobDepth);
        var matchBudget = checked(limit + 1);
        var matches = new List<string>(Math.Min(matchBudget, 16));
        var directoriesVisited = 0;

        Traverse(
            searchRoot.FullPath,
            searchRoot.DisplayPath,
            pattern,
            segmentIndex: 0,
            depth: 0,
            effectiveMaxDepth,
            matches,
            matchBudget,
            ref directoriesVisited,
            cancellationToken
        );

        var truncated = matches.Count > limit;
        if (truncated)
        {
            matches.RemoveAt(matches.Count - 1);
        }

        return new GlobSearchResult(
            searchRoot.DisplayPath,
            matches.ToArray(),
            truncated,
            directoriesVisited
        );
    }

    private void Traverse(
        string currentFullPath,
        string currentDisplayPath,
        GlobPattern pattern,
        int segmentIndex,
        int depth,
        int effectiveMaxDepth,
        List<string> matches,
        int matchBudget,
        ref int directoriesVisited,
        CancellationToken cancellationToken
    )
    {
        if (matches.Count >= matchBudget || segmentIndex >= pattern.Segments.Length)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        directoriesVisited++;

        var currentSegment = pattern.Segments[segmentIndex];
        var isFinalSegment = segmentIndex == pattern.Segments.Length - 1;
        var nextSegment = !isFinalSegment ? pattern.Segments[segmentIndex + 1] : default;
        var nextIsFinalSegment = !isFinalSegment && segmentIndex + 1 == pattern.Segments.Length - 1;

        var files = new List<string>();
        var directories = new List<string>();
        var enumeration = new FileSystemEnumerable<GlobEntryCandidate>(
            currentFullPath,
            static (ref FileSystemEntry entry) => new(
                entry.FileName.ToString(),
                entry.IsDirectory
            ),
            _enumerationOptions
        )
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                ShouldIncludeCandidate(
                    ref entry,
                    currentSegment,
                    isFinalSegment,
                    nextSegment,
                    nextIsFinalSegment
                ),
        };

        foreach (var entry in enumeration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
            {
                directories.Add(entry.Name);
            }
            else
            {
                files.Add(entry.Name);
            }
        }

        files.Sort(ResultPathComparer);
        directories.Sort(ResultPathComparer);

        foreach (var fileName in files)
        {
            matches.Add(CombineDisplayPath(currentDisplayPath, fileName));
            if (matches.Count >= matchBudget)
            {
                return;
            }
        }

        if (depth >= effectiveMaxDepth)
        {
            return;
        }

        if (currentSegment.Kind == GlobPatternSegmentKind.DoubleStar)
        {
            TraverseDoubleStarDirectories(
                currentFullPath,
                currentDisplayPath,
                pattern,
                segmentIndex,
                depth,
                effectiveMaxDepth,
                directories,
                matches,
                matchBudget,
                ref directoriesVisited,
                cancellationToken
            );
            return;
        }

        foreach (var directoryName in directories)
        {
            if (matches.Count >= matchBudget)
            {
                return;
            }

            Traverse(
                Path.Combine(currentFullPath, directoryName),
                CombineDisplayPath(currentDisplayPath, directoryName),
                pattern,
                segmentIndex + 1,
                depth + 1,
                effectiveMaxDepth,
                matches,
                matchBudget,
                ref directoriesVisited,
                cancellationToken
            );
        }
    }

    private void TraverseDoubleStarDirectories(
        string currentFullPath,
        string currentDisplayPath,
        GlobPattern pattern,
        int segmentIndex,
        int depth,
        int effectiveMaxDepth,
        IReadOnlyList<string> directories,
        List<string> matches,
        int matchBudget,
        ref int directoriesVisited,
        CancellationToken cancellationToken
    )
    {
        var nextSegmentIndex = segmentIndex + 1;
        if (nextSegmentIndex >= pattern.Segments.Length)
        {
            return;
        }

        var nextSegment = pattern.Segments[nextSegmentIndex];
        var nextIsFinal = nextSegmentIndex == pattern.Segments.Length - 1;

        foreach (var directoryName in directories)
        {
            if (matches.Count >= matchBudget)
            {
                return;
            }

            var nextFullPath = Path.Combine(currentFullPath, directoryName);
            var nextDisplayPath = CombineDisplayPath(currentDisplayPath, directoryName);

            if (!nextIsFinal && nextSegment.IsMatch(directoryName, IgnoreCase))
            {
                Traverse(
                    nextFullPath,
                    nextDisplayPath,
                    pattern,
                    nextSegmentIndex + 1,
                    depth + 1,
                    effectiveMaxDepth,
                    matches,
                    matchBudget,
                    ref directoriesVisited,
                    cancellationToken
                );

                if (matches.Count >= matchBudget)
                {
                    return;
                }
            }

            if (_policy.ShouldSkipDirectory(directoryName.AsSpan()))
            {
                continue;
            }

            Traverse(
                nextFullPath,
                nextDisplayPath,
                pattern,
                segmentIndex,
                depth + 1,
                effectiveMaxDepth,
                matches,
                matchBudget,
                ref directoriesVisited,
                cancellationToken
            );
        }
    }

    private static bool ShouldIncludeCandidate(
        ref FileSystemEntry entry,
        GlobPatternSegment currentSegment,
        bool isFinalSegment,
        GlobPatternSegment nextSegment,
        bool nextIsFinalSegment
    )
    {
        if (currentSegment.Kind == GlobPatternSegmentKind.DoubleStar)
        {
            return entry.IsDirectory
                || (nextIsFinalSegment && nextSegment.IsMatch(entry.FileName, IgnoreCase));
        }

        if (isFinalSegment)
        {
            return !entry.IsDirectory && currentSegment.IsMatch(entry.FileName, IgnoreCase);
        }

        return entry.IsDirectory && currentSegment.IsMatch(entry.FileName, IgnoreCase);
    }

    private FileSystemToolPath ResolveSearchRoot(FileSystemToolPath basePath, GlobPattern pattern)
    {
        if (pattern.SearchRootRelativePath == ".")
        {
            return basePath;
        }

        var searchRootFullPath = Path.Combine(
            basePath.FullPath,
            pattern.SearchRootRelativePath.Replace('/', Path.DirectorySeparatorChar)
        );

        return _policy.Resolve(searchRootFullPath);
    }

    private static string CombineDisplayPath(string parent, string childName)
    {
        return parent == "."
            ? childName
            : string.Concat(parent, "/", childName);
    }

    private readonly record struct GlobEntryCandidate(string Name, bool IsDirectory);
}

internal sealed record GlobSearchResult(
    string SearchRootDisplayPath,
    string[] Paths,
    bool Truncated,
    int DirectoriesVisited
);
