using Mcp.Net.WebUi.Sessions;
using Mcp.Net.WebUi.DTOs;
using Microsoft.AspNetCore.SignalR;

namespace Mcp.Net.WebUi.Hubs;

/// <summary>
/// Thin SignalR hub — routes commands to ChatSession, events flow back via SessionHost wiring.
/// </summary>
public class ChatHub : Hub
{
    private readonly SessionHost _sessionHost;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(SessionHost sessionHost, ILogger<ChatHub> logger)
    {
        _sessionHost = sessionHost;
        _logger = logger;
    }

    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogDebug("Client {ConnectionId} joined session {SessionId}", Context.ConnectionId, sessionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogDebug("Client {ConnectionId} left session {SessionId}", Context.ConnectionId, sessionId);
    }

    public async Task SendMessage(string sessionId, string message)
    {
        await SendMessageCoreAsync(
            sessionId,
            managed => managed.ChatSession.SendUserMessageAsync(message, Context.ConnectionAborted)
        );
    }

    public async Task SendMessageParts(string sessionId, IReadOnlyList<UserMessageContentPartDto> contentParts)
    {
        ArgumentNullException.ThrowIfNull(contentParts);

        var parts = contentParts.Select(static part => part.ToModel()).ToArray();

        await SendMessageCoreAsync(
            sessionId,
            managed => managed.ChatSession.SendUserMessageAsync(parts, Context.ConnectionAborted)
        );
    }

    public void AbortTurn(string sessionId)
    {
        var managed = _sessionHost.TryGet(sessionId);
        managed?.ChatSession.AbortCurrentTurn();
    }

    public async Task<string> CreateSession()
    {
        var sessionId = Guid.NewGuid().ToString();
        await _sessionHost.CreateAsync(sessionId, cancellationToken: Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        return sessionId;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Client {ConnectionId} disconnected", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private async Task SendMessageCoreAsync(
        string sessionId,
        Func<ManagedSession, Task> sendMessage
    )
    {
        try
        {
            var managed = await GetRequiredSessionAsync(sessionId);
            managed.Touch();
            await sendMessage(managed);
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", $"Error processing message: {ex.Message}");
        }
    }

    private async Task<ManagedSession> GetRequiredSessionAsync(string sessionId)
    {
        var managed = await _sessionHost.GetOrCreateAsync(sessionId, Context.ConnectionAborted);

        return managed ?? throw new HubException($"Session {sessionId} not found.");
    }
}
