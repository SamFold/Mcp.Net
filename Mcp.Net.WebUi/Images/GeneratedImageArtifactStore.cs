using System.Collections.Generic;

namespace Mcp.Net.WebUi.Images;

public sealed class GeneratedImageArtifactStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GeneratedImageArtifact> _artifacts =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _artifactOrder = new();
    private readonly int _capacity;

    public GeneratedImageArtifactStore(int capacity = 128)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public GeneratedImageArtifact Store(BinaryData data, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        var artifact = new GeneratedImageArtifact(
            Guid.NewGuid().ToString("n"),
            data,
            mediaType,
            DateTimeOffset.UtcNow
        );

        lock (_gate)
        {
            _artifacts[artifact.Id] = artifact;
            _artifactOrder.Enqueue(artifact.Id);
            TrimUnsafe();
        }

        return artifact;
    }

    public bool TryGet(string artifactId, out GeneratedImageArtifact? artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        lock (_gate)
        {
            return _artifacts.TryGetValue(artifactId, out artifact);
        }
    }

    private void TrimUnsafe()
    {
        while (_artifacts.Count > _capacity && _artifactOrder.Count > 0)
        {
            var artifactId = _artifactOrder.Dequeue();
            _artifacts.Remove(artifactId);
        }
    }
}
