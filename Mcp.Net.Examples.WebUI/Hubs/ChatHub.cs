using Microsoft.AspNetCore.SignalR;
using Mcp.Net.Examples.WebUI.Adapters.Interfaces;
using Mcp.Net.Examples.WebUI.Adapters.SignalR;
using Mcp.Net.Examples.WebUI.Chat.Interfaces;
using Mcp.Net.Examples.WebUI.DTOs;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Examples.WebUI.Hubs;

/// <summary>
/// SignalR hub for real-time chat communication
/// </summary>
public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly IChatRepository _chatRepository;
    private readonly IChatFactory _chatFactory;
    private readonly Dictionary<string, ISignalRChatAdapter> _activeAdapters = new();

    public ChatHub(
        ILogger<ChatHub> logger,
        IChatRepository chatRepository,
        IChatFactory chatFactory)
    {
        _logger = logger;
        _chatRepository = chatRepository;
        _chatFactory = chatFactory;
    }

    /// <summary>
    /// Join a specific chat session
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} joined session {SessionId}", Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Leave a specific chat session
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} left session {SessionId}", Context.ConnectionId, sessionId);
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
            
            // Process the message
            adapter.ProcessUserInput(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", $"Error processing message: {ex.Message}");
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
            _logger.LogInformation("Created new session {SessionId} for client {ConnectionId}", sessionId, Context.ConnectionId);
            
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
            
            // If we have an active adapter, update its system prompt too
            if (_activeAdapters.TryGetValue(sessionId, out var adapter) && 
                adapter.GetLlmClient() is var client && client != null)
            {
                client.SetSystemPrompt(systemPrompt);
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
            await Clients.Caller.SendAsync("ReceiveError", $"Error setting system prompt: {ex.Message}");
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
    /// Helper method to get or create an adapter for a session
    /// </summary>
    private async Task<ISignalRChatAdapter> GetOrCreateAdapterAsync(string sessionId)
    {
        // Try to get an existing adapter
        if (_activeAdapters.TryGetValue(sessionId, out var adapter))
        {
            return adapter;
        }
        
        // Get the session metadata
        var metadata = await _chatRepository.GetChatMetadataAsync(sessionId);
        if (metadata == null)
        {
            throw new KeyNotFoundException($"Session {sessionId} not found");
        }
        
        // Create a new adapter
        adapter = _chatFactory.CreateSignalRAdapter(
            sessionId,
            metadata.Model,
            metadata.Provider.ToString(),
            metadata.SystemPrompt
        );
        
        // Subscribe to message events
        adapter.MessageReceived += OnChatMessageReceived;
        
        // Store in active adapters
        _activeAdapters[sessionId] = adapter;
        
        // Start the adapter
        adapter.Start();
        
        return adapter;
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
                Timestamp = DateTime.UtcNow
            };
            
            await _chatRepository.StoreMessageAsync(message);
            
            // Don't try to notify clients directly from the ChatHub event handler
            // Just update the metadata in the repository
            var metadata = await _chatRepository.GetChatMetadataAsync(args.ChatId);
            if (metadata != null)
            {
                // Update LastUpdatedAt and LastMessagePreview
                metadata.LastUpdatedAt = DateTime.UtcNow;
                metadata.LastMessagePreview = args.Content.Length > 50 
                    ? args.Content.Substring(0, 47) + "..." 
                    : args.Content;
                
                // Update in repository
                await _chatRepository.UpdateChatMetadataAsync(metadata);
                
                // The notification to clients will happen in the InMemoryChatHistoryManager
                // when UpdateSessionMetadataAsync is called
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message event for session {SessionId}", args.ChatId);
        }
    }
}