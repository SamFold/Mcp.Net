using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Mcp.Net.LLM.OpenAI;

internal interface IOpenAiChatCompletionInvoker
{
    ChatCompletion CompleteChat(
        ChatClient client,
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options
    );

    IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
        ChatClient client,
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken
    );
}

internal sealed class OpenAiChatCompletionInvoker : IOpenAiChatCompletionInvoker
{
    public ChatCompletion CompleteChat(
        ChatClient client,
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options
    )
    {
        return client.CompleteChat(messages, options).Value;
    }

    public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
        ChatClient client,
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken
    )
    {
        return client.CompleteChatStreamingAsync(messages, options, cancellationToken);
    }
}

/// <summary>
/// Adapter that translates between the MCP-friendly chat/session model and the OpenAI Chat API.
/// Maintains the message history, forwards prompts/tool results, and normalises tool call payloads.
/// </summary>
public sealed class OpenAiChatClient : IChatClient
{
    private readonly ILogger<OpenAiChatClient> _logger;
    private readonly ChatClient _chatClient;
    private readonly ChatCompletionOptions _completionOptions;
    private readonly IOpenAiChatCompletionInvoker _completionInvoker;
    private readonly List<ChatMessage> _history = new();
    private readonly string _modelName;
    private string _systemPrompt =
        "You are a helpful assistant with access to various tools including calculators "
        + "and Warhammer 40k themed functions. Use these tools when appropriate.";

    public OpenAiChatClient(ChatClientOptions options, ILogger<OpenAiChatClient> logger)
        : this(options, logger, new OpenAiChatCompletionInvoker())
    {
    }

    internal OpenAiChatClient(
        ChatClientOptions options,
        ILogger<OpenAiChatClient> logger,
        IOpenAiChatCompletionInvoker completionInvoker
    )
    {
        _logger = logger;
        _completionInvoker =
            completionInvoker ?? throw new ArgumentNullException(nameof(completionInvoker));
        _systemPrompt = string.IsNullOrWhiteSpace(options.SystemPrompt)
            ? _systemPrompt
            : options.SystemPrompt;

        _modelName = ResolveModelName(options);
        _logger.LogInformation("Using OpenAI model: {Model}", _modelName);
        _chatClient = new OpenAIClient(options.ApiKey).GetChatClient(_modelName);

        _completionOptions = BuildCompletionOptions(_modelName, options);
        InitializeHistory();
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

    private ToolChatMessage BuildToolChatMessage(ToolInvocationResult result) =>
        new(result.ToolCallId, result.ToWireJson());

    private ChatClientAssistantTurn BuildAssistantTurn(ChatCompletion completion)
    {
        _history.Add(new AssistantChatMessage(completion));

        var blocks = new List<AssistantContentBlock>();
        var responseText = completion.Content?.FirstOrDefault()?.Text;
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            blocks.Add(new TextAssistantBlock(Guid.NewGuid().ToString("n"), responseText));
        }

        foreach (var toolCall in completion.ToolCalls)
        {
            var invocation = BuildToolInvocation(toolCall);
            blocks.Add(
                new ToolCallAssistantBlock(
                    Guid.NewGuid().ToString("n"),
                    invocation.Id,
                    invocation.Name,
                    invocation.Arguments
                )
            );
        }

        return new ChatClientAssistantTurn(
            Guid.NewGuid().ToString("n"),
            "openai",
            _modelName,
            blocks
        );
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(string argumentsJson)
    {
        var result = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(argumentsJson);

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            result[property.Name] = ConvertJsonValue(property.Value);
        }

        return result;
    }

