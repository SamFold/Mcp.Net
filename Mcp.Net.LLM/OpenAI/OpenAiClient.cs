using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Mcp.Net.LLM.OpenAI;

/// <summary>
/// Adapter that translates between the MCP-friendly chat/session model and the OpenAI Chat API.
/// Maintains the message history, forwards prompts/tool results, and normalises tool call payloads.
/// </summary>
public sealed class OpenAiChatClient : IChatClient
{
    private readonly ILogger<OpenAiChatClient> _logger;
    private readonly OpenAIClient _client;
    private readonly ChatClient _chatClient;
    private readonly ChatCompletionOptions _completionOptions;
    private readonly List<ChatMessage> _history = new();
    private string _systemPrompt =
        "You are a helpful assistant with access to various tools including calculators "
        + "and Warhammer 40k themed functions. Use these tools when appropriate.";

    public OpenAiChatClient(ChatClientOptions options, ILogger<OpenAiChatClient> logger)
    {
        _logger = logger;
        _client = new OpenAIClient(options.ApiKey);

        var modelName = ResolveModelName(options);
        _logger.LogInformation("Using OpenAI model: {Model}", modelName);
        _chatClient = _client.GetChatClient(modelName);

        _completionOptions = BuildCompletionOptions(modelName, options);
        _history.Add(new SystemChatMessage(_systemPrompt));
    }

    private static string ResolveModelName(ChatClientOptions options) =>
        string.IsNullOrWhiteSpace(options.Model) ? "gpt-5" : options.Model;

    private ChatCompletionOptions BuildCompletionOptions(
        string modelName,
        ChatClientOptions options
    )
    {
        var completionOptions = new ChatCompletionOptions();

        if (IsTemperatureSupported(modelName))
        {
            completionOptions.Temperature = options.Temperature;
            _logger.LogDebug(
                "Using temperature {Temperature} for model {Model}",
                options.Temperature,
                modelName
            );
        }
        else
        {
            _logger.LogDebug(
                "Model {Model} does not support temperature; omitting parameter",
                modelName
            );
        }

        return completionOptions;
    }

    private static readonly string[] TemperatureUnsupportedPrefixes =
    {
        "o",
        "gpt-5",
        "gpt-4.1",
        "gpt-4o-audio",
        "gpt-4o-mini-audio",
    };

    private static bool IsTemperatureSupported(string modelName)
    {
        foreach (var prefix in TemperatureUnsupportedPrefixes)
        {
            if (modelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static ToolInvocation BuildToolInvocation(ChatToolCall toolCall)
    {
        var arguments = ParseToolArguments(toolCall.FunctionArguments.ToString());
        return new ToolInvocation(toolCall.Id, toolCall.FunctionName, arguments);
    }

    public void RegisterTools(IEnumerable<Tool> tools)
    {
        foreach (var tool in tools)
        {
            var chatTool = ToolConverter.ConvertToChatTool(tool);
            _completionOptions.Tools.Add(chatTool);
        }
    }

    private ChatMessage ConvertToChatMessage(LlmMessage message) =>
        message.Type switch
        {
            MessageType.System => new SystemChatMessage(message.Content),
            MessageType.User => new UserChatMessage(message.Content),
            MessageType.Tool when message.ToolResult != null
                => BuildToolChatMessage(message.ToolResult),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Type), message.Type, null),
        };

    private ToolChatMessage BuildToolChatMessage(ToolInvocationResult result) =>
        new(result.ToolCallId, result.ToWireJson());

    private IEnumerable<LlmResponse> HandleTextResponse(ChatCompletion completion)
    {
        _history.Add(new AssistantChatMessage(completion));
        var responseText = completion.Content?.FirstOrDefault()?.Text ?? "No content available";
        return new[]
        {
            new LlmResponse
            {
                Content = responseText,
                Type = MessageType.Assistant,
            },
        };
    }

    private IEnumerable<LlmResponse> HandleToolCallResponse(ChatCompletion completion)
    {
        _history.Add(new AssistantChatMessage(completion));

        var invocations = completion.ToolCalls
            .Select(BuildToolInvocation)
            .ToList();

        return new[]
        {
            new LlmResponse
            {
                Content = string.Empty,
                ToolCalls = invocations,
                Type = MessageType.Tool,
            },
        };
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(string argumentsJson)
    {
        var result = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(argumentsJson);

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => property.Value.GetString() ?? string.Empty,
            };
        }

        return result;
    }

    private void AppendToolResult(ToolInvocationResult? result)
    {
        if (result == null)
        {
            return;
        }

        _history.Add(new ToolChatMessage(result.ToolCallId, result.ToWireJson()));
    }

    /// <summary>
    /// Gets a response from OpenAI based on the current message history
    /// </summary>
    /// <returns>List of LlmResponse objects</returns>
    public Task<IEnumerable<LlmResponse>> GetLlmResponse()
    {
        try
        {
            var completionResult = _chatClient.CompleteChat(_history, _completionOptions);
            var completion = completionResult.Value;

            // Handle different response types
            IEnumerable<LlmResponse> response = completion.FinishReason switch
            {
                ChatFinishReason.Stop => HandleTextResponse(completion),
                ChatFinishReason.ToolCalls => HandleToolCallResponse(completion),
                _ => [new() { Content = $"Unexpected response: {completion.FinishReason}" }],
            };

        return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API: {Message}", ex.Message);
            return Task.FromResult<IEnumerable<LlmResponse>>(
                [
                    new()
                    {
                        Content = $"Error communicating with OpenAI: {ex.Message}",
                        Type = Models.MessageType.System,
                    },
                ]
            );
        }
    }

    public Task<IEnumerable<LlmResponse>> SendMessageAsync(LlmMessage message)
    {
        // Handle tool responses differently - just add to history without making API call yet
        if (message.Type == MessageType.Tool)
        {
            AppendToolResult(message.ToolResult);
            return Task.FromResult(Enumerable.Empty<LlmResponse>());
        }

        // Standard message handling for non-tool messages
        // Convert our message to OpenAI format and add to history
        var chatMessage = ConvertToChatMessage(message);
        _history.Add(chatMessage);
        _logger.LogDebug($"User message added to history (OpenAI): {message.Content}");

        return GetLlmResponse();
    }

    public async Task<IEnumerable<LlmResponse>> SendToolResultsAsync(
        IEnumerable<ToolInvocationResult> toolResults
    )
    {
        foreach (var toolResult in toolResults)
        {
            _logger.LogDebug(
                "Adding tool result for {ToolName} with ID {ToolId} to history.",
                toolResult.ToolName,
                toolResult.ToolCallId
            );

            AppendToolResult(toolResult);
        }

        _logger.LogDebug("Making single API call after adding all tool results");
        var nextResponses = await GetLlmResponse();

        return nextResponses;
    }

    public string GetSystemPrompt() => _systemPrompt;

    /// <summary>
    /// Sets or updates the system prompt for the chat session
    /// </summary>
    public void SetSystemPrompt(string systemPrompt)
    {
        _logger.LogInformation("Setting system prompt for OpenAI chat client");
        _systemPrompt = systemPrompt;

        var existingSystemMessage = _history.FirstOrDefault(m => m is SystemChatMessage);
        if (existingSystemMessage != null)
        {
            int index = _history.IndexOf(existingSystemMessage);
            _history[index] = new SystemChatMessage(systemPrompt);
        }
        else
        {
            _history.Insert(0, new SystemChatMessage(systemPrompt));
        }
    }
}
