using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;
using Microsoft.Extensions.Logging;
using Tool = Anthropic.SDK.Common.Tool;

namespace Mcp.Net.LLM.Anthropic;

/// <summary>
/// Adapter that bridges the MCP-friendly chat model to Anthropic's Claude API.
/// Maintains message history, handles tool results, and normalises tool-use payloads.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private readonly List<Message> _messages = new();
    private readonly List<SystemMessage> _systemMessages = new();
    private readonly List<Tool> _anthropicTools = new();
    private readonly IAnthropicMessageClient _messagesClient;
    private readonly string _model;
    private readonly ILogger<AnthropicChatClient> _logger;
    private string _systemPrompt =
        "You are a helpful assistant with access to various tools including calculators "
        + "and Warhammer 40k themed functions. Use these tools when appropriate.";

    public AnthropicChatClient(ChatClientOptions options, ILogger<AnthropicChatClient> logger)
        : this(options, logger, new AnthropicMessageClient(options.ApiKey))
    {
    }

    internal AnthropicChatClient(
        ChatClientOptions options,
        ILogger<AnthropicChatClient> logger,
        IAnthropicMessageClient messagesClient
    )
    {
        _logger = logger;
        _messagesClient = messagesClient ?? throw new ArgumentNullException(nameof(messagesClient));
        _systemPrompt = string.IsNullOrWhiteSpace(options.SystemPrompt)
            ? _systemPrompt
            : options.SystemPrompt;

        // Determine the model to use
        if (string.IsNullOrEmpty(options.Model) || !options.Model.StartsWith("claude"))
        {
            _model = "claude-sonnet-4-5-20250929"; // Default
            _logger.LogWarning(
                "Invalid or missing model name '{ModelName}', using default model: {DefaultModel}",
                options.Model,
                _model
            );
        }
        else
        {
            _model = options.Model;
            _logger.LogInformation("Using Anthropic model: {Model}", _model);
        }

        _systemMessages.Add(new SystemMessage(_systemPrompt));
    }

    /// <summary>
    /// Sets or updates the system prompt for the chat session
    /// </summary>
    public void SetSystemPrompt(string systemPrompt)
    {
        _logger.LogInformation("Setting system prompt for Anthropic chat client");
        _systemPrompt = systemPrompt;

        // Update the system messages list
        _systemMessages.Clear();
        _systemMessages.Add(new SystemMessage(_systemPrompt));
    }

    /// <summary>
    /// Resets the conversation history, clearing all past messages
    /// </summary>
    public void ResetConversation()
    {
        _logger.LogInformation("Resetting conversation history for Anthropic chat client");
        _messages.Clear();

        // Re-add system messages as needed
        _systemMessages.Clear();
        _systemMessages.Add(new SystemMessage(_systemPrompt));
    }

    /// <summary>
    /// Registers the available MCP tools so the Anthropic API can surface them in tool-use responses.
    /// </summary>
    /// <param name="tools">Collection of MCP tool descriptors to expose to the model.</param>
    public void RegisterTools(IEnumerable<Mcp.Net.Core.Models.Tools.Tool> tools)
    {
        foreach (var tool in tools)
        {
            _logger.LogDebug("Registering Anthropic tool {ToolName}", tool.Name);
            _anthropicTools.Add(ConvertToAnthropicTool(tool));
        }
    }

    private static Tool ConvertToAnthropicTool(Mcp.Net.Core.Models.Tools.Tool mcpTool)
    {
        var toolName = mcpTool.Name;
        var toolDescription = mcpTool.Description;
        var toolSchema = JsonNode.Parse(mcpTool.InputSchema.GetRawText());

        var function = new Function(toolName, toolDescription, toolSchema);

        return new Tool(function);
    }

    /// <summary>
    /// Adds a tool result to the Anthropic message history without triggering an API request.
    /// </summary>
    /// <param name="result">The tool result returned from the MCP layer.</param>
    private void AddToolResultToHistory(ToolInvocationResult? result)
    {
        if (result == null)
        {
            return;
        }

        _messages.Add(
            new Message
            {
                Role = RoleType.User,
                Content = new List<ContentBase>
                {
                    new ToolResultContent
                    {
                        ToolUseId = result.ToolCallId,
                        Content = [new TextContent { Text = result.ToWireJson() }],
                    },
                },
            }
        );
    }

    /// <summary>
    /// Generates a response from Claude using the accumulated message history.
    /// </summary>
    private async Task<ChatClientTurnResult> GetTurnResultAsync()
    {
        try
        {
            var parameters = new MessageParameters
            {
                Model = _model,
                MaxTokens = 1024,
                Temperature = 1.0m,
                Messages = _messages.ToList(),
                Tools = _anthropicTools.ToList(),
                System = _systemMessages.ToList(),
            };

            var content = await _messagesClient.GetResponseContentAsync(parameters);
            var assistantMessage = content.ToList();

            _messages.Add(new Message { Role = RoleType.Assistant, Content = assistantMessage });

            return BuildAssistantTurn(assistantMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Claude API: {Message}", ex.Message);
            return new ChatClientFailure(
                ChatErrorSource.Provider,
                $"Error: {ex.Message}",
                Details: ex.ToString(),
                Provider: "anthropic",
                Model: _model
            );
        }
    }

    private ChatClientAssistantTurn BuildAssistantTurn(IEnumerable<ContentBase> contentItems)
    {
        var blocks = new List<AssistantContentBlock>();

        foreach (var content in contentItems)
        {
            switch (content)
            {
                case ThinkingContent thinking:
                    blocks.Add(
                        new ReasoningAssistantBlock(
                            Guid.NewGuid().ToString("n"),
                            thinking.Thinking,
                            ReasoningVisibility.Visible,
                            string.IsNullOrWhiteSpace(thinking.Signature) ? null : thinking.Signature
                        )
                    );
                    break;
                case RedactedThinkingContent redactedThinking:
                    blocks.Add(
                        new ReasoningAssistantBlock(
                            Guid.NewGuid().ToString("n"),
                            null,
                            ReasoningVisibility.Redacted,
                            string.IsNullOrWhiteSpace(redactedThinking.Data)
                                ? null
                                : redactedThinking.Data
                        )
                    );
                    break;
                case ToolUseContent toolUse:
                    foreach (var invocation in ExtractToolCalls(toolUse))
                    {
                        blocks.Add(
                            new ToolCallAssistantBlock(
                                Guid.NewGuid().ToString("n"),
                                invocation.Id,
                                invocation.Name,
                                invocation.Arguments
                            )
                        );
                    }
                    break;
                case TextContent text:
                    blocks.Add(new TextAssistantBlock(Guid.NewGuid().ToString("n"), text.Text));
                    break;
                default:
                    _logger.LogWarning(
                        "Received unsupported content type {ContentType} from Anthropic response",
                        content.GetType().Name
                    );
                    break;
            }
        }

        return new ChatClientAssistantTurn(
            Guid.NewGuid().ToString("n"),
            "anthropic",
            _model,
            blocks
        );
    }

    private IEnumerable<ToolInvocation> ExtractToolCalls(ToolUseContent toolUseContent)
    {
        if (toolUseContent.Type != ContentType.tool_use)
        {
            yield break;
        }

        var arguments = ParseToolArguments(toolUseContent.Input);

        if (toolUseContent.Id != null && toolUseContent.Name != null)
        {
            yield return new ToolInvocation(toolUseContent.Id, toolUseContent.Name, arguments);
        }
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(JsonNode? input)
    {
        if (input is not JsonObject jsonObject)
        {
            return new Dictionary<string, object?>();
        }

        var arguments = new Dictionary<string, object?>();
        foreach (var property in jsonObject)
        {
            arguments[property.Key] = ConvertJsonValue(property.Value);
        }

        return arguments;
    }

    private static object? ConvertJsonValue(JsonNode? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonValue jsonValue when jsonValue.TryGetValue<double>(out var number):
                return number;
            case JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolean):
                return boolean;
            case JsonValue jsonValue:
                return jsonValue.ToString();
            case JsonObject or JsonArray:
                return CloneStructuredValue(value);
            default:
                return value.ToString();
        }
    }

    private static JsonElement CloneStructuredValue(JsonNode value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Sends a user message to Claude and returns the resulting response.
    /// </summary>
    public async Task<ChatClientTurnResult> SendMessageAsync(string userMessage)
    {
        _messages.Add(
            new Message
            {
                Role = RoleType.User,
                Content = new List<ContentBase>
                {
                    new TextContent { Text = userMessage },
                },
            }
        );

        _logger.LogDebug("User message added to history (Anthropic): {Message}", userMessage);
        return await GetTurnResultAsync();
    }

    /// <summary>
     /// Appends tool execution results and requests the next assistant response batch.
     /// </summary>
    public async Task<ChatClientTurnResult> SendToolResultsAsync(
        IEnumerable<ToolInvocationResult> toolResults
    )
    {
        foreach (var toolResult in toolResults)
        {
            _logger.LogDebug(
                "Adding tool result for {ToolName} with ID {ToolId} to history",
                toolResult.ToolName,
                toolResult.ToolCallId
            );

            AddToolResultToHistory(toolResult);
        }

        _logger.LogDebug("Making single API call after adding all tool results");
        return await GetTurnResultAsync();
    }

    /// <summary>
    /// Gets the currently configured system prompt.
    /// </summary>
    public string GetSystemPrompt()
    {
        return _systemPrompt;
    }

    public ReplayTarget GetReplayTarget() => new("anthropic", _model);

    public void LoadReplayTranscript(ProviderReplayTranscript replayTranscript)
    {
        ArgumentNullException.ThrowIfNull(replayTranscript);

        _messages.Clear();

        foreach (var entry in replayTranscript.Entries)
        {
            switch (entry)
            {
                case UserChatEntry user:
                    _messages.Add(
                        new Message
                        {
                            Role = RoleType.User,
                            Content = new List<ContentBase>
                            {
                                new TextContent { Text = user.Content },
                            },
                        }
                    );
                    break;
                case AssistantChatEntry assistant:
                    var replayContent = assistant
                        .Blocks.Select(ToReplayContent)
                        .OfType<ContentBase>()
                        .ToList();

                    if (replayContent.Count == 0)
                    {
                        break;
                    }

                    _messages.Add(
                        new Message
                        {
                            Role = RoleType.Assistant,
                            Content = replayContent,
                        }
                    );
                    break;
                case ToolResultChatEntry toolResult:
                    AddToolResultToHistory(toolResult.Result);
                    break;
            }
        }
    }

    private static ContentBase? ToReplayContent(AssistantContentBlock block) =>
        block switch
        {
            TextAssistantBlock text => new TextContent { Text = text.Text },
            ReasoningAssistantBlock
            {
                Visibility: ReasoningVisibility.Visible,
                Text: { Length: > 0 } text,
                ReplayToken: { Length: > 0 } signature,
            }
                => new ThinkingContent
                {
                    Thinking = text,
                    Signature = signature,
                },
            ReasoningAssistantBlock
            {
                Visibility: ReasoningVisibility.Visible,
                Text: { Length: > 0 } text,
            } => new TextContent { Text = text },
            ReasoningAssistantBlock
            {
                Visibility: ReasoningVisibility.Redacted,
                ReplayToken: { Length: > 0 } data,
            } => new RedactedThinkingContent { Data = data },
            ReasoningAssistantBlock reasoning when !string.IsNullOrWhiteSpace(reasoning.Text)
                => new TextContent { Text = reasoning.Text! },
            ReasoningAssistantBlock => null,
            ToolCallAssistantBlock toolCall => new ToolUseContent
            {
                Id = toolCall.ToolCallId,
                Name = toolCall.ToolName,
                Input = JsonNode.Parse(JsonSerializer.Serialize(toolCall.Arguments)),
            },
            _ => throw new InvalidOperationException(
                $"Unsupported assistant replay block type '{block.GetType().Name}'."
            ),
        };
}
