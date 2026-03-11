using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Compaction;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.Agent.Tools;
using Microsoft.Extensions.Logging;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Agent.Core;

/// <summary>
/// Coordinates a conversation between an MCP server and an LLM.
/// Responsible for raising transcript and activity events, forwarding user input
/// to the provider, and executing tool calls returned by the LLM.
/// </summary>
public class ChatSession : IChatSessionEvents
{
    private sealed class ActiveTurnState : IDisposable
    {
        private readonly CancellationTokenRegistration _callerCancellationRegistration;

        public ActiveTurnState(CancellationToken callerCancellationToken)
        {
            CancellationSource = new CancellationTokenSource();
            if (callerCancellationToken.CanBeCanceled)
            {
                _callerCancellationRegistration = callerCancellationToken.Register(
                    static state => ((CancellationTokenSource)state!).Cancel(),
                    CancellationSource
                );
            }
        }

        public CancellationTokenSource CancellationSource { get; }

        public TaskCompletionSource<object?> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public CancellationToken CancellationToken => CancellationSource.Token;

        public void Dispose()
        {
            _callerCancellationRegistration.Dispose();
            CancellationSource.Dispose();
        }
    }

    private readonly object _stateGate = new();
    private readonly IChatClient _llmClient;
    private readonly IToolExecutor _toolExecutor;
    private readonly IChatTranscriptCompactor _transcriptCompactor;
    private readonly ILogger<ChatSession> _logger;
    private readonly List<ChatTranscriptEntry> _transcript = new();
    private readonly List<Tool> _registeredTools = new();
    private readonly Dictionary<string, Tool> _registeredToolsByName = new(
        StringComparer.OrdinalIgnoreCase
    );
    private ChatRequestOptions? _requestDefaults;
    private string? _sessionId;
    private string _systemPrompt = string.Empty;
    private DateTime _createdAt;
    private DateTime _lastActivityAt;
    private ActiveTurnState? _activeTurn;

    /// <summary>
    /// Gets the creation time of this session
    /// </summary>
    public DateTime CreatedAt => _createdAt;

    /// <summary>
    /// Gets the time of the last activity in this session
    /// </summary>
    public DateTime LastActivityAt => _lastActivityAt;

