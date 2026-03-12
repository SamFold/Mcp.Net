using Mcp.Net.WebUi.Sessions;
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
        try
        {
            var managed = await _sessionHost.GetOrCreateAsync(sessionId, Context.ConnectionAborted);
            if (managed == null)
                throw new HubException($"Session {sessionId} not found.");

            managed.Touch();
            await managed.ChatSession.SendUserMessageAsync(message, Context.ConnectionAborted);
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
}
