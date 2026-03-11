using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Compaction;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Core;

/// <summary>
/// Coordinates a conversation between an MCP server and an LLM.
/// Responsible for raising transcript and activity events, forwarding user input
/// to the provider, and executing tool calls returned by the LLM.
/// </summary>
public class ChatSession : IChatSessionEvents
{
    private readonly IChatClient _llmClient;
    private readonly IMcpClient _mcpClient;
    private readonly IToolRegistry _toolRegistry;
    private readonly IChatTranscriptCompactor _transcriptCompactor;
    private readonly ILogger<ChatSession> _logger;
    private readonly List<ChatTranscriptEntry> _transcript = new();
    private readonly List<Tool> _registeredTools = new();
    private ChatRequestOptions? _requestDefaults;
    private string? _sessionId;
    private string _systemPrompt = string.Empty;
    private DateTime _createdAt;
    private DateTime _lastActivityAt;

    /// <summary>
    /// Gets the creation time of this session
    /// </summary>
    public DateTime CreatedAt => _createdAt;

    /// <summary>
    /// Gets the time of the last activity in this session
    /// </summary>
    public DateTime LastActivityAt => _lastActivityAt;

    public IReadOnlyList<ChatTranscriptEntry> Transcript => _transcript;

    public event EventHandler? SessionStarted;

    public event EventHandler<ChatTranscriptChangedEventArgs>? TranscriptChanged;

    public event EventHandler<ChatSessionActivityChangedEventArgs>? ActivityChanged;

    public event EventHandler<ToolCallActivityChangedEventArgs>? ToolCallActivityChanged;

    /// <summary>
    /// Gets or sets the session ID
    /// </summary>
    public string? SessionId
    {
        get => _sessionId;
        set => _sessionId = value;
    }

    public ChatSession(
        IChatClient llmClient,
        IMcpClient mcpClient,
        IToolRegistry toolRegistry,
        ILogger<ChatSession> logger,
        IChatTranscriptCompactor? transcriptCompactor = null
    )
    {
        _llmClient = llmClient;
        _mcpClient = mcpClient;
        _toolRegistry = toolRegistry;
        _transcriptCompactor = transcriptCompactor ?? EntryCountChatTranscriptCompactor.Default;
        _logger = logger;
        _createdAt = DateTime.UtcNow;
        _lastActivityAt = _createdAt;
    }

