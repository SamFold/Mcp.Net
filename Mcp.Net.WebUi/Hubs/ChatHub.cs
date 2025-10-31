using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.Chat.Extensions;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Infrastructure.Services;
using Mcp.Net.WebUi.LLM.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

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
                _ = GenerateSessionTitleAsync(sessionId, message, adapter);
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
    public async Task<IReadOnlyList<PromptSummaryDto>> GetPrompts(string sessionId)
    {
        try
        {
            var adapter = await GetOrCreateAdapterAsync(sessionId);
            var prompts = await adapter.GetPromptsAsync();
            _adapterManager.MarkAdapterAsActive(sessionId);
            return prompts.Select(DtoMappers.ToPromptSummary).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching prompts for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<ResourceSummaryDto>> GetResources(string sessionId)
    {
        try
        {
            var adapter = await GetOrCreateAdapterAsync(sessionId);
            var resources = await adapter.GetResourcesAsync();
            _adapterManager.MarkAdapterAsActive(sessionId);
            return resources.Select(DtoMappers.ToResourceSummary).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching resources for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            throw;
        }
    }

    public async Task<object[]> GetPromptDefinition(string sessionId, string promptName)
    {
        if (string.IsNullOrWhiteSpace(promptName))
        {
            throw new ArgumentException("Prompt name is required", nameof(promptName));
        }

        try
        {
            var adapter = await GetOrCreateAdapterAsync(sessionId);
            _adapterManager.MarkAdapterAsActive(sessionId);
            return await adapter.GetPromptMessagesAsync(promptName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading prompt {Prompt} for session {SessionId}", promptName, sessionId);
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            throw;
        }
    }

    public async Task<ResourceContent[]> ReadResource(string sessionId, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("Resource URI is required", nameof(uri));
        }

        try
        {
            var adapter = await GetOrCreateAdapterAsync(sessionId);
            _adapterManager.MarkAdapterAsActive(sessionId);
            return await adapter.ReadResourceAsync(uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading resource {Uri} for session {SessionId}", uri, sessionId);
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            throw;
        }
    }

    public async Task<CompletionResultDto> RequestCompletion(string sessionId, CompletionRequestDto request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            throw new ArgumentException("Scope is required", nameof(request.Scope));
        }
        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            throw new ArgumentException("Identifier is required", nameof(request.Identifier));
        }
        if (string.IsNullOrWhiteSpace(request.ArgumentName))
        {
            throw new ArgumentException("ArgumentName is required", nameof(request.ArgumentName));
        }

        try
        {
            var adapter = await GetOrCreateAdapterAsync(sessionId);
            CompletionValues completion;

            var currentValue = request.CurrentValue ?? string.Empty;
            var context = request.Context == null
                ? null
                : new Dictionary<string, string>(request.Context, StringComparer.Ordinal);

            switch (request.Scope.ToLowerInvariant())
            {
                case "prompt":
                    completion = await adapter.CompletePromptAsync(
                        request.Identifier,
                        request.ArgumentName,
                        currentValue,
                        context
                    );
                    break;
                case "resource":
                    completion = await adapter.CompleteResourceAsync(
                        request.Identifier,
                        request.ArgumentName,
                        currentValue,
                        context
                    );
                    break;
                default:
                    throw new ArgumentException("Scope must be either 'prompt' or 'resource'", nameof(request.Scope));
            }

            _adapterManager.MarkAdapterAsActive(sessionId);
            return DtoMappers.ToCompletionResult(completion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting completion for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            throw;
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
    /// Receives an elicitation response from the web client and forwards it to the active adapter.
    /// </summary>
    public async Task SubmitElicitationResponse(string sessionId, ElicitationResponseDto response)
    {
        try
        {
            var adapter = await GetExistingAdapterAsync(sessionId);
            if (adapter == null)
            {
                _logger.LogWarning(
                    "Received elicitation response for inactive session {SessionId}",
                    sessionId
                );
                return;
            }

            _logger.LogInformation(
                "Elicitation response {RequestId} ({Action}) received from connection {ConnectionId} for session {SessionId}",
                response.RequestId,
                response.Action,
                Context.ConnectionId,
                sessionId
            );

            var handled = await adapter.TryResolveElicitationAsync(response);
            if (!handled)
            {
                _logger.LogWarning(
                    "No pending elicitation request {RequestId} for session {SessionId}",
                    response.RequestId,
                    sessionId
                );
            }
            else
            {
                _adapterManager.MarkAdapterAsActive(sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling elicitation response for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
        }
    }

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

    private async Task GenerateSessionTitleAsync(
        string sessionId,
        string firstMessage,
        ISignalRChatAdapter? adapter
    )
    {
        try
        {
            var generatedTitle = await _titleGenerationService.GenerateTitleAsync(firstMessage);
            var metadata = await _chatRepository.GetChatMetadataAsync(sessionId);
            if (metadata == null)
            {
                return;
            }

            metadata.Title = generatedTitle;
            await _chatRepository.UpdateChatMetadataAsync(metadata);

            if (adapter != null)
            {
                await adapter.NotifyMetadataUpdated(metadata);
            }

            _logger.LogInformation(
                "Generated title '{Title}' for session {SessionId}",
                generatedTitle,
                sessionId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to generate chat title for session {SessionId}",
                sessionId
            );
        }
    }

    /// <summary>
    /// Helper method to get an existing adapter without creating a new one
    /// </summary>
    private Task<ISignalRChatAdapter?> GetExistingAdapterAsync(string sessionId)
    {
        if (_adapterManager.TryGetAdapter(sessionId, out var adapter))
        {
            return Task.FromResult<ISignalRChatAdapter?>(adapter);
        }

        return Task.FromResult<ISignalRChatAdapter?>(null);
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
