using System.Collections.ObjectModel;

namespace Mcp.Net.LLM.Models;

public sealed record ChatUsage
{
    private static readonly IReadOnlyDictionary<string, int> EmptyAdditionalCounts =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());

    public ChatUsage(
        int inputTokens,
        int outputTokens,
        int totalTokens,
        IReadOnlyDictionary<string, int>? additionalCounts = null
    )
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
        AdditionalCounts = additionalCounts == null || additionalCounts.Count == 0
            ? EmptyAdditionalCounts
            : new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(additionalCounts));
    }

    public int InputTokens { get; }

    public int OutputTokens { get; }

    public int TotalTokens { get; }

    public IReadOnlyDictionary<string, int> AdditionalCounts { get; }

    public bool Equals(ChatUsage? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return InputTokens == other.InputTokens
            && OutputTokens == other.OutputTokens
            && TotalTokens == other.TotalTokens
            && AdditionalCounts.Count == other.AdditionalCounts.Count
            && AdditionalCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(
                other.AdditionalCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            );
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(InputTokens);
        hash.Add(OutputTokens);
        hash.Add(TotalTokens);

        foreach (var pair in AdditionalCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key);
            hash.Add(pair.Value);
        }

        return hash.ToHashCode();
    }
}
