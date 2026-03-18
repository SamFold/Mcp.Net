namespace Mcp.Net.WebUi.Images;

public sealed record GeneratedImageArtifact(
    string Id,
    BinaryData Data,
    string MediaType,
    DateTimeOffset CreatedAt
);
