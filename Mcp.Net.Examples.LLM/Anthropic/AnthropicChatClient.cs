using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Mcp.Net.Examples.LLM.Interfaces;
using Mcp.Net.Examples.LLM.Models;
using Microsoft.Extensions.Logging;
using Tool = Anthropic.SDK.Common.Tool;

namespace Mcp.Net.Examples.LLM.Anthropic;

public class AnthropicChatClient : IChatClient
{
    private readonly AnthropicClient _client;
    private readonly List<Message> _messages = new();
    private readonly List<SystemMessage> _systemMessages = new();
    private readonly List<Tool> _anthropicTools = new();
    private readonly string _model;
    private readonly ILogger<AnthropicChatClient> _logger;

    public AnthropicChatClient(ChatClientOptions options, ILogger<AnthropicChatClient> logger)
    {
        _logger = logger;
        _client = new AnthropicClient(options.ApiKey);
        _model = options.Model.StartsWith("claude") ? options.Model : "claude-3-7-sonnet-20250219";

        _systemMessages.Add(
            new SystemMessage(
                "You are a helpful assistant with access to various tools including calculators "
                    + "and Warhammer 40k themed functions. Use these tools when appropriate."
            )
        );
    }

    public void RegisterTools(IEnumerable<Mcp.Net.Core.Models.Tools.Tool> tools)
    {
        foreach (var tool in tools)
        {
            var anthropicTool = ConvertToAnthropicTool(tool);
            _anthropicTools.Add(anthropicTool);
        }
    }

    private Tool ConvertToAnthropicTool(Mcp.Net.Core.Models.Tools.Tool mcpTool)
    {
        var toolName = mcpTool.Name;
        var toolDescription = mcpTool.Description;
        var toolSchema = JsonNode.Parse(mcpTool.InputSchema.GetRawText());

        var function = new Function(toolName, toolDescription, toolSchema);

        return new Tool(function);
    }