    private static object? ConvertJsonValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => value.Clone(),
            JsonValueKind.Array => value.Clone(),
            _ => value.GetString() ?? string.Empty,
        };

    private void AppendToolResult(ToolInvocationResult? result)
    {
        if (result == null)
        {
            return;
        }

        _history.Add(new ToolChatMessage(result.ToolCallId, result.ToWireJson()));
    }

    private void InitializeHistory()
    {
        _history.Clear();
        _history.Add(new SystemChatMessage(_systemPrompt));
    }

    public void ResetConversation()
    {
        _logger.LogInformation("Resetting conversation history for OpenAI chat client");
        InitializeHistory();
    }

    private async Task<ChatClientTurnResult> GetTurnResultAsync(
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (assistantTurnUpdates != null)
            {
                return await GetStreamingTurnResultAsync(assistantTurnUpdates, cancellationToken);
            }

            var completion = _completionInvoker.CompleteChat(_chatClient, _history, _completionOptions);

            ChatClientTurnResult response = completion.FinishReason switch
            {
                ChatFinishReason.Stop => BuildAssistantTurn(completion),
                ChatFinishReason.ToolCalls => BuildAssistantTurn(completion),
                _ => new ChatClientFailure(
                    ChatErrorSource.Provider,
                    $"Unexpected response: {completion.FinishReason}",
                    Provider: "openai",
                    Model: _modelName
                ),
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API: {Message}", ex.Message);
            return new ChatClientFailure(
                ChatErrorSource.Provider,
                $"Error communicating with OpenAI: {ex.Message}",
                Details: ex.ToString(),
                Provider: "openai",
                Model: _modelName
            );
        }
    }

    public Task<ChatClientTurnResult> SendMessageAsync(
        string userMessage,
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    )
    {
        _ = assistantTurnUpdates;
        cancellationToken.ThrowIfCancellationRequested();

        var chatMessage = new UserChatMessage(userMessage);
        _history.Add(chatMessage);
        _logger.LogDebug("User message added to history (OpenAI): {Message}", userMessage);

        return GetTurnResultAsync(assistantTurnUpdates, cancellationToken);
    }

    public async Task<ChatClientTurnResult> SendToolResultsAsync(
        IEnumerable<ToolInvocationResult> toolResults,
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        return await GetTurnResultAsync(assistantTurnUpdates, cancellationToken);
    }

    public string GetSystemPrompt() => _systemPrompt;

    public ReplayTarget GetReplayTarget() => new("openai", _modelName);

    public void LoadReplayTranscript(ProviderReplayTranscript replayTranscript)
    {
        ArgumentNullException.ThrowIfNull(replayTranscript);

        InitializeHistory();

        foreach (var entry in replayTranscript.Entries)
        {
            switch (entry)
            {
                case UserChatEntry user:
                    _history.Add(new UserChatMessage(user.Content));
                    break;
                case AssistantChatEntry assistant:
                    AppendAssistantReplayEntry(assistant);
                    break;
                case ToolResultChatEntry toolResult:
                    AppendToolResult(toolResult.Result);
                    break;
            }
        }
    }

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

    private void AppendAssistantReplayEntry(AssistantChatEntry assistant)
    {
        var textParts = assistant
            .Blocks.SelectMany(ToReplayTextParts)
            .ToList();

        var toolCalls = assistant
            .Blocks.OfType<ToolCallAssistantBlock>()
            .Select(block =>
                ChatToolCall.CreateFunctionToolCall(
                    block.ToolCallId,
                    block.ToolName,
                    BinaryData.FromString(JsonSerializer.Serialize(block.Arguments))
                )
            )
            .ToList();

        if (textParts.Count == 0 && toolCalls.Count == 0)
        {
            return;
        }

        if (toolCalls.Count == 0)
        {
            _history.Add(new AssistantChatMessage(textParts));
            return;
        }

        if (textParts.Count == 0)
        {
            _history.Add(new AssistantChatMessage(toolCalls));
            return;
        }

        _history.Add(
            new AssistantChatMessage(CreateReplayChatCompletion(assistant, textParts, toolCalls))
        );
    }

    private static IEnumerable<ChatMessageContentPart> ToReplayTextParts(AssistantContentBlock block)
    {
        switch (block)
        {
            case TextAssistantBlock text:
                yield return ChatMessageContentPart.CreateTextPart(text.Text);
                yield break;

            case ReasoningAssistantBlock reasoning when !string.IsNullOrWhiteSpace(reasoning.Text):
                yield return ChatMessageContentPart.CreateTextPart(reasoning.Text!);
                yield break;
        }
    }

    private ChatCompletion CreateReplayChatCompletion(
        AssistantChatEntry assistant,
        IReadOnlyList<ChatMessageContentPart> textParts,
        IReadOnlyList<ChatToolCall> toolCalls
    ) =>
        OpenAIChatModelFactory.ChatCompletion(
            assistant.Id,
            ChatFinishReason.ToolCalls,
            new ChatMessageContent(textParts),
            refusal: null,
            toolCalls,
            ChatMessageRole.Assistant,
            functionCall: null,
            Array.Empty<ChatTokenLogProbabilityDetails>(),
            Array.Empty<ChatTokenLogProbabilityDetails>(),
            assistant.Timestamp,
            assistant.Model ?? _modelName,
            systemFingerprint: null,
            usage: null!
        );

    private async Task<ChatClientTurnResult> GetStreamingTurnResultAsync(
        IProgress<ChatClientAssistantTurn> assistantTurnUpdates,
        CancellationToken cancellationToken
    )
    {
        var accumulator = new StreamingAssistantTurnAccumulator();
        ChatClientAssistantTurn? lastReportedTurn = null;

        await foreach (var update in _completionInvoker
                           .CompleteChatStreamingAsync(
                               _chatClient,
                               _history,
                               _completionOptions,
                               cancellationToken
                           )
                           .WithCancellation(cancellationToken))
        {
            accumulator.Apply(update);

            var partialTurn = accumulator.BuildPartialTurn(_modelName);
            if (partialTurn == null || Equals(partialTurn, lastReportedTurn))
            {
                continue;
            }

            assistantTurnUpdates.Report(partialTurn);
            lastReportedTurn = partialTurn;
        }

        if (!accumulator.HasContent)
        {
            return new ChatClientFailure(
                ChatErrorSource.Provider,
                "Unexpected streaming response: no assistant content",
                Provider: "openai",
                Model: _modelName
            );
        }

        var finishReason = accumulator.FinishReason ?? accumulator.InferFinishReason();
        if (finishReason != ChatFinishReason.Stop && finishReason != ChatFinishReason.ToolCalls)
        {
            return new ChatClientFailure(
                ChatErrorSource.Provider,
                $"Unexpected response: {finishReason}",
                Provider: "openai",
                Model: _modelName
            );
        }

        var finalTurn = accumulator.BuildFinalTurn(_modelName);

        AppendAssistantReplayEntry(
            new AssistantChatEntry(
                finalTurn.Id,
                DateTimeOffset.UtcNow,
                finalTurn.Blocks,
                Provider: "openai",
                Model: _modelName
            )
        );

        return finalTurn;
    }

    private sealed class StreamingAssistantTurnAccumulator
    {
        private readonly string _assistantTurnId = Guid.NewGuid().ToString("n");
        private readonly string _textBlockId = Guid.NewGuid().ToString("n");
        private readonly StringBuilder _textBuilder = new();
        private readonly List<StreamingToolCallAccumulator> _toolCalls = new();

        public ChatFinishReason? FinishReason { get; private set; }

        public bool HasContent => _textBuilder.Length > 0 || _toolCalls.Count > 0;

        public void Apply(StreamingChatCompletionUpdate update)
        {
            if (update.FinishReason is { } finishReason)
            {
                FinishReason = finishReason;
            }

            foreach (var contentPart in update.ContentUpdate)
            {
                if (!string.IsNullOrWhiteSpace(contentPart.Text))
                {
                    _textBuilder.Append(contentPart.Text);
                }
            }

            foreach (var toolCallUpdate in update.ToolCallUpdates)
            {
                FindOrCreateToolCall(toolCallUpdate).Apply(toolCallUpdate);
            }
        }

        public ChatFinishReason InferFinishReason() =>
            _toolCalls.Count > 0 ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop;

        public ChatClientAssistantTurn? BuildPartialTurn(string modelName)
        {
            var blocks = BuildBlocks(lenientToolArguments: true);
            return blocks.Count == 0
                ? null
                : new ChatClientAssistantTurn(_assistantTurnId, "openai", modelName, blocks);
        }

        public ChatClientAssistantTurn BuildFinalTurn(string modelName)
        {
            var blocks = BuildBlocks(lenientToolArguments: false);
            return new ChatClientAssistantTurn(_assistantTurnId, "openai", modelName, blocks);
        }

        private List<AssistantContentBlock> BuildBlocks(bool lenientToolArguments)
        {
            var blocks = new List<AssistantContentBlock>();

            if (_textBuilder.Length > 0)
            {
                blocks.Add(new TextAssistantBlock(_textBlockId, _textBuilder.ToString()));
            }

            foreach (var toolCall in _toolCalls)
            {
                var block = toolCall.BuildBlock(lenientToolArguments);
                if (block != null)
                {
                    blocks.Add(block);
                }
            }

            return blocks;
        }

        private StreamingToolCallAccumulator FindOrCreateToolCall(
            StreamingChatToolCallUpdate update
        )
        {
            if (!string.IsNullOrWhiteSpace(update.ToolCallId))
            {
                var existing = _toolCalls.FirstOrDefault(call => call.ToolCallId == update.ToolCallId);
                if (existing != null)
                {
                    return existing;
                }
            }

            var created = new StreamingToolCallAccumulator();
            _toolCalls.Add(created);
            return created;
        }
    }

    private sealed class StreamingToolCallAccumulator
    {
        private readonly string _blockId = Guid.NewGuid().ToString("n");
        private readonly StringBuilder _argumentsBuilder = new();

        public string? ToolCallId { get; private set; }

        public string? FunctionName { get; private set; }

        public void Apply(StreamingChatToolCallUpdate update)
        {
            if (!string.IsNullOrWhiteSpace(update.ToolCallId))
            {
                ToolCallId = update.ToolCallId;
            }

            if (!string.IsNullOrWhiteSpace(update.FunctionName))
            {
                FunctionName = update.FunctionName;
            }

            var argumentsUpdate = update.FunctionArgumentsUpdate?.ToString();
            if (!string.IsNullOrEmpty(argumentsUpdate))
            {
                _argumentsBuilder.Append(argumentsUpdate);
            }
        }

        public ToolCallAssistantBlock? BuildBlock(bool lenientToolArguments)
        {
            if (string.IsNullOrWhiteSpace(ToolCallId) || string.IsNullOrWhiteSpace(FunctionName))
            {
                return null;
            }

            var argumentsText = _argumentsBuilder.ToString();
            IReadOnlyDictionary<string, object?> arguments;
            if (string.IsNullOrWhiteSpace(argumentsText))
            {
                arguments = new Dictionary<string, object?>();
            }
            else if (TryParseToolArguments(argumentsText, out var parsedArguments))
            {
                arguments = parsedArguments;
            }
            else if (lenientToolArguments)
            {
                arguments = new Dictionary<string, object?>();
            }
            else
            {
                arguments = ParseToolArguments(argumentsText);
            }

            return new ToolCallAssistantBlock(_blockId, ToolCallId, FunctionName, arguments);
        }
    }

    private static bool TryParseToolArguments(
        string argumentsJson,
        out IReadOnlyDictionary<string, object?> arguments
    )
    {
        try
        {
            arguments = ParseToolArguments(argumentsJson);
            return true;
        }
        catch (JsonException)
        {
            arguments = Array.Empty<KeyValuePair<string, object?>>()
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            return false;
        }
    }
}
