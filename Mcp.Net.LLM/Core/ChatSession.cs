using Mcp.Net.Client.Interfaces;
using Mcp.Net.LLM.Events;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Core;

/// <summary>
/// Represents an ongoing conversation session with an LLM
/// </summary>
public class ChatSession : IChatSessionEvents
{
    private readonly IChatClient _llmClient;
    private readonly IMcpClient _mcpClient;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<ChatSession> _logger;
    private string? _sessionId;
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

    /// <summary>
    /// Event raised when the session is started
    /// </summary>
    public event EventHandler? SessionStarted;

    /// <summary>
    /// Event raised when a user message is received
    /// </summary>
    public event EventHandler<string>? UserMessageReceived;

    /// <summary>
    /// Event raised when an assistant message is received
    /// </summary>
    public event EventHandler<string>? AssistantMessageReceived;

    /// <summary>
    /// Event raised when a tool execution state is updated
    /// </summary>
    public event EventHandler<ToolExecutionEventArgs>? ToolExecutionUpdated;

    /// <summary>
    /// Event raised when the thinking state changes
    /// </summary>
    public event EventHandler<ThinkingStateEventArgs>? ThinkingStateChanged;

    /// <summary>
    /// Gets the underlying LLM client
    /// </summary>
    public IChatClient GetLlmClient() => _llmClient;

    /// <summary>
    /// Gets or sets the session ID
    /// </summary>
    public string? SessionId
    {
        get => _sessionId;
        set => _sessionId = value;
    }

    /// <summary>
    /// Initializes a new instance of the ChatSession class
    /// </summary>
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

    /// <summary>
    /// Creates a new chat session based on an agent definition
    /// </summary>
    /// <param name="agent">The agent definition to use</param>
    /// <param name="factory">The agent factory</param>
    /// <param name="mcpClient">The MCP client</param>
    /// <param name="toolRegistry">The tool registry</param>
    /// <param name="logger">The logger</param>
    /// <param name="userId">Optional user ID for user-specific API keys</param>
    /// <returns>A new chat session configured with the agent's settings</returns>
    public static async Task<ChatSession> CreateFromAgentAsync(
        AgentDefinition agent,
        IAgentFactory factory,
        IMcpClient mcpClient,
        IToolRegistry toolRegistry,
        ILogger<ChatSession> logger,
        string? userId = null
    )
    {
        // Create a chat client from the agent definition, optionally with user-specific API key
        var chatClient = string.IsNullOrEmpty(userId)
            ? await factory.CreateClientFromAgentDefinitionAsync(agent)
            : await factory.CreateClientFromAgentDefinitionAsync(agent, userId);

        // Create a new chat session
        var session = new ChatSession(chatClient, mcpClient, toolRegistry, logger);

        // Associate the agent definition with the session
        session.AgentDefinition = agent;

        return session;
    }

    /// <summary>
    /// Creates a new chat session using an agent from the agent manager
    /// </summary>
    /// <param name="agentId">The ID of the agent to use</param>
    /// <param name="agentManager">The agent manager</param>
    /// <param name="mcpClient">The MCP client</param>
    /// <param name="toolRegistry">The tool registry</param>
    /// <param name="logger">The logger</param>
    /// <param name="userId">Optional user ID for user-specific API keys</param>
    /// <returns>A new chat session configured with the agent's settings</returns>
    public static async Task<ChatSession> CreateFromAgentIdAsync(
        string agentId,
        IAgentManager agentManager,
        IMcpClient mcpClient,
        IToolRegistry toolRegistry,
        ILogger<ChatSession> logger,
        string? userId = null
    )
    {
        // Get the agent definition
        var agent = await agentManager.GetAgentByIdAsync(agentId);
        if (agent == null)
        {
            throw new KeyNotFoundException($"Agent with ID {agentId} not found");
        }

        // Create a chat client from the agent
        var chatClient = await agentManager.CreateChatClientAsync(agentId, userId);

        // Create a new chat session
        var session = new ChatSession(chatClient, mcpClient, toolRegistry, logger);

        // Associate the agent definition with the session
        session.AgentDefinition = agent;

        return session;
    }

