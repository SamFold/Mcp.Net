using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Chat;
using Mcp.Net.WebUi.DTOs;

namespace Mcp.Net.Tests.WebUi.Chat;

public class ChatTranscriptEntryMapperTests
{
    [Fact]
    public void ToDto_UserEntryWithImageContent_ShouldIncludeTypedContentParts()
    {
        var entry = new UserChatEntry(
            "user-1",
            DateTimeOffset.UtcNow,
            [
                new TextUserContentPart("Describe this image."),
                new InlineImageUserContentPart(BinaryData.FromBytes([1, 2, 3, 4]), "image/png"),
            ],
            "turn-1"
        );

        var dto = ChatTranscriptEntryMapper.ToDto("session-1", entry);

        var userDto = dto.Should().BeOfType<UserChatTranscriptEntryDto>().Subject;
        userDto.Content.Should().Be("Describe this image.");
        userDto.ContentParts.Should().HaveCount(2);

        userDto.ContentParts[0]
            .Should()
            .BeOfType<TextUserMessageContentPartDto>()
            .Which.Text.Should()
            .Be("Describe this image.");

        var imagePart = userDto.ContentParts[1]
            .Should()
            .BeOfType<InlineImageUserMessageContentPartDto>()
            .Subject;
        imagePart.MediaType.Should().Be("image/png");
        imagePart.Data.Should().Equal(1, 2, 3, 4);
    }
}
