using Mcp.Net.Agent.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Chat;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace Mcp.Net.WebUi.Controllers;

/// <summary>
/// REST CRUD for chat sessions. Real-time chat goes through SignalR.
/// </summary>
[ApiController]
[Route("api/chat")]
public class SessionsController : ControllerBase
{
    private readonly SessionHost _sessionHost;
    private readonly IChatHistoryManager _historyManager;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        SessionHost sessionHost,
        IChatHistoryManager historyManager,
        ILogger<SessionsController> logger)
    {
        _sessionHost = sessionHost;
        _historyManager = historyManager;
        _logger = logger;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions()
    {
        var sessions = await _historyManager.GetAllSessionsAsync("default");
        var dtos = sessions
            .Select(s => new SessionMetadataDto
            {
                Id = s.Id,
                Title = s.Title,
                CreatedAt = s.CreatedAt,
                LastUpdatedAt = s.LastUpdatedAt,
                Model = s.Model,
                Provider = s.Provider.ToString().ToLowerInvariant(),
                SystemPrompt = s.SystemPrompt,
                LastMessagePreview = s.LastMessagePreview,
            })
            .OrderByDescending(s => s.LastUpdatedAt)
            .ToList();
        return Ok(dtos);
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] SessionCreateDto? options = null)
    {
        var sessionId = Guid.NewGuid().ToString();
        await _sessionHost.CreateAsync(
            sessionId,
            options?.Provider,
            options?.Model,
            options?.SystemPrompt,
            HttpContext.RequestAborted);
        return Ok(new { sessionId });
    }

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> EndSession(string sessionId)
    {
        await _sessionHost.RemoveAsync(sessionId);
        return Ok();
    }

    [HttpDelete("sessions/{sessionId}/delete")]
    public async Task<IActionResult> DeleteSession(string sessionId)
    {
        await _sessionHost.DeleteAsync(sessionId);
        return Ok();
    }

    [HttpGet("sessions/{sessionId}/messages")]
    public async Task<IActionResult> GetMessages(string sessionId)
    {
        var transcript = await _historyManager.GetSessionTranscriptAsync(sessionId);
        var dtos = transcript
            .Select(entry => ChatTranscriptEntryMapper.ToDto(sessionId, entry))
            .ToList();
        return Ok(dtos);
    }

    [HttpDelete("sessions/{sessionId}/messages")]
    public async Task<IActionResult> ClearMessages(string sessionId)
    {
        await _historyManager.ClearSessionTranscriptAsync(sessionId);

        var managed = _sessionHost.TryGet(sessionId);
        managed?.ChatSession.ResetConversation();

        return Ok();
    }

    [HttpGet("sessions/{sessionId}/system-prompt")]
    public async Task<IActionResult> GetSystemPrompt(string sessionId)
    {
        var metadata = await _historyManager.GetSessionMetadataAsync(sessionId);
        if (metadata == null)
            return NotFound();

        return Ok(new { prompt = metadata.SystemPrompt });
    }

    [HttpPut("sessions/{sessionId}/system-prompt")]
    public async Task<IActionResult> SetSystemPrompt(
        string sessionId,
        [FromBody] SystemPromptDto dto)
    {
        var metadata = await _historyManager.GetSessionMetadataAsync(sessionId);
        if (metadata == null)
            return NotFound();

        metadata.SystemPrompt = dto.Prompt;
        metadata.LastUpdatedAt = DateTime.UtcNow;
        await _historyManager.UpdateSessionMetadataAsync(metadata);

        // If session is live, push the change to the runtime
        var managed = _sessionHost.TryGet(sessionId);
        managed?.ChatSession.SetSystemPrompt(dto.Prompt);

        return Ok();
    }

    [HttpPut("sessions/{sessionId}")]
    public async Task<IActionResult> UpdateSession(
        string sessionId,
        [FromBody] SessionUpdateDto update)
    {
        var metadata = await _historyManager.GetSessionMetadataAsync(sessionId);
        if (metadata == null)
            return NotFound();

        if (!string.IsNullOrEmpty(update.Title))
            metadata.Title = update.Title;
        if (!string.IsNullOrEmpty(update.Model))
            metadata.Model = update.Model;
        if (!string.IsNullOrEmpty(update.Provider) &&
            Enum.TryParse<LlmProvider>(update.Provider, true, out var provider))
            metadata.Provider = provider;
        if (!string.IsNullOrEmpty(update.SystemPrompt))
            metadata.SystemPrompt = update.SystemPrompt;

        metadata.LastUpdatedAt = DateTime.UtcNow;
        await _historyManager.UpdateSessionMetadataAsync(metadata);

        return Ok();
    }
}
