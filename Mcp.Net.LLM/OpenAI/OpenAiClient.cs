using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using SharedChatToolChoiceKind = Mcp.Net.LLM.Models.ChatToolChoiceKind;

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
    private readonly IOpenAiChatCompletionInvoker _completionInvoker;
    private readonly IChatTranscriptReplayTransformer _replayTransformer;
    private readonly string _modelName;

    public OpenAiChatClient(ChatClientOptions options, ILogger<OpenAiChatClient> logger)
        : this(options, logger, new OpenAiChatCompletionInvoker(), null)
    {
    }

    internal OpenAiChatClient(
        ChatClientOptions options,
        ILogger<OpenAiChatClient> logger,
        IOpenAiChatCompletionInvoker completionInvoker,
        IChatTranscriptReplayTransformer? replayTransformer = null
    )
    {
        _logger = logger;
        _completionInvoker =
            completionInvoker ?? throw new ArgumentNullException(nameof(completionInvoker));
        _replayTransformer = replayTransformer ?? new ChatTranscriptReplayTransformer();

        _modelName = ResolveModelName(options);
        _logger.LogInformation("Using OpenAI model: {Model}", _modelName);
        _chatClient = new OpenAIClient(options.ApiKey).GetChatClient(_modelName);
    }

    private static string ResolveModelName(ChatClientOptions options) =>
        string.IsNullOrWhiteSpace(options.Model) ? "gpt-5" : options.Model;

    private ChatCompletionOptions CreateCompletionOptions(ChatClientRequest request)
    {
        var completionOptions = new ChatCompletionOptions();

        if (request.Options?.Temperature is float temperature && IsTemperatureSupported(_modelName))
        {
            completionOptions.Temperature = temperature;
            _logger.LogDebug(
                "Using temperature {Temperature} for model {Model}",
                temperature,
                _modelName
            );
        }
        else if (request.Options?.Temperature is float)
        {
            _logger.LogDebug(
                "Model {Model} does not support temperature; omitting parameter",
                _modelName
            );
        }

        if (request.Options?.MaxOutputTokens is int maxOutputTokens && maxOutputTokens > 0)
        {
            completionOptions.MaxOutputTokenCount = maxOutputTokens;
            _logger.LogDebug(
                "Using max output tokens {MaxOutputTokens} for model {Model}",
                completionOptions.MaxOutputTokenCount,
                _modelName
            );
        }

        foreach (var tool in request.Tools)
        {
            completionOptions.Tools.Add(ConvertToChatTool(tool));
        }

        if (request.Options?.ToolChoice is { } toolChoice)
        {
            completionOptions.ToolChoice = toolChoice.Kind switch
            {
                SharedChatToolChoiceKind.Auto => global::OpenAI.Chat.ChatToolChoice.CreateAutoChoice(),
                SharedChatToolChoiceKind.None => global::OpenAI.Chat.ChatToolChoice.CreateNoneChoice(),
                SharedChatToolChoiceKind.Required => global::OpenAI.Chat.ChatToolChoice.CreateRequiredChoice(),
                SharedChatToolChoiceKind.Specific => global::OpenAI.Chat.ChatToolChoice.CreateFunctionChoice(
                    toolChoice.ToolName!
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(toolChoice)),
            };
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

    private static string ToStopReason(ChatFinishReason finishReason) =>
        finishReason switch
        {
            ChatFinishReason.Stop => "stop",
            ChatFinishReason.Length => "length",
            ChatFinishReason.ContentFilter => "content_filter",
            ChatFinishReason.ToolCalls => "tool_calls",
            ChatFinishReason.FunctionCall => "function_call",
            _ => finishReason.ToString().ToLowerInvariant(),
        };

    private static ChatUsage? ToChatUsage(ChatTokenUsage? usage)
    {
        if (usage == null)
        {
            return null;
        }

        var additionalCounts = new Dictionary<string, int>();
        AddAdditionalCount(additionalCounts, "audioInputTokens", usage.InputTokenDetails?.AudioTokenCount);
        AddAdditionalCount(
            additionalCounts,
            "cachedInputTokens",
            usage.InputTokenDetails?.CachedTokenCount
        );
        AddAdditionalCount(additionalCounts, "audioOutputTokens", usage.OutputTokenDetails?.AudioTokenCount);
        AddAdditionalCount(
            additionalCounts,
            "reasoningOutputTokens",
            usage.OutputTokenDetails?.ReasoningTokenCount
        );

        return new ChatUsage(
            usage.InputTokenCount,
            usage.OutputTokenCount,
            usage.TotalTokenCount,
            additionalCounts
        );
    }

    private static void AddAdditionalCount(
        IDictionary<string, int> additionalCounts,
        string key,
        int? value
    )
    {
        if (value is > 0)
        {
            additionalCounts[key] = value.Value;
        }
    }

    private static ToolInvocation BuildToolInvocation(ChatToolCall toolCall)
    {
        var arguments = ParseToolArguments(toolCall.FunctionArguments.ToString());
        return new ToolInvocation(toolCall.Id, toolCall.FunctionName, arguments);
    }

    private ChatClientAssistantTurn BuildAssistantTurn(ChatCompletion completion)
    {
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
            blocks,
            ToStopReason(completion.FinishReason),
            ToChatUsage(completion.Usage)
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

    private async Task<ChatClientTurnResult> GetTurnResultAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions completionOptions,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var completion = _completionInvoker.CompleteChat(_chatClient, messages, completionOptions);

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

    public IChatCompletionStream SendAsync(
        ChatClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = BuildMessages(request);
        var completionOptions = CreateCompletionOptions(request);

        return ChatCompletionStream.Create(
            resultCancellationToken =>
                GetTurnResultAsync(messages, completionOptions, resultCancellationToken),
            (writer, streamCancellationToken) =>
                GetStreamingTurnResultAsync(
                    messages,
                    completionOptions,
                    writer,
                    streamCancellationToken
                ),
            cancellationToken
        );
    }

    private IReadOnlyList<ChatMessage> BuildMessages(ChatClientRequest request)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(request.SystemPrompt));
        }

        var replayTranscript = _replayTransformer.Transform(
            request.Transcript,
            new ReplayTarget("openai", _modelName)
        );

        foreach (var entry in replayTranscript.Entries)
        {
            switch (entry)
            {
                case UserChatEntry user:
                    messages.Add(new UserChatMessage(user.Content));
                    break;
                case AssistantChatEntry assistant:
                    AppendAssistantReplayEntry(messages, assistant);
                    break;
                case ToolResultChatEntry toolResult:
                    AppendToolResult(messages, toolResult.Result);
                    break;
            }
        }

        return messages;
    }

    private void AppendAssistantReplayEntry(ICollection<ChatMessage> history, AssistantChatEntry assistant)
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
            history.Add(new AssistantChatMessage(textParts));
            return;
        }

        if (textParts.Count == 0)
        {
            history.Add(new AssistantChatMessage(toolCalls));
            return;
        }

        history.Add(
            new AssistantChatMessage(CreateReplayChatCompletion(assistant, textParts, toolCalls))
        );
    }

    private static void AppendToolResult(
        ICollection<ChatMessage> history,
        ToolInvocationResult? result
    )
    {
        if (result == null)
        {
            return;
        }

        history.Add(new ToolChatMessage(result.ToolCallId, result.ToWireJson()));
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
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions completionOptions,
        ChannelWriter<ChatClientAssistantTurn> assistantTurnUpdates,
        CancellationToken cancellationToken
    )
    {
        var accumulator = new StreamingAssistantTurnAccumulator();
        ChatClientAssistantTurn? lastReportedTurn = null;

        await foreach (var update in _completionInvoker
                           .CompleteChatStreamingAsync(
                               _chatClient,
                               messages,
                               completionOptions,
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

            await assistantTurnUpdates.WriteAsync(partialTurn, cancellationToken);
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

        return finalTurn;
    }

    private static ChatTool ConvertToChatTool(ChatClientTool tool) =>
        ChatTool.CreateFunctionTool(
            functionName: tool.Name,
            functionDescription: tool.Description,
            functionParameters: BinaryData.FromString(tool.InputSchema.GetRawText())
        );

    private sealed class StreamingAssistantTurnAccumulator
    {
        private readonly string _assistantTurnId = Guid.NewGuid().ToString("n");
        private readonly string _textBlockId = Guid.NewGuid().ToString("n");
        private readonly StringBuilder _textBuilder = new();
        private readonly List<StreamingToolCallAccumulator> _toolCalls = new();

        public ChatFinishReason? FinishReason { get; private set; }

        public ChatUsage? Usage { get; private set; }

        public bool HasContent => _textBuilder.Length > 0 || _toolCalls.Count > 0;

        public void Apply(StreamingChatCompletionUpdate update)
        {
            if (update.FinishReason is { } finishReason)
            {
                FinishReason = finishReason;
            }

            if (update.Usage is { } usage)
            {
                Usage = ToChatUsage(usage);
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
                : new ChatClientAssistantTurn(
                    _assistantTurnId,
                    "openai",
                    modelName,
                    blocks,
                    FinishReason is { } finishReason ? ToStopReason(finishReason) : null,
                    Usage
                );
        }

        public ChatClientAssistantTurn BuildFinalTurn(string modelName)
        {
            var blocks = BuildBlocks(lenientToolArguments: false);
            var finishReason = FinishReason ?? InferFinishReason();
            return new ChatClientAssistantTurn(
                _assistantTurnId,
                "openai",
                modelName,
                blocks,
                ToStopReason(finishReason),
                Usage
            );
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
