using System.Text.Json.Serialization;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.DTOs;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextUserMessageContentPartDto), "text")]
[JsonDerivedType(typeof(InlineImageUserMessageContentPartDto), "inlineImage")]
public abstract record UserMessageContentPartDto
{
    public abstract UserContentPart ToModel();

    public static UserMessageContentPartDto FromModel(UserContentPart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        return part switch
        {
            TextUserContentPart text => new TextUserMessageContentPartDto(text.Text),
            InlineImageUserContentPart image => new InlineImageUserMessageContentPartDto(
                image.MediaType,
                image.Data.ToArray()
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported user content part type '{part.GetType().Name}'."
            ),
        };
    }
}

public sealed record TextUserMessageContentPartDto(string Text) : UserMessageContentPartDto
{
    public override UserContentPart ToModel() => new TextUserContentPart(Text);
}

public sealed record InlineImageUserMessageContentPartDto(string MediaType, byte[] Data)
    : UserMessageContentPartDto
{
    public override UserContentPart ToModel()
    {
        ArgumentNullException.ThrowIfNull(Data);
        return new InlineImageUserContentPart(BinaryData.FromBytes([.. Data]), MediaType);
    }
}