    /// <summary>
    /// Adds a tool result to the message history without triggering an API request
    /// </summary>
    /// <param name="toolCallId">The ID of the tool call this result is for</param>
    /// <param name="toolName">The name of the tool</param>
    /// <param name="results">The results from the tool execution</param>
    public void AddToolResultToHistory(
        string toolCallId,
        string toolName,
        Dictionary<string, object> results
    )
    {
        if (string.IsNullOrEmpty(toolCallId) || results == null)
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
                        ToolUseId = toolCallId,
                        Content = [new TextContent() { Text = JsonSerializer.Serialize(results) }],
                    },
                },
            }
        );
    }

    /// <summary>
    /// Gets a response from Claude based on the current message history
    /// </summary>
    /// <returns>List of LlmResponse objects</returns>
    public async Task<IEnumerable<LlmResponse>> GetLlmResponse()
    {
        var result = new List<LlmResponse>();

        try
        {
            var parameters = new MessageParameters
            {
                Model = _model,
                MaxTokens = 1024,
                Temperature = 1.0m,
                Messages = _messages,
                Tools = _anthropicTools,
            };

            var response = await _client.Messages.GetClaudeMessageAsync(parameters);

            _messages.Add(new Message { Role = RoleType.Assistant, Content = response.Content });

            foreach (var content in response.Content)
            {
                if (content.GetType() == typeof(ToolUseContent))
                {
                    var toolUseContent = (ToolUseContent)content;
                    var toolCalls = ExtractToolCalls(toolUseContent);
                    var llmResponse = new LlmResponse
                    {
                        Content = "",
                        ToolCalls = toolCalls,
                        Type = MessageType.Tool,
                    };
                    result.Add(llmResponse);
                }
                else
                {
                    var textContent = (TextContent)content;
                    var llmResponse = new LlmResponse
                    {
                        Content = textContent.Text,
                        Type = MessageType.Assistant,
                    };
                    result.Add(llmResponse);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling Claude API: {ex.Message}");
            return new List<LlmResponse>
            {
                new LlmResponse { Content = $"Error: {ex.Message}", Type = MessageType.System },
            };
        }
    }

    private List<Models.ToolCall> ExtractToolCalls(ToolUseContent toolUseContent)
    {
        var toolCalls = new List<Models.ToolCall>();

        if (toolUseContent.Type == ContentType.tool_use)
        {
            var toolArguments = new Dictionary<string, object>();

            // Parse the tool input
            if (toolUseContent != null && toolUseContent.Input != null)
            {
                // If Input is JsonNode, we need to handle it differently
                if (toolUseContent.Input is JsonObject jsonObject)
                {
                    foreach (var property in jsonObject)
                    {
                        string propertyName = property.Key;
                        JsonNode? propertyValue = property.Value;

                        object value;
                        if (propertyValue == null)
                        {
                            value = string.Empty;
                        }
                        else if (propertyValue is JsonValue jsonValue)
                        {
                            // Handle different value types
                            if (jsonValue.TryGetValue<double>(out var numberValue))
                            {
                                value = numberValue;
                            }
                            else if (jsonValue.TryGetValue<bool>(out var boolValue))
                            {
                                value = boolValue;
                            }
                            else
                            {
                                // Default to string
                                value = jsonValue.ToString() ?? string.Empty;
                            }
                        }
                        else if (propertyValue is JsonObject || propertyValue is JsonArray)
                        {
                            // Convert complex objects to string
                            value = propertyValue.ToJsonString();
                        }
                        else
                        {
                            value = propertyValue.ToString() ?? string.Empty;
                        }

                        toolArguments[propertyName] = value;
                    }
                }
            }

            if (toolUseContent?.Id != null && toolUseContent?.Name != null)
            {
                toolCalls.Add(
                    new Models.ToolCall
                    {
                        Id = toolUseContent.Id,
                        Name = toolUseContent.Name,
                        Arguments = toolArguments,
                    }
                );
            }
        }

        return toolCalls;
    }

    private void AddMessageToHistory(LlmMessage message)
    {
        switch (message.Type)
        {
            case MessageType.User:
                _messages.Add(
                    new Message
                    {
                        Role = RoleType.User,
                        Content = new List<ContentBase>
                        {
                            new TextContent() { Text = message.Content },
                        },
                    }
                );
                break;

            case MessageType.Tool:
                if (message.ToolCallId != null && message.ToolResults != null)
                {
                    // Convert tool results to JSON
                    string resultJson = JsonSerializer.Serialize(message.ToolResults);

                    // Add tool result to history
                    _messages.Add(
                        new Message
                        {
                            Role = RoleType.User,
                            Content = new List<ContentBase>
                            {
                                new ToolResultContent
                                {
                                    ToolUseId = message.ToolCallId,
                                    Content = [new TextContent() { Text = resultJson }],
                                },
                            },
                        }
                    );
                }
                break;

            case MessageType.System:
                // We don't add system messages in the middle of a conversation
                break;
        }
    }

    /// <summary>
    /// Called when a user posts their message to LLM (In this case Claude)
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public async Task<IEnumerable<LlmResponse>> SendMessageAsync(LlmMessage message)
    {
        AddMessageToHistory(message);
        return await GetLlmResponse();
    }

    /// <summary>
    /// Adds Tool Results to the History and then Gets a new Response from the LLM
    /// </summary>
    /// <param name="toolResults"></param>
    /// <returns></returns>
    public async Task<IEnumerable<LlmResponse>> SendToolResultsAsync(
        IEnumerable<Models.ToolCall> toolResults
    )
    {
        foreach (var toolResult in toolResults)
        {
            _logger.LogDebug(
                "Adding tool result for {ToolName} with ID {ToolId} to history",
                toolResult.Name,
                toolResult.Id
            );

            AddToolResultToHistory(toolResult.Id, toolResult.Name, toolResult.Results);
        }

        _logger.LogDebug("Making single API call after adding all tool results");
        var nextResponses = await GetLlmResponse();

        return nextResponses;
    }
}
