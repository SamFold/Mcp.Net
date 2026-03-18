using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
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
/// Builds Anthropic requests from an explicit conversation snapshot.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private readonly IAnthropicMessageClient _messagesClient;
    private readonly IChatTranscriptReplayTransformer _replayTransformer;
    private readonly string _model;
    private readonly ILogger<AnthropicChatClient> _logger;

    public AnthropicChatClient(ChatClientOptions options, ILogger<AnthropicChatClient> logger)
        : this(options, logger, new AnthropicMessageClient(options.ApiKey), null)
    {
    }

    internal AnthropicChatClient(
        ChatClientOptions options,
        ILogger<AnthropicChatClient> logger,
        IAnthropicMessageClient messagesClient,
        IChatTranscriptReplayTransformer? replayTransformer = null
    )
    {
        _logger = logger;
        _messagesClient = messagesClient ?? throw new ArgumentNullException(nameof(messagesClient));
        _replayTransformer = replayTransformer ?? new ChatTranscriptReplayTransformer();

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
    }

    private static Tool ConvertToAnthropicTool(ChatClientTool tool)
    {
        var toolSchema = JsonNode.Parse(tool.InputSchema.GetRawText());

        var function = new Function(tool.Name, tool.Description, toolSchema);

        return new Tool(function);
    }

    /// <summary>
    /// Generates a response from Claude using the supplied conversation snapshot.
    /// </summary>
    private async Task<ChatClientTurnResult> GetTurnResultAsync(
        MessageParameters parameters,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await _messagesClient.GetResponseAsync(parameters, cancellationToken);

            return BuildAssistantTurn(response);
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

    private MessageParameters CreateMessageParameters(ChatClientRequest request, bool stream = false)
    {
        var tools = request.Options?.ToolChoice?.Kind == ChatToolChoiceKind.None
            ? new List<Tool>()
            : request.Tools.Select(ConvertToAnthropicTool).ToList();

        var parameters = new MessageParameters
        {
            Model = _model,
            MaxTokens = request.Options?.MaxOutputTokens ?? 1024,
            Messages = BuildMessages(request),
            Tools = tools,
            System = string.IsNullOrWhiteSpace(request.SystemPrompt)
                ? []
                : [new SystemMessage(request.SystemPrompt)],
            Stream = stream,
        };

        if (request.Options?.Temperature is float temperature)
        {
            parameters.Temperature = Convert.ToDecimal(temperature);
        }

        if (request.Options?.ToolChoice is { } toolChoice)
        {
            parameters.ToolChoice = toolChoice.Kind switch
            {
                ChatToolChoiceKind.Auto => new ToolChoice { Type = ToolChoiceType.Auto },
                ChatToolChoiceKind.None => null,
                ChatToolChoiceKind.Required => new ToolChoice { Type = ToolChoiceType.Any },
                ChatToolChoiceKind.Specific => new ToolChoice
                {
                    Type = ToolChoiceType.Tool,
                    Name = toolChoice.ToolName!,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(toolChoice)),
            };
        }

        return parameters;
    }

    private static ChatUsage? ToChatUsage(Usage? usage)
    {
        if (usage == null)
        {
            return null;
        }

        var additionalCounts = new Dictionary<string, int>();
        AddAdditionalCount(
            additionalCounts,
            "cacheCreationInputTokens",
            usage.CacheCreationInputTokens
        );
        AddAdditionalCount(additionalCounts, "cacheReadInputTokens", usage.CacheReadInputTokens);
        AddAdditionalCount(
            additionalCounts,
            "webSearchRequests",
            usage.ServerToolUse?.WebSearchRequests
        );
        AddAdditionalCount(
            additionalCounts,
            "codeExecutionRequests",
            usage.ServerToolUse?.CodeExecutionRequests
        );
        AddAdditionalCount(additionalCounts, "webFetchRequests", usage.ServerToolUse?.WebFetchRequests);

        return new ChatUsage(
            usage.InputTokens,
            usage.OutputTokens,
            usage.InputTokens + usage.OutputTokens,
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

    private ChatClientAssistantTurn BuildAssistantTurn(MessageResponse response)
    {
        var blocks = new List<AssistantContentBlock>();
        var contentItems = response.Content ?? [];

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
            blocks,
            response.StopReason,
            ToChatUsage(response.Usage)
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

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(string inputJson) =>
        ParseToolArguments(JsonNode.Parse(inputJson));

    private static bool TryParseToolArguments(
        string inputJson,
        out IReadOnlyDictionary<string, object?> arguments
    )
    {
        try
        {
            arguments = ParseToolArguments(inputJson);
            return true;
        }
        catch (JsonException)
        {
            arguments = new Dictionary<string, object?>();
            return false;
        }
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

    public IChatCompletionStream SendAsync(
        ChatClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Options?.ImageGeneration != null)
        {
            return ChatCompletionStream.FromResult(
                new ChatClientFailure(
                    ChatErrorSource.Session,
                    "Anthropic image generation is not supported by Mcp.Net.LLM yet.",
                    Provider: "anthropic",
                    Model: _model
                ),
                cancellationToken
            );
        }

        return ChatCompletionStream.Create(
            resultCancellationToken =>
                GetTurnResultAsync(CreateMessageParameters(request), resultCancellationToken),
            (writer, streamCancellationToken) =>
                GetStreamingTurnResultAsync(
                    CreateMessageParameters(request, stream: true),
                    writer,
                    streamCancellationToken
                ),
            cancellationToken
        );
    }

    private async Task<ChatClientTurnResult> GetStreamingTurnResultAsync(
        MessageParameters parameters,
        ChannelWriter<ChatClientAssistantTurn> assistantTurnUpdates,
        CancellationToken cancellationToken
    )
    {
        var accumulator = new StreamingAssistantTurnAccumulator();
        ChatClientAssistantTurn? lastReportedTurn = null;

        await foreach (var response in _messagesClient
                           .StreamResponseAsync(parameters, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            accumulator.Apply(response);

            var partialTurn = accumulator.BuildPartialTurn(_model);
            if (partialTurn == null || AreEquivalentTurns(partialTurn, lastReportedTurn))
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
                Provider: "anthropic",
                Model: _model
            );
        }

        var finalTurn = accumulator.BuildFinalTurn(_model);

        return finalTurn;
    }

    private static bool AreEquivalentTurns(
        ChatClientAssistantTurn current,
        ChatClientAssistantTurn? previous
    ) =>
        previous != null
        && current.Id == previous.Id
        && current.Provider == previous.Provider
        && current.Model == previous.Model
        && current.StopReason == previous.StopReason
        && Equals(current.Usage, previous.Usage)
        && current.Blocks.SequenceEqual(previous.Blocks);

    private List<Message> BuildMessages(ChatClientRequest request)
    {
        var messages = new List<Message>();
        var replayTranscript = _replayTransformer.Transform(
            request.Transcript,
            new ReplayTarget("anthropic", _model)
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

    private static Message CreateUserMessage(UserChatEntry user) =>
        new()
        {
            Role = RoleType.User,
            Content = user.ContentParts.Select(ToAnthropicContent).ToList(),
        };

    private static ContentBase ToAnthropicContent(UserContentPart part) =>
        part switch
        {
            TextUserContentPart text => new TextContent { Text = text.Text },
            InlineImageUserContentPart image => new ImageContent
            {
                Source = new ImageSource
                {
                    Type = SourceType.base64,
                    MediaType = image.MediaType,
                    Data = Convert.ToBase64String(image.Data.ToArray()),
                },
            },
            _ => throw new InvalidOperationException(
                $"Unsupported user content part type '{part.GetType().Name}'."
            ),
        };

    private static void AppendToolResult(ICollection<Message> messages, ToolInvocationResult? result)
    {
        if (result == null)
        {
            return;
        }

        messages.Add(
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

    private static void AppendAssistantReplayEntry(
        ICollection<Message> messages,
        AssistantChatEntry assistant
    )
    {
        var replayContent = assistant.Blocks.Select(ToReplayContent).OfType<ContentBase>().ToList();

        if (replayContent.Count == 0)
        {
            return;
        }

        messages.Add(
            new Message
            {
                Role = RoleType.Assistant,
                Content = replayContent,
            }
        );
    }

    private sealed class StreamingAssistantTurnAccumulator
    {
        private readonly string _assistantTurnId = Guid.NewGuid().ToString("n");
        private readonly List<StreamingContentBlockAccumulator> _blocks = new();
        private readonly StreamingUsageAccumulator _usage = new();
        private StreamingContentBlockAccumulator? _activeBlock;

        public string? StopReason { get; private set; }

        public bool HasContent => _blocks.Any(block => block.HasContent);

        public void Apply(MessageResponse response)
        {
            _usage.Apply(response.StreamStartMessage?.Usage);
            _usage.Apply(response.Delta?.Usage);
            _usage.Apply(response.Usage);

            if (!string.IsNullOrWhiteSpace(response.Delta?.StopReason))
            {
                StopReason = response.Delta.StopReason;
            }
            else if (!string.IsNullOrWhiteSpace(response.StopReason))
            {
                StopReason = response.StopReason;
            }

            if (response.Type == "content_block_start" && response.ContentBlock != null)
            {
                _activeBlock = StartBlock(response.ContentBlock);
            }

            if (response.Delta != null && _activeBlock != null)
            {
                _activeBlock.Apply(response.Delta);
            }

            if (response.Type == "content_block_stop")
            {
                _activeBlock = null;
            }

            if (_activeBlock is { RawType: "tool_use" } && response.Delta?.StopReason == "tool_use")
            {
                _activeBlock = null;
            }
        }

        public ChatClientAssistantTurn? BuildPartialTurn(string model)
        {
            var blocks = BuildBlocks(lenientToolArguments: true);
            return blocks.Count == 0
                ? null
                : new ChatClientAssistantTurn(
                    _assistantTurnId,
                    "anthropic",
                    model,
                    blocks,
                    StopReason,
                    _usage.Build()
                );
        }

        public ChatClientAssistantTurn BuildFinalTurn(string model) =>
            new(
                _assistantTurnId,
                "anthropic",
                model,
                BuildBlocks(lenientToolArguments: false),
                StopReason,
                _usage.Build()
            );

        private StreamingContentBlockAccumulator? StartBlock(ContentBlock contentBlock)
        {
            _activeBlock = null;

            var block = new StreamingContentBlockAccumulator(contentBlock);
            _blocks.Add(block);

            return block.IsImmediate ? null : block;
        }

        private List<AssistantContentBlock> BuildBlocks(bool lenientToolArguments)
        {
            var blocks = new List<AssistantContentBlock>();

            foreach (var block in _blocks)
            {
                var builtBlock = block.BuildBlock(lenientToolArguments);
                if (builtBlock != null)
                {
                    blocks.Add(builtBlock);
                }
            }

            return blocks;
        }
    }

    private sealed class StreamingContentBlockAccumulator
    {
        private readonly string _blockId = Guid.NewGuid().ToString("n");
        private readonly StringBuilder _contentBuilder = new();
        private string? _toolCallId;
        private string? _toolName;
        private string? _reasoningReplayToken;
        private string? _redactedData;

        public StreamingContentBlockAccumulator(ContentBlock contentBlock)
        {
            RawType = contentBlock.Type ?? string.Empty;
            _toolCallId = contentBlock.Id;
            _toolName = contentBlock.Name;

            if (!string.IsNullOrWhiteSpace(contentBlock.Text))
            {
                _contentBuilder.Append(contentBlock.Text);
            }

            if (RawType == "redacted_thinking")
            {
                _redactedData = contentBlock.Data;
                IsImmediate = true;
            }
        }

        public string RawType { get; }

        public bool IsImmediate { get; }

        public bool HasContent => BuildBlock(lenientToolArguments: true) != null;

        public void Apply(Delta delta)
        {
            switch (RawType)
            {
                case "text" when !string.IsNullOrEmpty(delta.Text):
                    _contentBuilder.Append(delta.Text);
                    break;
                case "thinking":
                    if (!string.IsNullOrEmpty(delta.Thinking))
                    {
                        _contentBuilder.Append(delta.Thinking);
                    }

                    if (!string.IsNullOrWhiteSpace(delta.Signature))
                    {
                        _reasoningReplayToken = delta.Signature;
                    }

                    break;
                case "tool_use":
                    if (!string.IsNullOrWhiteSpace(delta.Name))
                    {
                        _toolName = delta.Name;
                    }

                    if (!string.IsNullOrEmpty(delta.PartialJson))
                    {
                        _contentBuilder.Append(delta.PartialJson);
                    }

                    break;
            }
        }

        public AssistantContentBlock? BuildBlock(bool lenientToolArguments)
        {
            switch (RawType)
            {
                case "text":
                    return _contentBuilder.Length == 0
                        ? null
                        : new TextAssistantBlock(_blockId, _contentBuilder.ToString());
                case "thinking":
                    if (_contentBuilder.Length == 0 && string.IsNullOrWhiteSpace(_reasoningReplayToken))
                    {
                        return null;
                    }

                    return new ReasoningAssistantBlock(
                        _blockId,
                        _contentBuilder.Length == 0 ? null : _contentBuilder.ToString(),
                        ReasoningVisibility.Visible,
                        string.IsNullOrWhiteSpace(_reasoningReplayToken) ? null : _reasoningReplayToken
                    );
                case "redacted_thinking":
                    return string.IsNullOrWhiteSpace(_redactedData)
                        ? null
                        : new ReasoningAssistantBlock(
                            _blockId,
                            null,
                            ReasoningVisibility.Redacted,
                            _redactedData
                        );
                case "tool_use":
                    if (string.IsNullOrWhiteSpace(_toolCallId) || string.IsNullOrWhiteSpace(_toolName))
                    {
                        return null;
                    }

                    var argumentsText = _contentBuilder.ToString();
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

                    return new ToolCallAssistantBlock(_blockId, _toolCallId, _toolName, arguments);
                default:
                    return null;
            }
        }
    }

    private sealed class StreamingUsageAccumulator
    {
        private int _inputTokens;
        private int _outputTokens;
        private int _cacheCreationInputTokens;
        private int _cacheReadInputTokens;
        private int _webSearchRequests;
        private int _codeExecutionRequests;
        private int _webFetchRequests;

        public void Apply(Usage? usage)
        {
            if (usage == null)
            {
                return;
            }

            _inputTokens = Math.Max(_inputTokens, usage.InputTokens);
            _outputTokens = Math.Max(_outputTokens, usage.OutputTokens);
            _cacheCreationInputTokens = Math.Max(
                _cacheCreationInputTokens,
                usage.CacheCreationInputTokens
            );
            _cacheReadInputTokens = Math.Max(_cacheReadInputTokens, usage.CacheReadInputTokens);
            _webSearchRequests = Math.Max(
                _webSearchRequests,
                usage.ServerToolUse?.WebSearchRequests ?? 0
            );
            _codeExecutionRequests = Math.Max(
                _codeExecutionRequests,
                usage.ServerToolUse?.CodeExecutionRequests ?? 0
            );
            _webFetchRequests = Math.Max(
                _webFetchRequests,
                usage.ServerToolUse?.WebFetchRequests ?? 0
            );
        }

        public ChatUsage? Build()
        {
            if (
                _inputTokens == 0
                && _outputTokens == 0
                && _cacheCreationInputTokens == 0
                && _cacheReadInputTokens == 0
                && _webSearchRequests == 0
                && _codeExecutionRequests == 0
                && _webFetchRequests == 0
            )
            {
                return null;
            }

            var additionalCounts = new Dictionary<string, int>();
            AddAdditionalCount(
                additionalCounts,
                "cacheCreationInputTokens",
                _cacheCreationInputTokens
            );
            AddAdditionalCount(additionalCounts, "cacheReadInputTokens", _cacheReadInputTokens);
            AddAdditionalCount(additionalCounts, "webSearchRequests", _webSearchRequests);
            AddAdditionalCount(additionalCounts, "codeExecutionRequests", _codeExecutionRequests);
            AddAdditionalCount(additionalCounts, "webFetchRequests", _webFetchRequests);

            return new ChatUsage(
                _inputTokens,
                _outputTokens,
                _inputTokens + _outputTokens,
                additionalCounts
            );
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
            ImageAssistantBlock => null,
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
