using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.DTOs;

namespace Mcp.Net.Tests.WebUi.DTOs;

public class UserMessageContentPartDtoTests
{
    [Fact]
    public void ToModel_TextPart_ShouldCreateTextUserContentPart()
    {
        var dto = new TextUserMessageContentPartDto("Describe this image.");

        var part = dto.ToModel();

        part.Should().BeOfType<TextUserContentPart>().Which.Text.Should().Be("Describe this image.");
    }

    [Fact]
    public void ToModel_InlineImagePart_ShouldCreateInlineImageUserContentPart()
    {
        var dto = new InlineImageUserMessageContentPartDto("image/png", [1, 2, 3, 4]);

        var part = dto.ToModel();

        var imagePart = part.Should().BeOfType<InlineImageUserContentPart>().Subject;
        imagePart.MediaType.Should().Be("image/png");
        imagePart.Data.ToArray().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void FromModel_TextPart_ShouldCreateTextUserMessageContentPartDto()
    {
        var part = new TextUserContentPart("Describe this image.");

        var dto = UserMessageContentPartDto.FromModel(part);

        dto.Should()
            .BeOfType<TextUserMessageContentPartDto>()
            .Which.Text.Should()
            .Be("Describe this image.");
    }

    [Fact]
    public void FromModel_InlineImagePart_ShouldCreateInlineImageUserMessageContentPartDto()
    {
        var part = new InlineImageUserContentPart(BinaryData.FromBytes([1, 2, 3, 4]), "image/png");

        var dto = UserMessageContentPartDto.FromModel(part);

        var imageDto = dto.Should().BeOfType<InlineImageUserMessageContentPartDto>().Subject;
        imageDto.MediaType.Should().Be("image/png");
        imageDto.Data.Should().Equal(1, 2, 3, 4);
    }
}