    /// <summary>
    /// Starts the chat session and raises the SessionStarted event
    /// </summary>
    public void StartSession()
    {
        SessionStarted?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Chat session started");
    }

    /// <summary>
    /// Sends a user message to the LLM and processes the response
    /// </summary>
    /// <param name="message">The user message</param>
    public async Task SendUserMessageAsync(string message)
    {
        // Update the last activity timestamp
        _lastActivityAt = DateTime.UtcNow;

        // Notify subscribers of the user message
        UserMessageReceived?.Invoke(this, message);

        _logger.LogDebug("Getting initial response for user message");
        var responseQueue = new Queue<LlmResponse>(await ProcessUserMessage(message));
        _logger.LogDebug("Initial response queue has {Count} items", responseQueue.Count);

        while (responseQueue.Count > 0)
        {
            var textResponses = new List<LlmResponse>();
            var toolResponses = new List<LlmResponse>();

            // Process the current batch of responses
            while (responseQueue.Count > 0)
            {
                var response = responseQueue.Dequeue();
                if (response.Type == MessageType.Assistant)
                {
                    textResponses.Add(response);
                }
                else if (response.Type == MessageType.Tool)
                {
                    toolResponses.Add(response);
                }
            }

            // Display any text responses
            foreach (var textResponse in textResponses)
            {
                _logger.LogDebug(
                    "Processing assistant message: {MessagePreview}...",
                    textResponse.Content.Substring(0, Math.Min(30, textResponse.Content.Length))
                );
                await DisplayMessageResponse(textResponse);
            }

            // Execute any tool calls and process the results
            if (toolResponses.Count > 0)
            {
                var toolResults = await ExecuteToolCalls(toolResponses);
                var responses = await SendToolResult(responseQueue, toolResults);

                foreach (var response in responses)
                {
                    responseQueue.Enqueue(response);
                }
            }

            // Update the last activity timestamp after each batch
            _lastActivityAt = DateTime.UtcNow;
        }
    }

    private async Task<IEnumerable<LlmResponse>> SendToolResult(
        Queue<LlmResponse> responseQueue,
        List<ToolInvocationResult> toolResults
    )
    {
        _logger.LogDebug("Total of {Count} tool results to send", toolResults.Count);

        // When processing tool results and waiting for LLM response, we show a single "Thinking..." indicator
        _logger.LogDebug("Setting thinking state to true for LLM response generation");
        ThinkingStateChanged?.Invoke(
            this,
            new ThinkingStateEventArgs(true, "thinking", _sessionId)
        );

        try
        {
            var responses = await _llmClient.SendToolResultsAsync(toolResults);
            return responses;
        }
        finally
        {
            _logger.LogDebug("Setting thinking state to false after LLM response generation");
            ThinkingStateChanged?.Invoke(
                this,
                new ThinkingStateEventArgs(false, "thinking", _sessionId)
            );
        }
    }

    private async Task<IEnumerable<LlmResponse>> ProcessUserMessage(string userInput)
    {
        var userMessage = new LlmMessage { Type = MessageType.User, Content = userInput };

        // When generating initial response to user message, show a "Thinking..." indicator
        _logger.LogDebug("Setting thinking state to true for initial user message processing");
        ThinkingStateChanged?.Invoke(
            this,
            new ThinkingStateEventArgs(true, "thinking", _sessionId)
        );

        try
        {
            var response = await _llmClient.SendMessageAsync(userMessage);
            return response;
        }
        finally
        {
            _logger.LogDebug("Setting thinking state to false after initial response generation");
            ThinkingStateChanged?.Invoke(
                this,
                new ThinkingStateEventArgs(false, "thinking", _sessionId)
            );
        }
    }

