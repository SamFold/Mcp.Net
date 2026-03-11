using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
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
    private readonly ILogger<ChatSession> _logger;
    private readonly List<ChatTranscriptEntry> _transcript = new();
    private readonly List<Tool> _registeredTools = new();
    private string? _sessionId;
    private string _systemPrompt = string.Empty;
    private DateTime _createdAt;
    private DateTime _lastActivityAt;

    /// <summary>
    /// The agent definition associated with this chat session (if any)
    /// </summary>
    public AgentDefinition? AgentDefinition { get; private set; }

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
        ILogger<ChatSession> logger
    )
    {
        _llmClient = llmClient;
        _mcpClient = mcpClient;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _createdAt = DateTime.UtcNow;
        _lastActivityAt = _createdAt;
    }

    public static async Task<ChatSession> CreateFromAgentAsync(
        AgentDefinition agent,
        IAgentFactory factory,
        IMcpClient mcpClient,
        IToolRegistry toolRegistry,
        ILogger<ChatSession> logger,
        string? userId = null
    )
    {
        var chatClient = string.IsNullOrEmpty(userId)
            ? await factory.CreateClientFromAgentDefinitionAsync(agent)
            : await factory.CreateClientFromAgentDefinitionAsync(agent, userId);

        var session = new ChatSession(chatClient, mcpClient, toolRegistry, logger)
        {
            AgentDefinition = agent,
        };
        session.ApplyAgentConfiguration(agent);

        return session;
    }

    public static async Task<ChatSession> CreateFromAgentIdAsync(
        string agentId,
        IAgentManager agentManager,
        IMcpClient mcpClient,
        IToolRegistry toolRegistry,
        ILogger<ChatSession> logger,
        string? userId = null
    )
    {
        var agent = await agentManager.GetAgentByIdAsync(agentId);
        if (agent == null)
        {
            throw new KeyNotFoundException($"Agent with ID {agentId} not found");
        }

        var chatClient = await agentManager.CreateChatClientAsync(agentId, userId);
        var session = new ChatSession(chatClient, mcpClient, toolRegistry, logger)
        {
            AgentDefinition = agent,
        };
        session.ApplyAgentConfiguration(agent);

        return session;
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
        var assistantTurnUpdates = CreateAssistantTurnProgress(turnId);

        AppendTranscript(
            new UserChatEntry(
                Guid.NewGuid().ToString("n"),
                DateTimeOffset.UtcNow,
                message,
                turnId
            )
        );

        var nextTurn = await RequestProviderAsync(
            () => _llmClient.SendAsync(BuildRequest(), assistantTurnUpdates),
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
                () => _llmClient.SendAsync(BuildRequest(), assistantTurnUpdates),
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
        Func<Task<ChatClientTurnResult>> operation,
        string turnId
    )
    {
        SetActivity(ChatSessionActivity.WaitingForProvider, turnId);

        try
        {
            return await operation();
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
            var results = new List<ToolInvocationResult>();

            foreach (var toolCall in toolCalls)
            {
                results.Add(await ExecuteToolCallAsync(toolCall, turnId));
            }

            return results;
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

            AppendTranscript(
                new ToolResultChatEntry(
                    Guid.NewGuid().ToString("n"),
                    DateTimeOffset.UtcNow,
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    missingResult,
                    true,
                    turnId
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

            AppendTranscript(
                new ToolResultChatEntry(
                    Guid.NewGuid().ToString("n"),
                    DateTimeOffset.UtcNow,
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    invocationResult,
                    invocationResult.IsError,
                    turnId
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

            AppendTranscript(
                new ToolResultChatEntry(
                    Guid.NewGuid().ToString("n"),
                    DateTimeOffset.UtcNow,
                    toolCall.ToolCallId,
                    toolCall.ToolName,
                    errorResult,
                    true,
                    turnId
                )
            );

            return errorResult;
        }
    }

    private void AppendTranscript(
        ChatTranscriptEntry entry,
        ChatTranscriptChangeKind changeKind = ChatTranscriptChangeKind.Added
    )
    {
        _transcript.Add(entry);
        TranscriptChanged?.Invoke(this, new ChatTranscriptChangedEventArgs(entry, changeKind));
    }

    private void ApplyAgentConfiguration(AgentDefinition agent)
    {
        SetSystemPrompt(agent.SystemPrompt);
        var enabledTools = _toolRegistry.EnabledTools ?? Array.Empty<Tool>();
        var allTools = _toolRegistry.AllTools ?? Array.Empty<Tool>();

        if (agent.ToolIds.Count == 0)
        {
            RegisterTools(enabledTools);
            return;
        }

        var selectedTools = allTools.Where(tool => agent.ToolIds.Contains(tool.Name)).ToArray();
        RegisterTools(selectedTools);
    }

    private ChatClientRequest BuildRequest() =>
        new(
            _systemPrompt,
            _transcript,
            _registeredTools.Select(tool => new ChatClientTool(tool.Name, tool.Description, tool.InputSchema)).ToArray()
        );

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

    private IProgress<ChatClientAssistantTurn> CreateAssistantTurnProgress(string turnId) =>
        new AssistantTurnProgress(turn => UpsertAssistantEntry(turn, turnId));

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

    private sealed class AssistantTurnProgress : IProgress<ChatClientAssistantTurn>
    {
        private readonly Action<ChatClientAssistantTurn> _handler;

        public AssistantTurnProgress(Action<ChatClientAssistantTurn> handler)
        {
            _handler = handler;
        }

        public void Report(ChatClientAssistantTurn value)
        {
            _handler(value);
        }
    }
}
