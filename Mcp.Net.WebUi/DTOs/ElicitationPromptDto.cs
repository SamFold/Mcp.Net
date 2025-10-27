using Mcp.Net.Core.Models.Elicitation;

namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO representing an elicitation prompt emitted to web clients.
/// </summary>
public sealed class ElicitationPromptDto
{
    public string SessionId { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public ElicitationSchema Schema { get; set; } = new();
}
