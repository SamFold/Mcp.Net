using FluentAssertions;
using Mcp.Net.WebUi.Images;

namespace Mcp.Net.Tests.WebUi.Images;

public class GeneratedImageArtifactStoreTests
{
    [Fact]
    public void Store_ShouldMakeArtifactAvailableById()
    {
        var store = new GeneratedImageArtifactStore(capacity: 4);

        var artifact = store.Store(BinaryData.FromBytes([1, 2, 3]), "image/png");

        store.TryGet(artifact.Id, out var storedArtifact).Should().BeTrue();
        storedArtifact.Should().NotBeNull();
        storedArtifact!.MediaType.Should().Be("image/png");
        storedArtifact.Data.ToArray().Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void Store_WhenCapacityIsExceeded_ShouldEvictOldestArtifact()
    {
        var store = new GeneratedImageArtifactStore(capacity: 1);

        var first = store.Store(BinaryData.FromBytes([1]), "image/png");
        var second = store.Store(BinaryData.FromBytes([2]), "image/png");

        store.TryGet(first.Id, out _).Should().BeFalse();
        store.TryGet(second.Id, out var storedArtifact).Should().BeTrue();
        storedArtifact!.Data.ToArray().Should().Equal([2]);
    }
}
