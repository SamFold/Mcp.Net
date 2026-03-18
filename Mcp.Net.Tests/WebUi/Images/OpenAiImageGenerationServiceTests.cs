using FluentAssertions;
using Mcp.Net.WebUi.Images;
using OpenAI.Images;

namespace Mcp.Net.Tests.WebUi.Images;

public class OpenAiImageGenerationServiceTests
{
    [Fact]
    public void CreateOptions_ForGptImageModels_ShouldNotSetResponseFormat()
    {
        var options = OpenAiImageGenerationService.CreateOptions(
            OpenAiImageGenerationService.NormalizeSize("square")
        );

        options.ResponseFormat.Should().BeNull();
        options.OutputFileFormat.Should().Be(GeneratedImageFileFormat.Png);
        options.Size.Should().Be(GeneratedImageSize.W1024xH1024);
    }

    [Theory]
    [InlineData(null, "square")]
    [InlineData(" square ", "square")]
    [InlineData("Landscape", "landscape")]
    [InlineData("PORTRAIT", "portrait")]
    public void NormalizeSize_ShouldNormalizeSupportedValues(
        string? input,
        string expected
    )
    {
        OpenAiImageGenerationService.NormalizeSize(input).Should().Be(expected);
    }
}
