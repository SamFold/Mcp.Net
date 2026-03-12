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

    [Fact]
    public void GenerateJsonSchema_ShouldCloseObjectShapesForStrictToolSchemas()
    {
        var schema = LocalToolSchemaGenerator.GenerateJsonSchema(typeof(NestedArgs));

        schema.TryGetProperty("$schema", out _).Should().BeFalse();
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var child = schema.GetProperty("properties").GetProperty("child");
        child.GetProperty("type").GetString().Should().Be("object");
        child.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    private sealed record SampleArgs(
        string Path,
        int RetryCount,
        int? MaxLines,
        string? Note
    );

    private sealed record NestedArgs(ChildArgs Child);

    private sealed record ChildArgs(string Name);
}
