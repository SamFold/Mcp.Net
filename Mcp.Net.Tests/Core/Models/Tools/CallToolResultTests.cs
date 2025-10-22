using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;

namespace Mcp.Net.Tests.Core.Models.Tools;

public class CallToolResultTests
{
    [Fact]
    public void CallToolResult_Should_Serialize_Success_Result_Correctly()
    {
        // Arrange
        var result = new ToolCallResult
        {
            Content = new ContentBase[]
            {
                new TextContent { Text = "Operation succeeded" }
            },
            IsError = false
        };

        // Act
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<ToolCallResult>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.IsError.Should().BeFalse();
        deserialized.Content.Should().HaveCount(1);
        deserialized.Content.First().Should().BeOfType<TextContent>();
        ((TextContent)deserialized.Content.First()).Text.Should().Be("Operation succeeded");
    }

    [Fact]
    public void CallToolResult_Should_Serialize_Error_Result_Correctly()
    {
        // Arrange
        var result = new ToolCallResult
        {
            Content = new ContentBase[]
            {
                new TextContent { Text = "Error: Invalid operation" },
                new TextContent { Text = "Stack trace: ..." }
            },
            IsError = true
        };

        // Act
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<ToolCallResult>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.IsError.Should().BeTrue();
        deserialized.Content.Should().HaveCount(2);
        deserialized.Content.Should().AllBeOfType<TextContent>();
        ((TextContent)deserialized.Content.First()).Text.Should().Be("Error: Invalid operation");
        ((TextContent)deserialized.Content.Skip(1).First()).Text.Should().Be("Stack trace: ...");
    }

    [Fact]
    public void CallToolResult_Should_Handle_Empty_Content()
    {
        // Arrange
        var result = new ToolCallResult
        {
            Content = Array.Empty<ContentBase>(),
            IsError = false
        };

        // Act
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<ToolCallResult>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.IsError.Should().BeFalse();
        deserialized.Content.Should().NotBeNull();
        deserialized.Content.Should().BeEmpty();
    }

    [Fact]
    public void CallToolResult_Should_Serialize_Structured_Output_And_ResourceLinks()
    {
        var result = new ToolCallResult
        {
            IsError = false,
            StructuredContent = new { count = 3 },
            Meta = new Dictionary<string, object?> { ["origin"] = "test" }
        };

        result.Content = new ContentBase[]
        {
            new ResourceLinkContent { Uri = "mcp://resource/123", Name = "Example" }
        };

        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<ToolCallResult>(json);

        deserialized.Should().NotBeNull();
        deserialized!.StructuredContent.Should().NotBeNull();
        var structured = (JsonElement)deserialized.StructuredContent!;
        structured.GetProperty("count").GetInt32().Should().Be(3);

        deserialized.Content.Should().HaveCount(1);
        deserialized.Content.First().Should().BeOfType<ResourceLinkContent>();
        ((ResourceLinkContent)deserialized.Content.First()).Uri.Should().Be("mcp://resource/123");

        deserialized.Meta.Should().NotBeNull();
        ((JsonElement)deserialized.Meta!["origin"]!).GetString().Should().Be("test");
    }
}
