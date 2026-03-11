namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO for session metadata
/// </summary>
public class SessionMetadataDto
{
    /// <summary>
    /// Unique ID for the session
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Title of the session
    /// </summary>
    public string Title { get; set; } = "New Chat";

    /// <summary>
    /// When the session was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the session was last updated
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Model used for the session
    /// </summary>
    public string Model { get; set; } = "default";

    /// <summary>
    /// Provider used for the session
    /// </summary>
    public string Provider { get; set; } = "default";

    /// <summary>
    /// System prompt used for the session
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Preview of the last message
    /// </summary>
    public string? LastMessagePreview { get; set; }

}
