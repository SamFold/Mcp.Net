using System.Text.Json;

namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO carrying the web client's response to an elicitation prompt.
/// </summary>
public sealed class ElicitationResponseDto
{
    public string RequestId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public JsonElement? Content { get; set; }
}
