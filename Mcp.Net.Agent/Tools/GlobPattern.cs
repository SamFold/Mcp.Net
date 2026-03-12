using System.Buffers;
using System.IO.Enumeration;

namespace Mcp.Net.Agent.Tools;

internal sealed class GlobPattern
{
    private static readonly SearchValues<char> WildcardCharacters = SearchValues.Create("*?".AsSpan());

    private GlobPattern(
        string originalPattern,
        string searchRootRelativePath,
        GlobPatternSegment[] segments,
        bool isRecursive,
        int maxDepth
    )
    {
        OriginalPattern = originalPattern;
        SearchRootRelativePath = searchRootRelativePath;
        Segments = segments;
        IsRecursive = isRecursive;
        MaxDepth = maxDepth;
    }

    public string OriginalPattern { get; }

    public string SearchRootRelativePath { get; }

    public GlobPatternSegment[] Segments { get; }

    public bool IsRecursive { get; }

    public int MaxDepth { get; }

    public static GlobPattern Parse(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var normalizedPattern = pattern.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPattern))
        {
            throw new InvalidOperationException(
                $"Pattern '{pattern}' must be relative to the configured path."
            );
        }

        var rawSegments = normalizedPattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var compiledSegments = new List<GlobPatternSegment>(rawSegments.Length);

        foreach (var rawSegment in rawSegments)
        {
            if (rawSegment == ".")
            {
                continue;
            }

            if (rawSegment == "..")
            {
                throw new InvalidOperationException(
                    $"Pattern '{pattern}' must not contain '..' segments."
                );
            }

            if (rawSegment == "**")
            {
                if (
                    compiledSegments.Count > 0
                    && compiledSegments[^1].Kind == GlobPatternSegmentKind.DoubleStar
                )
                {
                    continue;
                }

                compiledSegments.Add(new GlobPatternSegment(GlobPatternSegmentKind.DoubleStar, rawSegment));
                continue;
            }

            if (rawSegment.Contains("**", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Pattern '{pattern}' is invalid. '**' must occupy an entire path segment."
                );
            }

            compiledSegments.Add(
                rawSegment.AsSpan().ContainsAny(WildcardCharacters)
                    ? new GlobPatternSegment(
                        GlobPatternSegmentKind.SimpleExpression,
                        rawSegment
                    )
                    : new GlobPatternSegment(GlobPatternSegmentKind.Literal, rawSegment)
            );
        }

        if (compiledSegments.Count == 0)
        {
            throw new InvalidOperationException(
                $"Pattern '{pattern}' must contain at least one path segment."
            );
        }

        if (compiledSegments[^1].Kind == GlobPatternSegmentKind.DoubleStar)
        {
            compiledSegments.Add(
                new GlobPatternSegment(GlobPatternSegmentKind.SimpleExpression, "*")
            );
        }

        var firstWildcardIndex = compiledSegments.FindIndex(segment =>
            segment.Kind != GlobPatternSegmentKind.Literal
        );
        var searchRootSegmentCount = firstWildcardIndex >= 0
            ? firstWildcardIndex
            : Math.Max(compiledSegments.Count - 1, 0);
        var searchRootRelativePath = searchRootSegmentCount == 0
            ? "."
            : string.Join(
                '/',
                compiledSegments.Take(searchRootSegmentCount).Select(segment => segment.Value)
            );
        var remainingSegments = compiledSegments.Skip(searchRootSegmentCount).ToArray();
        var isRecursive = remainingSegments.Any(segment =>
            segment.Kind == GlobPatternSegmentKind.DoubleStar
        );
        var maxDepth = isRecursive ? int.MaxValue : Math.Max(remainingSegments.Length - 1, 0);

        return new GlobPattern(
            normalizedPattern,
            searchRootRelativePath,
            remainingSegments,
            isRecursive,
            maxDepth
        );
    }
}

internal enum GlobPatternSegmentKind
{
    Literal,
    SimpleExpression,
    DoubleStar,
}

internal readonly record struct GlobPatternSegment(GlobPatternSegmentKind Kind, string Value)
{
    public bool IsMatch(ReadOnlySpan<char> candidate, bool ignoreCase)
    {
        return Kind switch
        {
            GlobPatternSegmentKind.Literal => candidate.Equals(
                Value,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
            ),
            GlobPatternSegmentKind.SimpleExpression => FileSystemName.MatchesSimpleExpression(
                Value,
                candidate,
                ignoreCase
            ),
            _ => false,
        };
    }
}
