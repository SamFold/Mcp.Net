using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.LLM.Core;
using Mcp.Net.LLM.Elicitation;
using Mcp.Net.LLM.Events;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.WebUi.Adapters.SignalR;

/// <summary>
/// Adapter class that connects ChatSession to SignalR for web UI
/// </summary>
public class SignalRChatAdapter : ISignalRChatAdapter, IElicitationPromptProvider
{
    private readonly ChatSession _chatSession;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<SignalRChatAdapter> _logger;
    private readonly string _sessionId;
    private readonly ElicitationCoordinator? _elicitationCoordinator;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ElicitationClientResponse>> _pendingElicitations = new();

    public event EventHandler<ChatMessageEventArgs>? MessageReceived;

    public string SessionId => _sessionId;

    public SignalRChatAdapter(
        ChatSession chatSession,
        IHubContext<ChatHub> hubContext,
        ILogger<SignalRChatAdapter> logger,
        string sessionId,
        ElicitationCoordinator? elicitationCoordinator = null
    )
    {
        _chatSession = chatSession;
        _hubContext = hubContext;
        _logger = logger;
        _sessionId = sessionId;
        _elicitationCoordinator = elicitationCoordinator;

        _elicitationCoordinator?.SetProvider(this);

        // Set the session ID in the chat session
        _chatSession.SessionId = sessionId;

        // Wire up event handlers
        _chatSession.SessionStarted += OnSessionStarted;
        _chatSession.AssistantMessageReceived += OnAssistantMessageReceived;
        _chatSession.ToolExecutionUpdated += OnToolExecutionUpdated;
        _chatSession.ThinkingStateChanged += OnThinkingStateChanged;
    }

    /// <summary>
    /// Sends an MCP <c>elicitation/create</c> payload to every SignalR client attached to the session
    /// and waits for the UI to accept, decline, or cancel the request. Invoked by the
    /// <see cref="ElicitationCoordinator"/> whenever the server asks the client to gather data.
    /// </summary>
    /// <param name="context">The request issued by the MCP server.</param>
    /// <param name="cancellationToken">Token that is canceled when the server withdraws the request or the connection closes.</param>
    /// <returns>The action/data that should be returned to the MCP server.</returns>
    public async Task<ElicitationClientResponse> PromptAsync(
        ElicitationRequestContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<ElicitationClientResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingElicitations.TryAdd(requestId, tcs))
        {
            throw new InvalidOperationException("Unable to track elicitation request.");
        }

        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() =>
            {
                if (_pendingElicitations.TryRemove(requestId, out var pending))
                {
                    pending.TrySetCanceled(cancellationToken);
                    _ = NotifyElicitationCancelledAsync(requestId);
                }
            });
        }

        var promptDto = new ElicitationPromptDto
        {
            SessionId = _sessionId,
            RequestId = requestId,
            Message = context.Message,
            Schema = context.RequestedSchema,
        };

        await _hubContext.Clients.Group(_sessionId).SendAsync("ElicitationRequested", promptDto, cancellationToken);

        try
        {
            return await tcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            registration.Dispose();
            _pendingElicitations.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// Starts the backing <see cref="ChatSession"/> and notifies connected clients that the session is live.
    /// Used when a new SignalR adapter is materialised for a web chat session.
    /// </summary>
    public void Start()
    {
        _logger.LogInformation("Starting chat session {SessionId}", _sessionId);

        // Start the chat session
        _chatSession.StartSession();
    }

    /// <summary>
    /// Forwards user text supplied by the web client to the <see cref="ChatSession"/> for processing.
    /// Invoked by <see cref="ChatHub"/> when a browser submits a new chat message.
    /// </summary>
    /// <param name="message">The raw user text received from the UI.</param>
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

        // Use the session ID from the event args (if available) or fall back to the adapter's session ID
        string sessionId = args.SessionId ?? _sessionId;

        // Extract thinking context
        string thinkingContext = args.Context;

        // Simple, direct implementation - now that we've fixed the server-side event generation
        // there shouldn't be any duplicates to filter

        // Send the thinking state change to all clients in the session group
        await _hubContext
            .Clients.Group(_sessionId)
            .SendAsync("ThinkingStateChanged", sessionId, args.IsThinking, thinkingContext);
    }

    /// <summary>
    /// Attempts to resolve an outstanding elicitation request using input supplied by the web client.
    /// Called from <see cref="ChatHub"/> when the browser posts an elicitation response.
    /// </summary>
    /// <param name="response">The action/content selected in the browser.</param>
    /// <returns><c>true</c> when the response matched a tracked request; otherwise <c>false</c>.</returns>
    public Task<bool> TryResolveElicitationAsync(ElicitationResponseDto response)
    {
        if (!_pendingElicitations.TryRemove(response.RequestId, out var pending))
        {
            _logger.LogWarning(
                "No pending elicitation request {RequestId} for session {SessionId}",
                response.RequestId,
                _sessionId
            );
            return Task.FromResult(false);
        }

        var action = response.Action?.ToLowerInvariant();
        switch (action)
        {
            case "accept":
                var payload = response.Content ?? JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
                pending.TrySetResult(ElicitationClientResponse.Accept(payload));
                return Task.FromResult(true);
            case "decline":
                pending.TrySetResult(ElicitationClientResponse.Decline());
                return Task.FromResult(true);
            case "cancel":
                pending.TrySetResult(ElicitationClientResponse.Cancel());
                return Task.FromResult(true);
            default:
                _logger.LogWarning(
                    "Unknown elicitation action '{Action}' for request {RequestId}; declining.",
                    response.Action,
                    response.RequestId
                );
                pending.TrySetResult(ElicitationClientResponse.Decline());
                return Task.FromResult(true);
        }
    }

    public async Task NotifyMetadataUpdated(ChatSessionMetadata metadata)
    {
        _logger.LogDebug("Notifying metadata update for session {SessionId}", _sessionId);

        // Convert to a DTO with only the properties needed by the client
        var metadataDto = new SessionMetadataDto
        {
            Id = metadata.Id,
            Title = metadata.Title,
            CreatedAt = metadata.CreatedAt,
            LastUpdatedAt = metadata.LastUpdatedAt,
            Model = metadata.Model,
            Provider = metadata.Provider.ToString().ToLower(),
            LastMessagePreview = metadata.LastMessagePreview,
        };

        await _hubContext
            .Clients.Group(_sessionId)
            .SendAsync("SessionMetadataUpdated", metadataDto);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing chat session {SessionId}", _sessionId);

        // Unwire events
        _chatSession.SessionStarted -= OnSessionStarted;
        _chatSession.AssistantMessageReceived -= OnAssistantMessageReceived;
        _chatSession.ToolExecutionUpdated -= OnToolExecutionUpdated;
        _chatSession.ThinkingStateChanged -= OnThinkingStateChanged;

        _elicitationCoordinator?.SetProvider(null);

        foreach (var key in _pendingElicitations.Keys)
        {
            if (_pendingElicitations.TryRemove(key, out var pending))
            {
                pending.TrySetCanceled();
            }
        }
    }

    private Task NotifyElicitationCancelledAsync(string requestId)
    {
        _logger.LogDebug(
            "Notifying clients that elicitation request {RequestId} was cancelled for session {SessionId}.",
            requestId,
            _sessionId
        );

        return _hubContext
            .Clients.Group(_sessionId)
            .SendAsync(
                "ElicitationCancelled",
                new
                {
                    SessionId = _sessionId,
                    RequestId = requestId,
                }
            );
    }
}
