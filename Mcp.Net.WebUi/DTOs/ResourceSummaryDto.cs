namespace Mcp.Net.WebUi.DTOs;

public class ResourceSummaryDto
{
    public string Uri { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? MimeType { get; set; }
}