    public ChatSession(
        IChatClient llmClient,
        IMcpClient mcpClient,
        IToolRegistry toolRegistry,
        ILogger<ChatSession> logger,
        ChatSessionConfiguration configuration,
        IChatTranscriptCompactor? transcriptCompactor = null
    )
        : this(llmClient, mcpClient, toolRegistry, logger, transcriptCompactor)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ApplyConfiguration(configuration);
    }

    public void StartSession()
    {
        SessionStarted?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Chat session started");
    }

    /// <summary>
    /// Clears the in-memory transcript for the current conversation.
    /// </summary>
    public void ResetConversation()
    {
        _transcript.Clear();
        _createdAt = DateTime.UtcNow;
        _lastActivityAt = _createdAt;
    }

    /// <summary>
    /// Updates the system prompt for the current provider session.
    /// </summary>
    public void SetSystemPrompt(string systemPrompt)
    {
        _systemPrompt = systemPrompt ?? string.Empty;
    }

    /// <summary>
    /// Returns the current provider system prompt.
    /// </summary>
    public string GetSystemPrompt() => _systemPrompt;

    /// <summary>
    /// Applies a runtime configuration snapshot to this session.
    /// </summary>
    public void ApplyConfiguration(ChatSessionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        SetSystemPrompt(configuration.SystemPrompt);
        SetRequestDefaults(configuration.RequestDefaults);
        RegisterTools(configuration.Tools);
    }

    /// <summary>
    /// Updates the shared request defaults used for provider requests in this session.
    /// </summary>
    public void SetRequestDefaults(ChatRequestOptions? requestDefaults)
    {
        _requestDefaults = requestDefaults;
    }

    /// <summary>
    /// Replaces the provider-facing tool set for the session.
    /// </summary>
    public void RegisterTools(IEnumerable<Tool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        _registeredTools.Clear();
        _registeredTools.AddRange(tools);
    }

    public Task LoadTranscriptAsync(IReadOnlyList<ChatTranscriptEntry> transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        _transcript.Clear();
        _transcript.AddRange(transcript);

        if (_transcript.Count > 0)
        {
            _createdAt = _transcript[0].Timestamp.UtcDateTime;
            _lastActivityAt = _transcript[^1].Timestamp.UtcDateTime;
        }

        return Task.CompletedTask;
    }

    public async Task SendUserMessageAsync(string message)
    {
        _lastActivityAt = DateTime.UtcNow;
        var turnId = Guid.NewGuid().ToString("n");

        AppendTranscript(
            new UserChatEntry(
                Guid.NewGuid().ToString("n"),
                DateTimeOffset.UtcNow,
                message,
                turnId
            )
        );

        var nextTurn = await RequestProviderAsync(
            () => _llmClient.SendAsync(BuildRequest()),
            turnId
        );

        while (nextTurn is ChatClientAssistantTurn assistantTurn)
        {
            UpsertAssistantEntry(assistantTurn, turnId);

            var toolCalls = assistantTurn.Blocks.OfType<ToolCallAssistantBlock>().ToList();
            if (toolCalls.Count == 0)
            {
                break;
            }

            await ExecuteToolCallsAsync(toolCalls, turnId);
            nextTurn = await RequestProviderAsync(
                () => _llmClient.SendAsync(BuildRequest()),
                turnId
            );
        }

        if (nextTurn is ChatClientFailure failure)
        {
            AppendTranscript(ToErrorEntry(failure, turnId));
        }

        _lastActivityAt = DateTime.UtcNow;
    }

    private async Task<ChatClientTurnResult> RequestProviderAsync(
        Func<IChatCompletionStream> operation,
        string turnId
    )
    {
        SetActivity(ChatSessionActivity.WaitingForProvider, turnId);

        try
        {
            var stream = operation();

            await foreach (var assistantTurn in stream)
            {
                UpsertAssistantEntry(assistantTurn, turnId);
            }

            return await stream.GetResultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider request failed");

            return new ChatClientFailure(
                ChatErrorSource.Provider,
                ex.Message,
                Details: ex.ToString()
            );
        }
        finally
        {
            SetActivity(ChatSessionActivity.Idle, turnId);
        }
    }

    private async Task<List<ToolInvocationResult>> ExecuteToolCallsAsync(
        IReadOnlyList<ToolCallAssistantBlock> toolCalls,
        string turnId
    )
    {
        SetActivity(ChatSessionActivity.ExecutingTool, turnId);

        try
        {
            var tasks = toolCalls
                .Select(toolCall => ExecuteToolCallAsync(toolCall, turnId))
                .ToArray();
            var results = await Task.WhenAll(tasks);

            for (var index = 0; index < toolCalls.Count; index++)
            {
                AppendToolResultTranscript(toolCalls[index], results[index], turnId);
            }

            return results.ToList();
        }
        finally
        {
            SetActivity(ChatSessionActivity.Idle, turnId);
        }
    }

    private async Task<ToolInvocationResult> ExecuteToolCallAsync(
        ToolCallAssistantBlock toolCall,
        string turnId
    )
    {
        ToolCallActivityChanged?.Invoke(
            this,
            new ToolCallActivityChangedEventArgs(
                toolCall.ToolCallId,
                toolCall.ToolName,
                ToolCallExecutionState.Queued,
                toolCall.Arguments
            )
        );

        var tool = _toolRegistry.GetToolByName(toolCall.ToolName);
        if (tool == null)
        {
            _logger.LogError("Tool {ToolName} not found", toolCall.ToolName);

            var missingResult = CreateErrorResult(toolCall.ToolCallId, toolCall.ToolName, "Tool not found in registry");

            ToolCallActivityChanged?.Invoke(
                this,
                new ToolCallActivityChangedEventArgs(
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    ToolCallExecutionState.Failed,
                    toolCall.Arguments,
                    missingResult,
                    "Tool not found in registry"
                )
            );

            return missingResult;
        }

        ToolCallActivityChanged?.Invoke(
            this,
            new ToolCallActivityChangedEventArgs(
                toolCall.ToolCallId,
                toolCall.ToolName,
                ToolCallExecutionState.Running,
                toolCall.Arguments
            )
        );

        try
        {
            var mcpResult = await _mcpClient.CallTool(tool.Name, toolCall.Arguments);
            var invocationResult = ToolResultConverter.FromMcpResult(
                toolCall.ToolCallId,
                toolCall.ToolName,
                mcpResult
            );

            ToolCallActivityChanged?.Invoke(
                this,
                new ToolCallActivityChangedEventArgs(
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    invocationResult.IsError
                        ? ToolCallExecutionState.Failed
                        : ToolCallExecutionState.Completed,
                    toolCall.Arguments,
                    invocationResult,
                    invocationResult.IsError
                        ? string.Join(Environment.NewLine, invocationResult.Text)
                        : null
                )
            );

            return invocationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing tool {ToolName}: {ErrorMessage}",
                toolCall.ToolName,
                ex.Message
            );

            var errorResult = CreateErrorResult(toolCall.ToolCallId, toolCall.ToolName, ex.Message);

            ToolCallActivityChanged?.Invoke(
                this,
                new ToolCallActivityChangedEventArgs(
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    ToolCallExecutionState.Failed,
                    toolCall.Arguments,
                    errorResult,
                    ex.Message
                )
            );

            return errorResult;
        }
    }

    private void AppendToolResultTranscript(
        ToolCallAssistantBlock toolCall,
        ToolInvocationResult result,
        string turnId
    )
    {
        AppendTranscript(
            new ToolResultChatEntry(
                Guid.NewGuid().ToString("n"),
                DateTimeOffset.UtcNow,
                toolCall.ToolCallId,
                toolCall.ToolName,
                result,
                result.IsError,
                turnId
            )
        );
    }

    private void AppendTranscript(
        ChatTranscriptEntry entry,
        ChatTranscriptChangeKind changeKind = ChatTranscriptChangeKind.Added
    )
    {
        _transcript.Add(entry);
        TranscriptChanged?.Invoke(this, new ChatTranscriptChangedEventArgs(entry, changeKind));
    }

    private ChatClientRequest BuildRequest()
    {
        var compactedTranscript = _transcriptCompactor.Compact(_transcript);
        if (compactedTranscript.Count != _transcript.Count)
        {
            _logger.LogDebug(
                "Compacted transcript from {OriginalCount} to {CompactedCount} entries before provider request",
                _transcript.Count,
                compactedTranscript.Count
            );
        }

        return new ChatClientRequest(
            _systemPrompt,
            compactedTranscript,
            _registeredTools.Select(tool => new ChatClientTool(tool.Name, tool.Description, tool.InputSchema)).ToArray(),
            BuildRequestOptions()
        );
    }

    private ChatRequestOptions? BuildRequestOptions() => _requestDefaults;

    private void UpsertAssistantEntry(ChatClientAssistantTurn turn, string turnId)
    {
        var assistantEntry = ToAssistantEntry(turn, turnId);
        var existingIndex = _transcript.FindIndex(entry => entry.Id == assistantEntry.Id);
        if (existingIndex < 0)
        {
            AppendTranscript(assistantEntry);
            return;
        }

        var existingEntry = _transcript[existingIndex] as AssistantChatEntry;
        var updatedEntry = existingEntry == null
            ? assistantEntry
            : assistantEntry with { Timestamp = existingEntry.Timestamp };

        if (Equals(_transcript[existingIndex], updatedEntry))
        {
            return;
        }

        _transcript[existingIndex] = updatedEntry;
        TranscriptChanged?.Invoke(
            this,
            new ChatTranscriptChangedEventArgs(updatedEntry, ChatTranscriptChangeKind.Updated)
        );
    }

    private void SetActivity(ChatSessionActivity activity, string turnId)
    {
        _logger.LogDebug(
            "Setting chat activity to {Activity} for turn {TurnId}",
            activity,
            turnId
        );

        ActivityChanged?.Invoke(
            this,
            new ChatSessionActivityChangedEventArgs(activity, turnId, _sessionId)
        );
    }

    private static AssistantChatEntry ToAssistantEntry(
        ChatClientAssistantTurn turn,
        string turnId
    ) =>
        new(
            turn.Id,
            DateTimeOffset.UtcNow,
            turn.Blocks,
            turnId,
            turn.Provider,
            turn.Model,
            turn.StopReason,
            turn.Usage
        );

    private static ErrorChatEntry ToErrorEntry(ChatClientFailure failure, string turnId) =>
        new(
            Guid.NewGuid().ToString("n"),
            DateTimeOffset.UtcNow,
            failure.Source,
            failure.Message,
            failure.Code,
            failure.Details,
            null,
            failure.IsRetryable,
            turnId,
            failure.Provider,
            failure.Model
        );

    private static ToolInvocationResult CreateErrorResult(
        string toolCallId,
        string toolName,
        string message
    )
    {
        var text = string.IsNullOrWhiteSpace(message) ? Array.Empty<string>() : new[] { message };

        return new ToolInvocationResult(
            toolCallId,
            toolName,
            true,
            text,
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );
    }
}
