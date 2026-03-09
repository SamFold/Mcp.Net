namespace Mcp.Net.LLM.Replay;

public enum CrossProviderReasoningReplayMode
{
    Drop,
    ConvertVisibleToText,
}

public enum UnmatchedToolCallReplayMode
{
    SynthesizeErrorToolResult,
    TruncateAtLastSafeEntry,
}

public sealed record ReplayTarget
{
    public ReplayTarget(
        string provider,
        string model,
        CrossProviderReasoningReplayMode crossProviderReasoningReplayMode = CrossProviderReasoningReplayMode.Drop,
        UnmatchedToolCallReplayMode unmatchedToolCallReplayMode = UnmatchedToolCallReplayMode.SynthesizeErrorToolResult
    )
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Replay target provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Replay target model is required.", nameof(model));
        }

        Provider = provider;
        Model = model;
        CrossProviderReasoningReplayMode = crossProviderReasoningReplayMode;
        UnmatchedToolCallReplayMode = unmatchedToolCallReplayMode;
    }

    public string Provider { get; }

    public string Model { get; }

    public CrossProviderReasoningReplayMode CrossProviderReasoningReplayMode { get; }

    public UnmatchedToolCallReplayMode UnmatchedToolCallReplayMode { get; }
}
