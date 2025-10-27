using System.Runtime.CompilerServices;
using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Mcp.Net.LLM.OpenAI;

public class OpenAiChatClient : IChatClient
{
    private readonly ILogger<OpenAiChatClient> _logger;
    private readonly OpenAIClient _client;
    private readonly ChatClient _chatClient;
    private readonly ChatCompletionOptions _options;
    private readonly List<ChatMessage> _history = [];
    private string _systemPrompt =
        "You are a helpful assistant with access to various tools including calculators "
        + "and Warhammer 40k themed functions. Use these tools when appropriate.";

    public OpenAiChatClient(ChatClientOptions options, ILogger<OpenAiChatClient> logger)
    {
        _logger = logger;
        _client = new OpenAIClient(options.ApiKey);

        // Use a default model if none specified
        var modelName = !string.IsNullOrEmpty(options.Model) ? options.Model : "gpt-4o";

        _logger.LogInformation("Using OpenAI model: {Model}", modelName);
        _chatClient = _client.GetChatClient(modelName);

        // Create completion options with appropriate parameters based on model
        _options = new ChatCompletionOptions();

        // OpenAI's "o" series models (o1, o3-mini, etc.) don't support temperature
        if (modelName.StartsWith("o"))
        {
            _logger.LogDebug(
                "Model {Model} is an 'o' series model and doesn't support temperature - omitting parameter",
                modelName
            );
        }
        else
        {
            _options.Temperature = options.Temperature;
            _logger.LogDebug(
                "Using temperature {Temperature} for model {Model}",
                options.Temperature,
                modelName
            );
        }

        _history.Add(new SystemChatMessage(_systemPrompt));
    }

    public void RegisterTools(IEnumerable<Tool> tools)
    {
        foreach (var tool in tools)
        {
            var chatTool = ToolConverter.ConvertToChatTool(tool);
            _options.Tools.Add(chatTool);
        }
    }

    private List<LlmResponse> HandleTextResponse(ChatCompletion completion)
    {
        _history.Add(new AssistantChatMessage(completion));

        string responseText = "No content available";
        if (completion.Content?.Count > 0 && completion.Content[0].Text != null)
        {
            responseText = completion.Content[0].Text;
        }

        return [new() { Content = responseText, Type = MessageType.Assistant }];
    }

    private List<LlmResponse> HandleToolCallResponse(ChatCompletion completion)
    {
        _history.Add(new AssistantChatMessage(completion));

        var invocations = completion
            .ToolCalls
            .Select(tc =>
            {
                var arguments = ParseToolArguments(tc.FunctionArguments.ToString());
                return new ToolInvocation(tc.Id, tc.FunctionName, arguments);
            })
            .ToList();

        var response = new LlmResponse
        {
            Content = "",
            ToolCalls = invocations,
            Type = MessageType.Tool,
        };

        return [response];
    }

    private Dictionary<string, object?> ParseToolArguments(string argumentsJson)
    {
        var result = new Dictionary<string, object?>();
        var doc = JsonDocument.Parse(argumentsJson);

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    result[property.Name] = property.Value.GetDouble();
                    break;
                case JsonValueKind.True:
                    result[property.Name] = true;
                    break;
                case JsonValueKind.False:
                    result[property.Name] = false;
                    break;
                default:
                    result[property.Name] = property.Value.GetString() ?? string.Empty;
                    break;
            }
        }

        return result;
    }

    private ChatMessage ConvertToChatMessage(LlmMessage message) =>
        message.Type switch
        {
            MessageType.System => new SystemChatMessage(message.Content),
            MessageType.User => new UserChatMessage(message.Content),
            MessageType.Tool when message.ToolResult != null => new ToolChatMessage(
                message.ToolResult.ToolCallId,
                message.ToolResult.ToWireJson()
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Type)),
        };

    /// <summary>
    /// Gets a response from OpenAI based on the current message history
    /// </summary>
    /// <returns>List of LlmResponse objects</returns>
    public Task<IEnumerable<LlmResponse>> GetLlmResponse()
    {
        try
        {
            var completionResult = _chatClient.CompleteChat(_history, _options);
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
            if (message.ToolResult != null)
            {
                _history.Add(
                    new ToolChatMessage(
                        message.ToolResult.ToolCallId,
                        message.ToolResult.ToWireJson()
                    )
                );
            }

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

            await SendMessageAsync(
                new LlmMessage
                {
                    Type = MessageType.Tool,
                    ToolCallId = toolResult.ToolCallId,
                    ToolName = toolResult.ToolName,
                    ToolResult = toolResult,
                }
            );
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
