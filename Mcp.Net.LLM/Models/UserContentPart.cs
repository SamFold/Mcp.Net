namespace Mcp.Net.LLM.Models;

public enum UserContentPartKind
{
    Text,
    InlineImage,
}

public abstract record UserContentPart(UserContentPartKind Kind);

public sealed record TextUserContentPart : UserContentPart
{
    public TextUserContentPart(string text)
        : base(UserContentPartKind.Text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}

public sealed record InlineImageUserContentPart : UserContentPart
{
    public InlineImageUserContentPart(BinaryData data, string mediaType)
        : base(UserContentPartKind.InlineImage)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrWhiteSpace(mediaType))
        {
            throw new ArgumentException("Image media type is required.", nameof(mediaType));
        }

        Data = data;
        MediaType = mediaType;
    }

    public BinaryData Data { get; }

    public string MediaType { get; }
}