    public bool IsProcessing => Volatile.Read(ref _activeTurn) != null;

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
        IToolExecutor toolExecutor,
        ILogger<ChatSession> logger,
        IChatTranscriptCompactor? transcriptCompactor = null
    )
    {
        _llmClient = llmClient;
        _toolExecutor = toolExecutor;
        _transcriptCompactor = transcriptCompactor ?? EntryCountChatTranscriptCompactor.Default;
        _logger = logger;
        _createdAt = DateTime.UtcNow;
        _lastActivityAt = _createdAt;
    }

    public ChatSession(
        IChatClient llmClient,
        IToolExecutor toolExecutor,
        ILogger<ChatSession> logger,
        ChatSessionConfiguration configuration,
        IChatTranscriptCompactor? transcriptCompactor = null
    )
        : this(llmClient, toolExecutor, logger, transcriptCompactor)
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
        lock (_stateGate)
        {
            ThrowIfProcessingUnsafe();
            _transcript.Clear();
            _createdAt = DateTime.UtcNow;
            _lastActivityAt = _createdAt;
        }
    }

    /// <summary>
    /// Updates the system prompt for the current provider session.
    /// </summary>
    public void SetSystemPrompt(string systemPrompt)
    {
        lock (_stateGate)
        {
            ThrowIfProcessingUnsafe();
            _systemPrompt = systemPrompt ?? string.Empty;
        }
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

        lock (_stateGate)
        {
            ThrowIfProcessingUnsafe();
            SetSystemPromptUnsafe(configuration.SystemPrompt);
            SetRequestDefaultsUnsafe(configuration.RequestDefaults);
            RegisterToolsUnsafe(configuration.Tools);
        }
    }

    /// <summary>
    /// Updates the shared request defaults used for provider requests in this session.
    /// </summary>
    public void SetRequestDefaults(ChatRequestOptions? requestDefaults)
    {
        lock (_stateGate)
        {
            ThrowIfProcessingUnsafe();
            SetRequestDefaultsUnsafe(requestDefaults);
        }
    }

    /// <summary>
    /// Replaces the provider-facing tool set for the session.
    /// </summary>
    public void RegisterTools(IEnumerable<Tool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        lock (_stateGate)
        {
            ThrowIfProcessingUnsafe();
            RegisterToolsUnsafe(tools);
        }
    }

    public Task LoadTranscriptAsync(IReadOnlyList<ChatTranscriptEntry> transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        lock (_stateGate)
        {
            ThrowIfProcessingUnsafe();
            _transcript.Clear();
            _transcript.AddRange(transcript);

            if (_transcript.Count > 0)
            {
                _createdAt = _transcript[0].Timestamp.UtcDateTime;
                _lastActivityAt = _transcript[^1].Timestamp.UtcDateTime;
            }
        }

        return Task.CompletedTask;
    }

    public async Task SendUserMessageAsync(
        string message,
        CancellationToken cancellationToken = default
    )
    {
        var activeTurn = BeginTurn(cancellationToken);

        try
        {
            await SendUserMessageCoreAsync(message, activeTurn.CancellationToken);
        }
        finally
        {
            EndTurn(activeTurn);
        }
    }

    public void AbortCurrentTurn()
    {
        var activeTurn = Volatile.Read(ref _activeTurn);
        if (activeTurn == null)
        {
            return;
        }

        try
        {
            activeTurn.CancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The turn completed while abort was racing with cleanup.
        }
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        var activeTurn = Volatile.Read(ref _activeTurn);
        return activeTurn == null
            ? Task.CompletedTask
            : activeTurn.Completion.Task.WaitAsync(cancellationToken);
    }

    private async Task SendUserMessageCoreAsync(
        string message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        try
        {
            var nextTurn = await RequestProviderAsync(
                () => _llmClient.SendAsync(BuildRequest(), cancellationToken),
                turnId,
                cancellationToken
            );

            while (nextTurn is ChatClientAssistantTurn assistantTurn)
            {
                UpsertAssistantEntry(assistantTurn, turnId);

                var toolCalls = assistantTurn.Blocks.OfType<ToolCallAssistantBlock>().ToList();
                if (toolCalls.Count == 0)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteToolCallsAsync(toolCalls, turnId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                nextTurn = await RequestProviderAsync(
                    () => _llmClient.SendAsync(BuildRequest(), cancellationToken),
                    turnId,
                    cancellationToken
                );
            }

            if (nextTurn is ChatClientFailure failure)
            {
                AppendTranscript(ToErrorEntry(failure, turnId));
            }
        }
        finally
        {
            _lastActivityAt = DateTime.UtcNow;
        }
    }

    private ActiveTurnState BeginTurn(CancellationToken callerCancellationToken)
    {
        var activeTurn = new ActiveTurnState(callerCancellationToken);

        lock (_stateGate)
        {
            if (_activeTurn != null)
            {
                activeTurn.Dispose();
                throw new InvalidOperationException(
                    "A chat turn is already in progress for this session."
                );
            }

            _activeTurn = activeTurn;
        }

        return activeTurn;
    }

    private void EndTurn(ActiveTurnState activeTurn)
    {
        lock (_stateGate)
        {
            if (ReferenceEquals(_activeTurn, activeTurn))
            {
                _activeTurn = null;
            }
        }

        activeTurn.Completion.TrySetResult(null);
        activeTurn.Dispose();
    }

    private async Task<ChatClientTurnResult> RequestProviderAsync(
        Func<IChatCompletionStream> operation,
        string turnId,
        CancellationToken cancellationToken
    )
    {
        SetActivity(ChatSessionActivity.WaitingForProvider, turnId);

        try
        {
            var stream = operation();

            await foreach (var assistantTurn in stream.WithCancellation(cancellationToken))
            {
                UpsertAssistantEntry(assistantTurn, turnId);
            }

            return await stream.GetResultAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Provider request canceled for turn {TurnId}", turnId);
            throw;
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
        string turnId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetActivity(ChatSessionActivity.ExecutingTool, turnId);

        try
        {
            var tasks = toolCalls
                .Select(toolCall => ExecuteToolCallAsync(toolCall, turnId, cancellationToken))
                .ToArray();

            ToolInvocationResult[] results;
            try
            {
                results = await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppendCompletedToolResults(toolCalls, tasks, turnId);
                throw;
            }

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
        string turnId,
        CancellationToken cancellationToken
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

        if (!_registeredToolsByName.TryGetValue(toolCall.ToolName, out var tool))
        {
            _logger.LogError("Tool {ToolName} not registered for this session", toolCall.ToolName);

            var missingResult = ToolInvocationResultFactory.CreateError(
                toolCall.ToolCallId,
                toolCall.ToolName,
                "Tool not registered for this session"
            );

            ToolCallActivityChanged?.Invoke(
                this,
                new ToolCallActivityChangedEventArgs(
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    ToolCallExecutionState.Failed,
                    toolCall.Arguments,
                    missingResult,
                    "Tool not registered for this session"
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
            var invocationResult = await _toolExecutor.ExecuteAsync(
                new RuntimeToolInvocation(
                    toolCall.ToolCallId,
                    tool.Name,
                    toolCall.Arguments
                ),
                cancellationToken
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ToolCallActivityChanged?.Invoke(
                this,
                new ToolCallActivityChangedEventArgs(
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    ToolCallExecutionState.Cancelled,
                    toolCall.Arguments,
                    null,
                    "Tool execution canceled"
                )
            );

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing tool {ToolName}: {ErrorMessage}",
                toolCall.ToolName,
                ex.Message
            );

            var errorResult = ToolInvocationResultFactory.CreateError(
                toolCall.ToolCallId,
                toolCall.ToolName,
                ex.Message
            );

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

    private void AppendCompletedToolResults(
        IReadOnlyList<ToolCallAssistantBlock> toolCalls,
        IReadOnlyList<Task<ToolInvocationResult>> tasks,
        string turnId
    )
    {
        for (var index = 0; index < toolCalls.Count; index++)
        {
            if (!tasks[index].IsCompletedSuccessfully)
            {
                continue;
            }

            AppendToolResultTranscript(toolCalls[index], tasks[index].Result, turnId);
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

    private void SetSystemPromptUnsafe(string systemPrompt) => _systemPrompt = systemPrompt ?? string.Empty;

    private void SetRequestDefaultsUnsafe(ChatRequestOptions? requestDefaults) =>
        _requestDefaults = requestDefaults;

    private void RegisterToolsUnsafe(IEnumerable<Tool> tools)
    {
        _registeredTools.Clear();
        _registeredToolsByName.Clear();

        foreach (var tool in tools)
        {
            _registeredTools.Add(tool);
            _registeredToolsByName[tool.Name] = tool;
        }
    }

    private void ThrowIfProcessingUnsafe()
    {
        if (_activeTurn != null)
        {
            throw new InvalidOperationException(
                "This operation cannot be performed while a chat turn is in progress."
            );
        }
    }

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

}
