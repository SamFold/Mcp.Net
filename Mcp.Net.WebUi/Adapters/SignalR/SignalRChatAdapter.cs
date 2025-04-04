using Mcp.Net.LLM;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Mcp.Net.WebUi.Adapters.SignalR;

/// <summary>
/// Adapter class that connects ChatSession to SignalR for web UI
/// </summary>
public class SignalRChatAdapter : ISignalRChatAdapter
{
    private readonly ChatSession _chatSession;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<SignalRChatAdapter> _logger;
    private readonly string _sessionId;

    public event EventHandler<ChatMessageEventArgs>? MessageReceived;

    public string SessionId => _sessionId;

    public SignalRChatAdapter(
        ChatSession chatSession,
        IHubContext<ChatHub> hubContext,
        ILogger<SignalRChatAdapter> logger,
        string sessionId
    )
    {
        _chatSession = chatSession;
        _hubContext = hubContext;
        _logger = logger;
        _sessionId = sessionId;

        // Wire up event handlers
        _chatSession.SessionStarted += OnSessionStarted;
        _chatSession.AssistantMessageReceived += OnAssistantMessageReceived;
        _chatSession.ToolExecutionUpdated += OnToolExecutionUpdated;
        _chatSession.ThinkingStateChanged += OnThinkingStateChanged;
    }

    /// <summary>
    /// Start the chat session
    /// </summary>
    public void Start()
    {
        _logger.LogInformation("Starting chat session {SessionId}", _sessionId);

        // Start the chat session
        _chatSession.StartSession();
    }

    /// <summary>
    /// Process a user message
    /// </summary>
    public async void ProcessUserInput(string message)
    {
        _logger.LogDebug(
            "Processing user input for session {SessionId}: {Message}",
            _sessionId,
            message
        );

        try
        {
            await _chatSession.SendUserMessageAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user message: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Reset the conversation history in the LLM client
    /// </summary>
    public void ResetConversation()
    {
        // Access the LLM client through the chat session
        if (_chatSession is ChatSession cs && GetLlmClient() is IChatClient client)
        {
            client.ResetConversation();
            _logger.LogInformation("Reset conversation for session {SessionId}", _sessionId);
        }
    }

    /// <summary>
    /// Load conversation history from stored messages
    /// </summary>
    public async Task LoadHistoryAsync(List<StoredChatMessage> messages)
    {
        _logger.LogInformation(
            "LoadHistoryAsync not implemented yet - you will need to implement this to replay the message history"
        );
        await Task.CompletedTask;
    }

    /// <summary>
    /// Get the LLM client used by this session
    /// </summary>
    public IChatClient? GetLlmClient()
    {
        // Use the accessor method we added to ChatSession
        return _chatSession.GetLlmClient();
    }

    private async void OnSessionStarted(object? sender, EventArgs e)
    {
        _logger.LogDebug("Session started: {SessionId}", _sessionId);
        await _hubContext.Clients.Group(_sessionId).SendAsync("SessionStarted", _sessionId);
    }

    private async void OnAssistantMessageReceived(object? sender, string message)
    {
        _logger.LogDebug(
            "Assistant message received in session {SessionId}: {MessagePreview}...",
            _sessionId,
            message.Substring(0, Math.Min(30, message.Length))
        );

        var messageId = $"assistant_{Guid.NewGuid()}";

        // Create a message DTO
        var messageDto = new ChatMessageDto
        {
            SessionId = _sessionId,
            Type = "assistant",
            Content = message,
            Id = messageId,
        };

        // Notify subscribers about the message (e.g. for storage)
        MessageReceived?.Invoke(
            this,
            new ChatMessageEventArgs(_sessionId, messageId, message, "assistant")
        );

        // Send message to clients via SignalR
        await _hubContext.Clients.Group(_sessionId).SendAsync("ReceiveMessage", messageDto);
    }

    private async void OnToolExecutionUpdated(object? sender, ToolExecutionEventArgs args)
    {
        _logger.LogDebug(
            "Tool execution update for session {SessionId}: Tool {ToolName}, Success: {Success}",
            _sessionId,
            args.ToolName,
            args.Success
        );
        var toolDto = ToolExecutionDto.FromEventArgs(args, _sessionId);
        await _hubContext.Clients.Group(_sessionId).SendAsync("ToolExecutionUpdated", toolDto);
    }

    private async void OnThinkingStateChanged(object? sender, ThinkingStateEventArgs args)
    {
        _logger.LogDebug(
            "Thinking state changed for session {SessionId}: {IsThinking}, Context: {Context}",
            _sessionId,
            args.IsThinking,
            args.Context
        );
        await _hubContext
            .Clients.Group(_sessionId)
            .SendAsync("ThinkingStateChanged", args.IsThinking, args.Context);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing chat session {SessionId}", _sessionId);

        // Unwire events
        _chatSession.SessionStarted -= OnSessionStarted;
        _chatSession.AssistantMessageReceived -= OnAssistantMessageReceived;
        _chatSession.ToolExecutionUpdated -= OnToolExecutionUpdated;
        _chatSession.ThinkingStateChanged -= OnThinkingStateChanged;
    }
}
