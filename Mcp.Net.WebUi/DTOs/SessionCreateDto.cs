namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO for creating a new session
/// </summary>
public class SessionCreateDto
{
    /// <summary>
    /// Model to use for the session
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Provider to use for the session
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// System prompt to use for the session
    /// </summary>
    public string? SystemPrompt { get; set; }

}
