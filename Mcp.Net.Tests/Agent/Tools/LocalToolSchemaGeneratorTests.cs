using FluentAssertions;
using Mcp.Net.Agent.Tools;

namespace Mcp.Net.Tests.Agent.Tools;

public class LocalToolSchemaGeneratorTests
{
    [Fact]
    public void GenerateJsonSchema_ShouldInferRequiredPropertiesFromNullability()
    {
        var schema = LocalToolSchemaGenerator.GenerateJsonSchema(typeof(SampleArgs));

        var required = schema.GetProperty("required").EnumerateArray()
            .Select(entry => entry.GetString())
            .ToArray();

        required.Should().Contain("path");
        required.Should().Contain("retryCount");
        required.Should().NotContain("maxLines");
        required.Should().NotContain("note");
    }

    private sealed record SampleArgs(
        string Path,
        int RetryCount,
        int? MaxLines,
        string? Note
    );
}
