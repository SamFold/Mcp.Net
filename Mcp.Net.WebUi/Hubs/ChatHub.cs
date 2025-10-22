using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.Chat.Extensions;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Infrastructure.Services;
using Mcp.Net.WebUi.LLM.Services;
using Microsoft.AspNetCore.SignalR;

namespace Mcp.Net.WebUi.Hubs;

/// <summary>
/// SignalR hub for real-time chat communication
/// </summary>
public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly IChatRepository _chatRepository;
    private readonly IChatFactory _chatFactory;
    private readonly IChatAdapterManager _adapterManager;
    private readonly ITitleGenerationService _titleGenerationService;

    public ChatHub(
        ILogger<ChatHub> logger,
        IChatRepository chatRepository,
        IChatFactory chatFactory,
        IChatAdapterManager adapterManager,
        ITitleGenerationService titleGenerationService
    )
    {
        _logger = logger;
        _chatRepository = chatRepository;
        _chatFactory = chatFactory;
        _adapterManager = adapterManager;
        _titleGenerationService = titleGenerationService;
    }

    /// <summary>
    /// Join a specific chat session
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation(
            "Client {ConnectionId} joined session {SessionId}",
            Context.ConnectionId,
            sessionId
        );
    }

    /// <summary>
    /// Leave a specific chat session
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation(
            "Client {ConnectionId} left session {SessionId}",
            Context.ConnectionId,
            sessionId
        );
    }

    /// <summary>
    /// Send a message to a specific chat session
    /// </summary>
    public async Task SendMessage(string sessionId, string message)
    {
        _logger.LogInformation("Received message for session {SessionId}", sessionId);

        try
        {
            // Get or create adapter for this session
            var adapter = await GetOrCreateAdapterAsync(sessionId);

            // Log the current system prompt to debug the issue
            if (adapter?.GetLlmClient() is var client && client != null)
            {
                _logger.LogDebug(
                    "Current system prompt when sending message: {SystemPrompt}",
                    client.GetSystemPrompt()
                );
            }

            // Mark the adapter as active
            _adapterManager.MarkAdapterAsActive(sessionId);

            // Create a message to store in history
            var chatMessage = new StoredChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                Type = "user",
                Content = message,
                Timestamp = DateTime.UtcNow,
            };

            // Store the message
            await _chatRepository.StoreMessageAsync(chatMessage);

            // Check if this is the first message in the session
            bool isFirstMessage = await _chatRepository.IsFirstMessageAsync(sessionId);
            if (isFirstMessage)
            {
                try
                {
                    // Generate title from the first message
                    var generatedTitle = await _titleGenerationService.GenerateTitleAsync(message);

                    // Update the session metadata
                    var metadata = await _chatRepository.GetChatMetadataAsync(sessionId);
                    if (metadata != null)
                    {
                        metadata.Title = generatedTitle;
                        await _chatRepository.UpdateChatMetadataAsync(metadata);

                        // Notify clients of the title change
                        if (adapter != null)
                            await adapter.NotifyMetadataUpdated(metadata);

                        _logger.LogInformation(
                            "Generated title '{Title}' for session {SessionId}",
                            generatedTitle,
                            sessionId
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error generating title for session {SessionId}",
                        sessionId
                    );
                    // Continue with default title if generation fails
                }
            }

            // Process the message
            if (adapter != null)
                adapter.ProcessUserInput(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync(
                "ReceiveError",
                $"Error processing message: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Create a new chat session
    /// </summary>
    public async Task<string> CreateSession()
    {
        try
        {
            // Generate a session ID
            var sessionId = Guid.NewGuid().ToString();

            // Create and store metadata
            var metadata = _chatFactory.CreateSessionMetadata(sessionId);
            await _chatRepository.CreateChatAsync(metadata);

            // Add the client to the session group
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            _logger.LogInformation(
                "Created new session {SessionId} for client {ConnectionId}",
                sessionId,
                Context.ConnectionId
            );

            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session");

            // Report the error to the client
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);

            // Return a session ID anyway so the client has something to reference
            // but it won't actually work for chat
            return Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// Get the current system prompt for a session
    /// </summary>
    public async Task<string> GetSystemPrompt(string sessionId)
    {
        try
        {
            _logger.LogInformation("Getting system prompt for session {SessionId}", sessionId);
            return await _chatRepository.GetSystemPromptAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system prompt for session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Set the system prompt for a session
    /// </summary>
    public async Task SetSystemPrompt(string sessionId, string systemPrompt)
    {
        try
        {
            _logger.LogInformation("Setting system prompt for session {SessionId}", sessionId);
            await _chatRepository.SetSystemPromptAsync(sessionId, systemPrompt);

            // Try to get existing adapter first (don't create a new one)
            var adapter = await GetExistingAdapterAsync(sessionId);
            if (adapter != null)
            {
                if (adapter.GetLlmClient() is var client && client != null)
                {
                    _logger.LogInformation(
                        "Updating system prompt in existing adapter for session {SessionId}",
                        sessionId
                    );
                    client.SetSystemPrompt(systemPrompt);
                    // Mark as active since we just used it
                    _adapterManager.MarkAdapterAsActive(sessionId);
                }
            }
            else
            {
                _logger.LogDebug(
                    "No active adapter found for session {SessionId}, system prompt will be applied when adapter is created",
                    sessionId
                );
            }

            // Notify clients
            var message = new ChatMessageDto
            {
                SessionId = sessionId,
                Type = "system",
                Content = "System prompt has been updated.",
                Id = $"system_{Guid.NewGuid()}",
            };

            await Clients.Group(sessionId).SendAsync("ReceiveMessage", message);
            await Clients.Group(sessionId).SendAsync("SystemPromptUpdated", systemPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting system prompt for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync(
                "ReceiveError",
                $"Error setting system prompt: {ex.Message}"
            );
            throw;
        }
    }

    /// <summary>
    /// Override of OnDisconnectedAsync to handle client disconnection
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Helper method to get an existing adapter without creating a new one
    /// </summary>
    private async Task<ISignalRChatAdapter?> GetExistingAdapterAsync(string sessionId)
    {
        // Check if adapter exists in the manager
        if (!_adapterManager.GetActiveSessions().Contains(sessionId))
        {
            return null;
        }

        // Get the adapter without creating a new one
        return await _adapterManager.GetOrCreateAdapterAsync(
            sessionId,
            (sid) => Task.FromResult<ISignalRChatAdapter>(null!)
        );
    }

    /// <summary>
    /// Helper method to get or create an adapter for a session
    /// </summary>
    private async Task<ISignalRChatAdapter> GetOrCreateAdapterAsync(string sessionId)
    {
        _logger.LogDebug(
            "Currently Active Sessions: {Sessions}",
            string.Join(", ", _adapterManager.GetActiveSessions())
        );

        return await _adapterManager.GetOrCreateAdapterAsync(
            sessionId,
            async (sid) =>
            {
                // Get the session metadata
                var metadata = await _chatRepository.GetChatMetadataAsync(sid);
                if (metadata == null)
                {
                    throw new KeyNotFoundException($"Session {sid} not found");
                }

                // Create a new adapter
                var newAdapter = await _chatFactory.CreateSignalRAdapterAsync(
                    sid,
                    metadata.Model,
                    metadata.Provider.ToString(),
                    metadata.SystemPrompt
                );

                _logger.LogDebug("New SignalR Adapter was created for sessionId {SessionId}", sid);

                // Subscribe to message events
                newAdapter.MessageReceived += OnChatMessageReceived;

                // Start the adapter
                newAdapter.Start();

                return newAdapter;
            }
        );
    }

    /// <summary>
    /// Handler for chat messages received from an adapter
    /// </summary>
    private async void OnChatMessageReceived(object? sender, ChatMessageEventArgs args)
    {
        try
        {
            // Store the message
            var message = new StoredChatMessage
            {
                Id = args.MessageId,
                SessionId = args.ChatId,
                Type = args.Type,
                Content = args.Content,
                Timestamp = DateTime.UtcNow,
            };

            await _chatRepository.StoreMessageAsync(message);

            // Don't try to notify clients directly from the ChatHub event handler
            // Just update the metadata in the repository
            var metadata = await _chatRepository.GetChatMetadataAsync(args.ChatId);
            if (metadata != null)
            {
                // Update LastUpdatedAt and LastMessagePreview
                metadata.LastUpdatedAt = DateTime.UtcNow;
                metadata.LastMessagePreview =
                    args.Content.Length > 50 ? args.Content.Substring(0, 47) + "..." : args.Content;

                // Update in repository
                await _chatRepository.UpdateChatMetadataAsync(metadata);

                // The notification to clients will happen in the InMemoryChatHistoryManager
                // when UpdateSessionMetadataAsync is called
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling message event for session {SessionId}",
                args.ChatId
            );
        }
    }
}