    /// <summary>
    /// Given a Message response back from the LLM, notify subscribers about it
    /// </summary>
    private async Task DisplayMessageResponse(LlmResponse response)
    {
        AssistantMessageReceived?.Invoke(this, response.Content);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Given a list of tool invocations, executes them against the MCP server and returns the results.
    /// </summary>
    private async Task<List<ToolInvocationResult>> ExecuteToolCalls(List<LlmResponse> toolResponses)
    {
        _logger.LogDebug("Starting ExecuteToolCalls batch");

        var accumulatedResults = new List<ToolInvocationResult>();

        foreach (var toolResponse in toolResponses)
        {
            var toolCalls = toolResponse.ToolCalls;
            _logger.LogDebug("Found {Count} tool calls to process in response", toolCalls.Count);

            foreach (var invocation in toolCalls)
            {
                _logger.LogDebug(
                    "Processing tool call {ToolName} ({ToolCallId})",
                    invocation.Name,
                    invocation.Id
                );

                accumulatedResults.Add(await ExecuteToolCall(invocation));
            }
        }

        return accumulatedResults;
    }

    /// <summary>
    /// Executes the supplied tool invocation and returns a structured result payload.
    /// </summary>
    private async Task<ToolInvocationResult> ExecuteToolCall(ToolInvocation toolInvocation)
    {
        _logger.LogDebug("Starting ExecuteToolCall for {ToolName}", toolInvocation.Name);

        var tool = _toolRegistry.GetToolByName(toolInvocation.Name);
        if (tool == null)
        {
            _logger.LogError("Tool {ToolName} not found", toolInvocation.Name);

            var missingResult = CreateErrorResult(
                toolInvocation,
                "Tool not found in registry",
                isError: true
            );

            ToolExecutionUpdated?.Invoke(
                this,
                new ToolExecutionEventArgs(
                    toolInvocation,
                    ToolExecutionState.Failed,
                    success: false,
                    errorMessage: "Tool not found in registry",
                    result: missingResult
                )
            );

            return missingResult;
        }

        _logger.LogDebug(
            "Calling tool {ToolName} with arguments: {@Arguments}",
            tool.Name,
            toolInvocation.Arguments
        );

        ToolExecutionUpdated?.Invoke(
            this,
            new ToolExecutionEventArgs(
                toolInvocation,
                ToolExecutionState.Starting,
                success: true
            )
        );

        try
        {
            var mcpResult = await _mcpClient.CallTool(tool.Name, toolInvocation.Arguments);
            var invocationResult = ToolInvocationResult.FromMcpResult(
                toolInvocation.Id,
                toolInvocation.Name,
                mcpResult
            );

            var eventSuccess = !invocationResult.IsError;
            var errorMessage = invocationResult.IsError
                ? string.Join(Environment.NewLine, invocationResult.Text)
                : null;

            ToolExecutionUpdated?.Invoke(
                this,
                new ToolExecutionEventArgs(
                    toolInvocation,
                    ToolExecutionState.Completed,
                    success: eventSuccess,
                    errorMessage: errorMessage,
                    result: invocationResult
                )
            );

            return invocationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing tool {ToolName}: {ErrorMessage}",
                toolInvocation.Name,
                ex.Message
            );

            var errorResult = CreateErrorResult(toolInvocation, ex.Message, isError: true);

            ToolExecutionUpdated?.Invoke(
                this,
                new ToolExecutionEventArgs(
                    toolInvocation,
                    ToolExecutionState.Failed,
                    success: false,
                    errorMessage: ex.Message,
                    result: errorResult
                )
            );

            return errorResult;
        }
    }

    private static ToolInvocationResult CreateErrorResult(
        ToolInvocation invocation,
        string message,
        bool isError
    )
    {
        var text = string.IsNullOrWhiteSpace(message)
            ? Array.Empty<string>()
            : new[] { message };

        return new ToolInvocationResult(
            invocation.Id,
            invocation.Name,
            isError,
            text,
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );
    }
}
