namespace Mcp.Net.WebUi.Images;

public static class GeneratedImageArtifactRoutes
{
    public const string BasePath = "/api/generated-images";

    public static string GetPath(string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return $"{BasePath}/{Uri.EscapeDataString(artifactId)}";
    }
}
