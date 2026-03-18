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
using OpenAI.Responses;
using SharedChatToolChoiceKind = Mcp.Net.LLM.Models.ChatToolChoiceKind;

namespace Mcp.Net.LLM.OpenAI;

#pragma warning disable OPENAI001
#pragma warning disable SCME0001

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
    private readonly ResponsesClient _responsesClient;
    private readonly IOpenAiChatCompletionInvoker _completionInvoker;
    private readonly IOpenAiResponsesInvoker _responsesInvoker;
    private readonly IChatTranscriptReplayTransformer _replayTransformer;
    private readonly string _modelName;

    public OpenAiChatClient(ChatClientOptions options, ILogger<OpenAiChatClient> logger)
        : this(options, logger, new OpenAiChatCompletionInvoker(), null, null)
    {
    }

    internal OpenAiChatClient(
        ChatClientOptions options,
        ILogger<OpenAiChatClient> logger,
        IOpenAiChatCompletionInvoker completionInvoker,
        IOpenAiResponsesInvoker? responsesInvoker = null,
        IChatTranscriptReplayTransformer? replayTransformer = null
    )
    {
        _logger = logger;
        _completionInvoker =
            completionInvoker ?? throw new ArgumentNullException(nameof(completionInvoker));
        _responsesInvoker = responsesInvoker ?? new OpenAiResponsesInvoker();
        _replayTransformer = replayTransformer ?? new ChatTranscriptReplayTransformer();

        _modelName = ResolveModelName(options);
        _logger.LogInformation("Using OpenAI model: {Model}", _modelName);
        var openAiClient = new OpenAIClient(options.ApiKey);
        _chatClient = openAiClient.GetChatClient(_modelName);
        _responsesClient = openAiClient.GetResponsesClient();
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

    private static ChatUsage? ToChatUsage(ResponseTokenUsage? usage)
    {
        if (usage == null)
        {
            return null;
        }

        var additionalCounts = new Dictionary<string, int>();
        AddAdditionalCount(additionalCounts, "cachedInputTokens", usage.InputTokenDetails?.CachedTokenCount);
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
        var arguments = ParseToolArguments(toolCall.FunctionArguments.ToMemory());
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

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(string argumentsJson) =>
        ParseToolArguments(Encoding.UTF8.GetBytes(argumentsJson));

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(ReadOnlyMemory<byte> argumentsJson)
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

    private async Task<ChatClientTurnResult> GetImageGenerationTurnResultAsync(
        ChatClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !TryCreateImageGenerationRequest(
                request,
                out var responseOptions,
                out var validationFailure
            )
        )
        {
            return validationFailure!;
        }

        try
        {
            var response = await _responsesInvoker.CreateResponseAsync(
                _responsesClient,
                responseOptions!,
                cancellationToken
            );

            return BuildImageGenerationAssistantTurn(response, request.Options!.ImageGeneration!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI Responses API: {Message}", ex.Message);
            return new ChatClientFailure(
                ChatErrorSource.Provider,
                $"Error communicating with OpenAI: {ex.Message}",
                Details: ex.ToString(),
                Provider: "openai",
                Model: _modelName
            );
        }
    }

    private async Task<ChatClientTurnResult> GetStreamingImageGenerationTurnResultAsync(
        ChatClientRequest request,
        ChannelWriter<ChatClientAssistantTurn> assistantTurnUpdates,
        CancellationToken cancellationToken
    )
    {
        var result = await GetImageGenerationTurnResultAsync(request, cancellationToken);

        if (result is ChatClientAssistantTurn assistantTurn)
        {
            await assistantTurnUpdates.WriteAsync(assistantTurn, cancellationToken);
        }

        return result;
    }

    private bool TryCreateImageGenerationRequest(
        ChatClientRequest request,
        out CreateResponseOptions? responseOptions,
        out ChatClientFailure? validationFailure
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        responseOptions = null;
        validationFailure = null;

        if (request.Options?.ImageGeneration == null)
        {
            throw new InvalidOperationException("Image generation options are required.");
        }

        var imageGeneration = request.Options.ImageGeneration;

        if (request.Tools.Count > 0)
        {
            validationFailure = CreateImageGenerationFailure(
                "OpenAI image generation does not yet support tools in the same request."
            );
            return false;
        }

        var inputItems = new List<ResponseItem>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            inputItems.Add(ResponseItem.CreateSystemMessageItem(request.SystemPrompt));
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
                    if (
                        !TryCreateImageGenerationUserMessage(
                            user,
                            out var userMessage,
                            out var errorMessage
                        )
                    )
                    {
                        validationFailure = CreateImageGenerationFailure(errorMessage!);
                        return false;
                    }

                    inputItems.Add(userMessage!);
                    break;

                case AssistantChatEntry assistant:
                    if (
                        !TryCreateImageGenerationAssistantMessage(
                            assistant,
                            out var assistantMessage,
                            out errorMessage
                        )
                    )
                    {
                        validationFailure = CreateImageGenerationFailure(errorMessage!);
                        return false;
                    }

                    if (assistantMessage != null)
                    {
                        inputItems.Add(assistantMessage);
                    }

                    break;

                case ToolResultChatEntry:
                    validationFailure = CreateImageGenerationFailure(
                        "OpenAI image generation does not yet support replaying tool results."
                    );
                    return false;
            }
        }

        responseOptions = new CreateResponseOptions(_modelName, inputItems)
        {
            MaxOutputTokenCount = request.Options?.MaxOutputTokens,
        };

        if (request.Options?.Temperature is float temperature && IsTemperatureSupported(_modelName))
        {
            responseOptions.Temperature = temperature;
        }

        responseOptions.Tools.Add(
            ResponseTool.CreateImageGenerationTool(
                model: imageGeneration.Model ?? "gpt-image-1.5",
                outputFileFormat: ToOpenAiOutputFileFormat(imageGeneration.OutputFormat)
            )
        );

        return true;
    }

    private static bool TryCreateImageGenerationUserMessage(
        UserChatEntry user,
        out MessageResponseItem? userMessage,
        out string? errorMessage
    )
    {
        if (user.ContentParts.OfType<InlineImageUserContentPart>().Any())
        {
            userMessage = null;
            errorMessage =
                "OpenAI image generation does not yet support inline image input on the Responses route.";
            return false;
        }

        var contentParts = user.ContentParts.OfType<TextUserContentPart>()
            .Select(static part => ResponseContentPart.CreateInputTextPart(part.Text))
            .ToArray();

        if (contentParts.Length == 0)
        {
            userMessage = null;
            errorMessage = "OpenAI image generation requires text input.";
            return false;
        }

        userMessage = ResponseItem.CreateUserMessageItem(contentParts);
        errorMessage = null;
        return true;
    }

    private static bool TryCreateImageGenerationAssistantMessage(
        AssistantChatEntry assistant,
        out MessageResponseItem? assistantMessage,
        out string? errorMessage
    )
    {
        if (assistant.Blocks.OfType<ToolCallAssistantBlock>().Any())
        {
            assistantMessage = null;
            errorMessage =
                "OpenAI image generation does not yet support replaying assistant tool calls.";
            return false;
        }

        var contentParts = assistant.Blocks
            .SelectMany(ToImageGenerationAssistantContentParts)
            .ToArray();

        assistantMessage = contentParts.Length == 0
            ? null
            : ResponseItem.CreateAssistantMessageItem(contentParts);
        errorMessage = null;
        return true;
    }

    private static IEnumerable<ResponseContentPart> ToImageGenerationAssistantContentParts(
        AssistantContentBlock block
    )
    {
        switch (block)
        {
            case TextAssistantBlock text when !string.IsNullOrWhiteSpace(text.Text):
                yield return ResponseContentPart.CreateOutputTextPart(
                    text.Text,
                    Array.Empty<ResponseMessageAnnotation>()
                );
                yield break;

            case ReasoningAssistantBlock reasoning when !string.IsNullOrWhiteSpace(reasoning.Text):
                yield return ResponseContentPart.CreateOutputTextPart(
                    reasoning.Text!,
                    Array.Empty<ResponseMessageAnnotation>()
                );
                yield break;
        }
    }

    private ChatClientTurnResult BuildImageGenerationAssistantTurn(
        ResponseResult response,
        ChatImageGenerationOptions imageGenerationOptions
    )
    {
        var blocks = new List<AssistantContentBlock>();

        foreach (var outputItem in response.OutputItems)
        {
            switch (outputItem)
            {
                case MessageResponseItem message:
                    AppendResponseMessageBlocks(blocks, message);
                    break;

                case ReasoningResponseItem reasoning:
                    var summaryText = reasoning.GetSummaryText();
                    if (!string.IsNullOrWhiteSpace(summaryText))
                    {
                        blocks.Add(
                            new ReasoningAssistantBlock(
                                Guid.NewGuid().ToString("n"),
                                summaryText,
                                ReasoningVisibility.Visible
                            )
                        );
                    }

                    break;

                case ImageGenerationCallResponseItem image:
                    blocks.Add(
                        new ImageAssistantBlock(
                            Guid.NewGuid().ToString("n"),
                            image.ImageResultBytes,
                            ResolveGeneratedImageMediaType(image, imageGenerationOptions.OutputFormat)
                        )
                    );
                    break;
            }
        }

        if (blocks.Count == 0)
        {
            return new ChatClientFailure(
                ChatErrorSource.Provider,
                "Unexpected OpenAI image-generation response: no assistant content",
                Provider: "openai",
                Model: _modelName
            );
        }

        return new ChatClientAssistantTurn(
            Guid.NewGuid().ToString("n"),
            "openai",
            _modelName,
            blocks,
            response.Status?.ToString()?.ToLowerInvariant(),
            ToChatUsage(response.Usage)
        );
    }

    private static void AppendResponseMessageBlocks(
        ICollection<AssistantContentBlock> blocks,
        MessageResponseItem message
    )
    {
        foreach (var contentPart in message.Content)
        {
            if (
                contentPart.Kind == ResponseContentPartKind.OutputText
                && !string.IsNullOrWhiteSpace(contentPart.Text)
            )
            {
                blocks.Add(new TextAssistantBlock(Guid.NewGuid().ToString("n"), contentPart.Text));
            }
        }
    }

    private ChatClientFailure CreateImageGenerationFailure(string message) =>
        new(
            ChatErrorSource.Session,
            message,
            Provider: "openai",
            Model: _modelName
        );

    private static ImageGenerationToolOutputFileFormat ToOpenAiOutputFileFormat(
        ChatImageOutputFormat outputFormat
    ) =>
        outputFormat switch
        {
            ChatImageOutputFormat.Png => ImageGenerationToolOutputFileFormat.Png,
            ChatImageOutputFormat.Jpeg => ImageGenerationToolOutputFileFormat.Jpeg,
            ChatImageOutputFormat.Webp => ImageGenerationToolOutputFileFormat.Webp,
            _ => throw new ArgumentOutOfRangeException(nameof(outputFormat)),
        };

    private static string ResolveGeneratedImageMediaType(
        ImageGenerationCallResponseItem image,
        ChatImageOutputFormat fallbackFormat
    ) =>
        fallbackFormat switch
        {
            ChatImageOutputFormat.Png => "image/png",
            ChatImageOutputFormat.Jpeg => "image/jpeg",
            ChatImageOutputFormat.Webp => "image/webp",
            _ => throw new ArgumentOutOfRangeException(nameof(fallbackFormat)),
        };

    public IChatCompletionStream SendAsync(
        ChatClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Options?.ImageGeneration != null)
        {
            return CreateImageGenerationStream(request, cancellationToken);
        }

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

    private IChatCompletionStream CreateImageGenerationStream(
        ChatClientRequest request,
        CancellationToken cancellationToken
    ) =>
        ChatCompletionStream.Create(
            resultCancellationToken =>
                GetImageGenerationTurnResultAsync(request, resultCancellationToken),
            (writer, streamCancellationToken) =>
                GetStreamingImageGenerationTurnResultAsync(
                    request,
                    writer,
                    streamCancellationToken
                ),
            cancellationToken
        );

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
                    messages.Add(CreateUserMessage(user));
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

    private static UserChatMessage CreateUserMessage(UserChatEntry user)
    {
        var contentParts = user.ContentParts.Select(ToChatMessageContentPart).ToArray();

        return contentParts.Length == 1 && contentParts[0].Kind == ChatMessageContentPartKind.Text
            ? new UserChatMessage(contentParts[0].Text)
            : new UserChatMessage(contentParts);
    }

    private static ChatMessageContentPart ToChatMessageContentPart(UserContentPart part) =>
        part switch
        {
            TextUserContentPart text => ChatMessageContentPart.CreateTextPart(text.Text),
            InlineImageUserContentPart image => ChatMessageContentPart.CreateImagePart(
                image.Data,
                image.MediaType
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported user content part type '{part.GetType().Name}'."
            ),
        };

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
            functionParameters: BinaryData.FromString(tool.InputSchema.GetRawText()),
            functionSchemaIsStrict: IsStrictCompatibleFunctionSchema(tool.InputSchema)
        );

    private static bool IsStrictCompatibleFunctionSchema(JsonElement schema)
    {
        return schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("type", out var type)
            && string.Equals(type.GetString(), "object", StringComparison.Ordinal)
            && IsStrictCompatibleSchemaNode(schema);
    }

    private static bool IsStrictCompatibleSchemaNode(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        HashSet<string>? propertyNames = null;
        HashSet<string>? requiredPropertyNames = null;
        var hasObjectShape = false;
        var hasClosedObjectShape = false;

        foreach (var property in schema.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    hasObjectShape = string.Equals(
                        property.Value.GetString(),
                        "object",
                        StringComparison.Ordinal
                    );
                    break;

                case "description":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }
                    break;

                case "properties":
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    propertyNames = new HashSet<string>(
                        property.Value.EnumerateObject().Select(child => child.Name),
                        StringComparer.Ordinal
                    );

                    foreach (var child in property.Value.EnumerateObject())
                    {
                        if (!IsStrictCompatibleSchemaNode(child.Value))
                        {
                            return false;
                        }
                    }
                    break;

                case "required":
                    if (
                        property.Value.ValueKind != JsonValueKind.Array
                        || property.Value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)
                    )
                    {
                        return false;
                    }

                    requiredPropertyNames = new HashSet<string>(
                        property.Value.EnumerateArray().Select(item => item.GetString()!),
                        StringComparer.Ordinal
                    );
                    break;

                case "items":
                    if (!IsStrictCompatibleSchemaNode(property.Value))
                    {
                        return false;
                    }
                    break;

                case "enum":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        return false;
                    }
                    break;

                case "additionalProperties":
                    if (property.Value.ValueKind != JsonValueKind.False)
                    {
                        return false;
                    }

                    hasClosedObjectShape = true;
                    break;

                case "minLength":
                    if (property.Value.ValueKind != JsonValueKind.Number)
                    {
                        return false;
                    }
                    break;

                default:
                    return false;
            }
        }

        if (propertyNames != null)
        {
            if (requiredPropertyNames == null || !requiredPropertyNames.SetEquals(propertyNames))
            {
                return false;
            }
        }

        return !hasObjectShape || hasClosedObjectShape;
    }

    private sealed class StreamingAssistantTurnAccumulator
    {
        private readonly string _assistantTurnId = Guid.NewGuid().ToString("n");
        private readonly string _textBlockId = Guid.NewGuid().ToString("n");
        private readonly StringBuilder _textBuilder = new();
        private readonly SortedDictionary<int, StreamingToolCallAccumulator> _toolCalls = new();

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

            foreach (var toolCall in _toolCalls.Values)
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
            if (_toolCalls.TryGetValue(update.Index, out var existing))
            {
                return existing;
            }

            var created = new StreamingToolCallAccumulator();
            _toolCalls[update.Index] = created;
            return created;
        }
    }

    private sealed class StreamingToolCallAccumulator
    {
        private readonly string _blockId = Guid.NewGuid().ToString("n");
        private readonly MemoryStream _argumentsBuffer = new();

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

            var argumentsUpdate = update.FunctionArgumentsUpdate?.ToMemory();
            if (argumentsUpdate is { IsEmpty: false })
            {
                _argumentsBuffer.Write(argumentsUpdate.Value.Span);
            }
        }

        public ToolCallAssistantBlock? BuildBlock(bool lenientToolArguments)
        {
            if (string.IsNullOrWhiteSpace(ToolCallId) || string.IsNullOrWhiteSpace(FunctionName))
            {
                return null;
            }

            IReadOnlyDictionary<string, object?> arguments;
            if (_argumentsBuffer.Length == 0)
            {
                arguments = new Dictionary<string, object?>();
            }
            else if (TryParseToolArguments(_argumentsBuffer.GetBuffer().AsMemory(0, (int)_argumentsBuffer.Length), out var parsedArguments))
            {
                arguments = parsedArguments;
            }
            else if (lenientToolArguments)
            {
                arguments = new Dictionary<string, object?>();
            }
            else
            {
                arguments = ParseToolArguments(_argumentsBuffer.GetBuffer().AsMemory(0, (int)_argumentsBuffer.Length));
            }

            return new ToolCallAssistantBlock(_blockId, ToolCallId, FunctionName, arguments);
        }
    }

    private static bool TryParseToolArguments(
        ReadOnlyMemory<byte> argumentsJson,
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

#pragma warning restore SCME0001
#pragma warning restore OPENAI001
