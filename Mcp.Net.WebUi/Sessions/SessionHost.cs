using System.Collections.Concurrent;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Client;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Authentication;
using Mcp.Net.WebUi.Chat;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.LLM;
using Mcp.Net.WebUi.LLM.Factories;
using Microsoft.AspNetCore.SignalR;

namespace Mcp.Net.WebUi.Sessions;

/// <summary>
/// Manages session lifecycle: create, get, remove, cleanup, and event wiring to SignalR.
/// Replaces ChatFactory, ChatAdapterManager, and SignalRChatAdapter.
/// </summary>
public sealed class SessionHost : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, ManagedSession> _sessions = new();
    private readonly IChatSessionFactory _sessionFactory;
    private readonly ILlmClientProvider _llmClientProvider;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ToolRegistry _toolRegistry;
    private readonly IChatHistoryManager _historyManager;
    private readonly DefaultLlmSettings _defaultSettings;
    private readonly IConfiguration _configuration;
    private readonly IMcpClientBuilderConfigurator _authConfigurator;
    private readonly ILogger<SessionHost> _logger;
    private readonly TimeSpan _inactivityThreshold = TimeSpan.FromMinutes(30);
    private Timer? _cleanupTimer;

    public SessionHost(
        IChatSessionFactory sessionFactory,
        ILlmClientProvider llmClientProvider,
        IHubContext<ChatHub> hubContext,
        ToolRegistry toolRegistry,
        IChatHistoryManager historyManager,
        DefaultLlmSettings defaultSettings,
        IConfiguration configuration,
        IMcpClientBuilderConfigurator authConfigurator,
        ILogger<SessionHost> logger)
    {
        _sessionFactory = sessionFactory;
        _llmClientProvider = llmClientProvider;
        _hubContext = hubContext;
        _toolRegistry = toolRegistry;
        _historyManager = historyManager;
        _defaultSettings = defaultSettings;
        _configuration = configuration;
        _authConfigurator = authConfigurator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new managed session with a dedicated LLM client and optional MCP client.
    /// </summary>
    public async Task<ManagedSession> CreateAsync(
        string sessionId,
        string? provider = null,
        string? model = null,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var providerEnum = ParseProvider(provider);
        var modelName = model ?? _defaultSettings.ModelName;
        var effectiveSystemPrompt = systemPrompt ?? _defaultSettings.DefaultSystemPrompt;

        // Create per-session LLM client
        IChatClient chatClient = _llmClientProvider.Create(providerEnum, modelName);

        // Create per-session MCP client (if configured)
        IMcpClient? mcpClient = await CreateMcpClientAsync(cancellationToken);

        // Create ChatSession via factory
        var options = new ChatSessionFactoryOptions
        {
            SystemPrompt = effectiveSystemPrompt,
            McpClient = mcpClient,
        };

        var chatSession = await _sessionFactory.CreateAsync(chatClient, options, cancellationToken);
        chatSession.SessionId = sessionId;

        // Register tools from the shared registry
        chatSession.RegisterTools(_toolRegistry.EnabledTools);

        // Create metadata
        var metadata = new ChatSessionMetadata
        {
            Id = sessionId,
            Title = $"New Chat {DateTime.UtcNow:g}",
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            Model = modelName,
            Provider = providerEnum,
            SystemPrompt = effectiveSystemPrompt,
        };

        // Persist metadata
        await _historyManager.CreateSessionAsync("default", metadata);

        // Assemble managed session
        var managed = new ManagedSession(sessionId, chatSession, metadata, mcpClient);

        // Wire ChatSession events → SignalR broadcasts
        WireEvents(managed);

        // Load any existing transcript
        var transcript = await _historyManager.GetSessionTranscriptAsync(sessionId);
        if (transcript.Count > 0)
            await chatSession.LoadTranscriptAsync(transcript);

        if (!_sessions.TryAdd(sessionId, managed))
        {
            await managed.DisposeAsync();
            throw new InvalidOperationException($"Session {sessionId} already exists.");
        }

        _logger.LogInformation("Created session {SessionId} with {Provider}/{Model}", sessionId, providerEnum, modelName);
        return managed;
    }

    /// <summary>
    /// Gets an existing session, or materializes one from persisted metadata.
    /// </summary>
    public async Task<ManagedSession?> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            existing.Touch();
            return existing;
        }

        // Try to materialize from persisted metadata
        var metadata = await _historyManager.GetSessionMetadataAsync(sessionId);
        if (metadata == null)
            return null;

        return await CreateAsync(
            sessionId,
            metadata.Provider.ToString(),
            metadata.Model,
            metadata.SystemPrompt,
            cancellationToken);
    }

    public ManagedSession? TryGet(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        session?.Touch();
        return session;
    }

    public async Task RemoveAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Removing session {SessionId}", sessionId);
            await session.DisposeAsync();
        }
    }

    public async Task DeleteAsync(string sessionId)
    {
        await RemoveAsync(sessionId);
        await _historyManager.DeleteSessionAsync(sessionId);
        _logger.LogInformation("Deleted session {SessionId} and its history", sessionId);
    }

    public IReadOnlyList<SessionMetadataDto> ListActive() =>
        _sessions.Values
            .Select(s => ToDto(s.Metadata))
            .OrderByDescending(s => s.LastUpdatedAt)
            .ToList();

    // --- IHostedService ---

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer = new Timer(
            CleanupInactiveSessions,
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _cleanupTimer?.Dispose();

    // --- Event Wiring ---

    private void WireEvents(ManagedSession managed)
    {
        var sessionId = managed.Id;
        var chatSession = managed.ChatSession;

        // Transcript changes → ReceiveMessage / UpdateMessage
        EventHandler<ChatTranscriptChangedEventArgs> transcriptHandler = async (_, args) =>
        {
            try
            {
                var dto = ChatTranscriptEntryMapper.ToDto(sessionId, args.Entry);
                var method = args.ChangeKind == ChatTranscriptChangeKind.Updated
                    ? "UpdateMessage"
                    : "ReceiveMessage";

                await _hubContext.Clients.Group(sessionId).SendAsync(method, dto);

                // Persist to history
                if (args.ChangeKind == ChatTranscriptChangeKind.Updated)
                    await _historyManager.UpsertTranscriptEntryAsync(sessionId, args.Entry);
                else
                    await _historyManager.AddTranscriptEntryAsync(sessionId, args.Entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting transcript change for session {SessionId}", sessionId);
            }
        };
        chatSession.TranscriptChanged += transcriptHandler;

        // Tool execution → ToolExecutionUpdated
        EventHandler<ToolCallActivityChangedEventArgs> toolHandler = async (_, args) =>
        {
            try
            {
                var dto = ToolExecutionDto.FromEventArgs(args, sessionId);
                await _hubContext.Clients.Group(sessionId).SendAsync("ToolExecutionUpdated", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting tool execution for session {SessionId}", sessionId);
            }
        };
        chatSession.ToolCallActivityChanged += toolHandler;

        // Activity changes → ThinkingStateChanged
        EventHandler<ChatSessionActivityChangedEventArgs> activityHandler = async (_, args) =>
        {
            try
            {
                bool isThinking = args.Activity != ChatSessionActivity.Idle;
                string context = args.Activity switch
                {
                    ChatSessionActivity.WaitingForProvider => "waiting-for-provider",
                    ChatSessionActivity.ExecutingTool => "executing-tool",
                    _ => "idle",
                };
                await _hubContext.Clients.Group(sessionId)
                    .SendAsync("ThinkingStateChanged", sessionId, isThinking, context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting activity for session {SessionId}", sessionId);
            }
        };
        chatSession.ActivityChanged += activityHandler;

        // Track subscriptions for cleanup via disposable wrappers
        managed.TrackSubscription(new EventUnsubscriber<ChatTranscriptChangedEventArgs>(
            h => chatSession.TranscriptChanged -= h, transcriptHandler));
        managed.TrackSubscription(new EventUnsubscriber<ToolCallActivityChangedEventArgs>(
            h => chatSession.ToolCallActivityChanged -= h, toolHandler));
        managed.TrackSubscription(new EventUnsubscriber<ChatSessionActivityChangedEventArgs>(
            h => chatSession.ActivityChanged -= h, activityHandler));

        // Tool registry updates → push to session + clients
        EventHandler<IReadOnlyList<Core.Models.Tools.Tool>> toolsUpdatedHandler = async (_, tools) =>
        {
            try
            {
                chatSession.RegisterTools(_toolRegistry.EnabledTools);
                var payload = tools.Select(t => new { t.Name, t.Title, t.Description }).ToArray();
                await _hubContext.Clients.Group(sessionId).SendAsync("ToolsUpdated", payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling tool update for session {SessionId}", sessionId);
            }
        };
        _toolRegistry.ToolsUpdated += toolsUpdatedHandler;
        managed.TrackSubscription(new EventUnsubscriber<IReadOnlyList<Core.Models.Tools.Tool>>(
            h => _toolRegistry.ToolsUpdated -= h, toolsUpdatedHandler));
    }

    // --- Helpers ---

    private async Task<IMcpClient?> CreateMcpClientAsync(CancellationToken cancellationToken)
    {
        var mcpServerUrl = _configuration["McpServer:Url"];
        if (string.IsNullOrEmpty(mcpServerUrl))
            return null;

        try
        {
            var builder = new McpClientBuilder()
                .UseSseTransport(mcpServerUrl);
            await _authConfigurator.ConfigureAsync(builder, cancellationToken).ConfigureAwait(false);
            return await builder.BuildAndInitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MCP client for session");
            throw;
        }
    }

    private LlmProvider ParseProvider(string? provider)
    {
        if (string.IsNullOrEmpty(provider))
            return _defaultSettings.Provider;

        return provider.Equals("openai", StringComparison.OrdinalIgnoreCase)
            ? LlmProvider.OpenAI
            : LlmProvider.Anthropic;
    }

    private static SessionMetadataDto ToDto(ChatSessionMetadata m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        CreatedAt = m.CreatedAt,
        LastUpdatedAt = m.LastUpdatedAt,
        Model = m.Model,
        Provider = m.Provider.ToString().ToLowerInvariant(),
        SystemPrompt = m.SystemPrompt,
        LastMessagePreview = m.LastMessagePreview,
    };

    private void CleanupInactiveSessions(object? state)
    {
        var now = DateTime.UtcNow;
        var inactive = _sessions
            .Where(kvp => now - kvp.Value.LastActiveAt > _inactivityThreshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in inactive)
        {
            _logger.LogInformation("Cleaning up inactive session {SessionId}", sessionId);
            _ = RemoveAsync(sessionId);
        }

        if (inactive.Count > 0)
            _logger.LogInformation("Removed {Count} inactive sessions. {Remaining} still active.", inactive.Count, _sessions.Count);
    }
}

/// <summary>
/// Disposable wrapper to unsubscribe from an event on dispose.
/// </summary>
internal sealed class EventUnsubscriber<T> : IDisposable
{
    private readonly Action<EventHandler<T>> _unsubscribe;
    private readonly EventHandler<T> _handler;

    public EventUnsubscriber(Action<EventHandler<T>> unsubscribe, EventHandler<T> handler)
    {
        _unsubscribe = unsubscribe;
        _handler = handler;
    }

    public void Dispose() => _unsubscribe(_handler);
}
