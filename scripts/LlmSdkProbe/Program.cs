using System.Collections;
using System.ClientModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using AnthropicTool = Anthropic.SDK.Common.Tool;

var exitCode = await LlmSdkProbeProgram.RunAsync(args);
return exitCode;

internal static class LlmSdkProbeProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        ProbeOptions options;
        try
        {
            options = ProbeOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(ProbeOptions.HelpText);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ProbeOptions.HelpText);
            return 0;
        }

        var plans = ProbePlanner.Build(options).ToList();
        if (plans.Count == 0)
        {
            Console.Error.WriteLine("No probe plans were selected.");
            return 2;
        }

        var reports = new List<ProbeReport>();
        foreach (var plan in plans)
        {
            reports.Add(await ProbeRunner.RunAsync(plan, options));
        }

        var output = JsonSerializer.Serialize(
            reports,
            new JsonSerializerOptions { WriteIndented = true }
        );

        Console.WriteLine(output);

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var outputPath = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, output);
            Console.Error.WriteLine($"Wrote probe output to {outputPath}");
        }

        return reports.Any(r => !r.Success) ? 1 : 0;
    }
}

internal sealed record ProbeOptions(
    ProviderSelection Provider,
    ScenarioSelection Scenario,
    bool Streaming,
    bool ShowHelp,
    string? Model,
    string? OutputPath,
    int AnthropicThinkingBudget
)
{
    public static string HelpText =>
        """
        LLM SDK response probe

        Usage:
          dotnet run --project scripts/LlmSdkProbe/LlmSdkProbe.csproj -- --provider <openai|anthropic|all> --scenario <text|tool|reasoning|error|all> [options]

        Options:
          --provider <value>             Required. openai, anthropic, or all
          --scenario <value>             Required. text, tool, reasoning, error, or all
          --model <value>                Optional override for the model used by the selected probe(s)
          --stream                       Enables OpenAI Responses streaming for the reasoning probe
          --anthropic-thinking-budget N  Thinking budget tokens for Anthropic reasoning probe. Default: 4000
          --output <path>                Optional path to write the JSON report
          --help                         Show this help text

        Examples:
          dotnet run --project scripts/LlmSdkProbe/LlmSdkProbe.csproj -- --provider openai --scenario reasoning --stream
          dotnet run --project scripts/LlmSdkProbe/LlmSdkProbe.csproj -- --provider anthropic --scenario reasoning
          dotnet run --project scripts/LlmSdkProbe/LlmSdkProbe.csproj -- --provider all --scenario all --output artifacts/llm-probe.json

        Notes:
          - OPENAI_API_KEY is required for OpenAI probes.
          - ANTHROPIC_API_KEY is required for Anthropic probes.
          - Error probes intentionally call each provider with an invalid model name to capture the real exception shape.
        """;

    public static ProbeOptions Parse(string[] args)
    {
        ProviderSelection? provider = null;
        ScenarioSelection? scenario = null;
        string? model = null;
        string? outputPath = null;
        bool streaming = false;
        bool showHelp = false;
        int anthropicThinkingBudget = 4000;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--provider":
                    provider = ParseProvider(ReadRequiredValue(args, ref i, "--provider"));
                    break;
                case "--scenario":
                    scenario = ParseScenario(ReadRequiredValue(args, ref i, "--scenario"));
                    break;
                case "--model":
                    model = ReadRequiredValue(args, ref i, "--model");
                    break;
                case "--output":
                    outputPath = ReadRequiredValue(args, ref i, "--output");
                    break;
                case "--anthropic-thinking-budget":
                    anthropicThinkingBudget = int.Parse(
                        ReadRequiredValue(args, ref i, "--anthropic-thinking-budget")
                    );
                    break;
                case "--stream":
                    streaming = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        if (showHelp)
        {
            return new ProbeOptions(
                ProviderSelection.All,
                ScenarioSelection.All,
                streaming,
                showHelp,
                model,
                outputPath,
                anthropicThinkingBudget
            );
        }

        if (provider is null)
        {
            throw new ArgumentException("Missing required --provider argument.");
        }

        if (scenario is null)
        {
            throw new ArgumentException("Missing required --scenario argument.");
        }

        return new ProbeOptions(
            provider.Value,
            scenario.Value,
            streaming,
            showHelp,
            model,
            outputPath,
            anthropicThinkingBudget
        );
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }

    private static ProviderSelection ParseProvider(string value) =>
        value.ToLowerInvariant() switch
        {
            "openai" => ProviderSelection.OpenAI,
            "anthropic" => ProviderSelection.Anthropic,
            "all" => ProviderSelection.All,
            _ => throw new ArgumentException($"Unsupported provider: {value}"),
        };

    private static ScenarioSelection ParseScenario(string value) =>
        value.ToLowerInvariant() switch
        {
            "text" => ScenarioSelection.Text,
            "tool" => ScenarioSelection.Tool,
            "reasoning" => ScenarioSelection.Reasoning,
            "error" => ScenarioSelection.Error,
            "all" => ScenarioSelection.All,
            _ => throw new ArgumentException($"Unsupported scenario: {value}"),
        };
}

internal enum ProviderSelection
{
    OpenAI,
    Anthropic,
    All,
}

internal enum ScenarioSelection
{
    Text,
    Tool,
    Reasoning,
    Error,
    All,
}

internal sealed record ProbePlan(
    string Provider,
    string Scenario,
    string ApiSurface,
    string Model,
    string Prompt
);

internal static class ProbePlanner
{
    public static IEnumerable<ProbePlan> Build(ProbeOptions options)
    {
        var providers = options.Provider switch
        {
            ProviderSelection.OpenAI => new[] { "openai" },
            ProviderSelection.Anthropic => new[] { "anthropic" },
            _ => new[] { "openai", "anthropic" },
        };

        var scenarios = options.Scenario switch
        {
            ScenarioSelection.Text => new[] { "text" },
            ScenarioSelection.Tool => new[] { "tool" },
            ScenarioSelection.Reasoning => new[] { "reasoning" },
            ScenarioSelection.Error => new[] { "error" },
            _ => new[] { "text", "tool", "reasoning", "error" },
        };

        foreach (var provider in providers)
        {
            foreach (var scenario in scenarios)
            {
                var plan = provider switch
                {
                    "openai" => new ProbePlan(
                        provider,
                        scenario,
                        scenario == "reasoning" ? "openai.responses" : "openai.chat",
                        options.Model ?? GetDefaultOpenAiModel(scenario),
                        GetDefaultPrompt(provider, scenario)
                    ),
                    "anthropic" => new ProbePlan(
                        provider,
                        scenario,
                        "anthropic.messages",
                        options.Model ?? GetDefaultAnthropicModel(scenario),
                        GetDefaultPrompt(provider, scenario)
                    ),
                    _ => throw new InvalidOperationException($"Unsupported provider {provider}"),
                };

                yield return plan;
            }
        }
    }

    private static string GetDefaultOpenAiModel(string scenario) =>
        scenario switch
        {
            "reasoning" => "gpt-5.1",
            _ => "gpt-5",
        };

    private static string GetDefaultAnthropicModel(string scenario) =>
        scenario switch
        {
            "reasoning" => AnthropicModels.Claude46Sonnet,
            _ => AnthropicModels.Claude45Sonnet,
        };

    private static string GetDefaultPrompt(string provider, string scenario) =>
        (provider, scenario) switch
        {
            (_, "text") =>
                "Reply with one short sentence that identifies the response shape you are returning.",
            (_, "tool") =>
                "You must call the provided record_probe tool exactly once and include a concise status and code.",
            ("openai", "reasoning") =>
                "Think through this carefully: what is the best opening move in tic-tac-toe and why?",
            ("anthropic", "reasoning") =>
                "How many r characters are in the word strawberry? Think step by step.",
            (_, "error") => "This prompt is unused because the error probe uses an invalid model name.",
            _ => "Hello.",
        };
}

internal static class ProbeRunner
{
    public static async Task<ProbeReport> RunAsync(ProbePlan plan, ProbeOptions options)
    {
        try
        {
            return plan.Provider switch
            {
                "openai" => await RunOpenAiAsync(plan, options),
                "anthropic" => await RunAnthropicAsync(plan, options),
                _ => throw new InvalidOperationException($"Unsupported provider {plan.Provider}"),
            };
        }
        catch (Exception ex)
        {
            return ProbeReport.Failure(plan, ex);
        }
    }

    private static async Task<ProbeReport> RunOpenAiAsync(ProbePlan plan, ProbeOptions options)
    {
        var apiKey = RequireEnvironmentVariable("OPENAI_API_KEY");
        return plan.Scenario switch
        {
            "text" => await RunOpenAiChatTextAsync(apiKey, plan),
            "tool" => await RunOpenAiChatToolAsync(apiKey, plan),
            "reasoning" => await RunOpenAiResponsesReasoningAsync(apiKey, plan, options.Streaming),
            "error" => await RunOpenAiErrorAsync(apiKey, plan),
            _ => throw new InvalidOperationException($"Unsupported scenario {plan.Scenario}"),
        };
    }

    private static async Task<ProbeReport> RunAnthropicAsync(ProbePlan plan, ProbeOptions options)
    {
        var apiKey = RequireEnvironmentVariable("ANTHROPIC_API_KEY");
        return plan.Scenario switch
        {
            "text" => await RunAnthropicTextAsync(apiKey, plan),
            "tool" => await RunAnthropicToolAsync(apiKey, plan),
            "reasoning" => await RunAnthropicThinkingAsync(apiKey, plan, options.AnthropicThinkingBudget),
            "error" => await RunAnthropicErrorAsync(apiKey, plan),
            _ => throw new InvalidOperationException($"Unsupported scenario {plan.Scenario}"),
        };
    }

    private static async Task<ProbeReport> RunOpenAiChatTextAsync(string apiKey, ProbePlan plan)
    {
        var client = new ChatClient(plan.Model, apiKey);
        var messages = new ChatMessage[]
        {
            new SystemChatMessage("You are a response-shape probe."),
            new UserChatMessage(plan.Prompt),
        };

        var completion = await client.CompleteChatAsync(messages);
        return ProbeReport.FromSuccess(plan, completion.Value, BuildOpenAiChatSummary(completion.Value));
    }

    private static async Task<ProbeReport> RunOpenAiChatToolAsync(string apiKey, ProbePlan plan)
    {
        var client = new ChatClient(plan.Model, apiKey);
        var tool = ChatTool.CreateFunctionTool(
            functionName: "record_probe",
            functionDescription: "Record a probe result into structured JSON.",
            functionParameters: BinaryData.FromString(
                """
                {
                  "type": "object",
                  "properties": {
                    "status": { "type": "string" },
                    "code": { "type": "integer" }
                  },
                  "required": ["status", "code"],
                  "additionalProperties": false
                }
                """
            )
        );

        var options = new ChatCompletionOptions
        {
            ToolChoice = ChatToolChoice.CreateFunctionChoice("record_probe"),
        };
        options.Tools.Add(tool);

        var messages = new ChatMessage[]
        {
            new SystemChatMessage("You are a response-shape probe."),
            new UserChatMessage(plan.Prompt),
        };

        var completion = await client.CompleteChatAsync(messages, options);
        return ProbeReport.FromSuccess(plan, completion.Value, BuildOpenAiChatSummary(completion.Value));
    }

    private static async Task<ProbeReport> RunOpenAiResponsesReasoningAsync(
        string apiKey,
        ProbePlan plan,
        bool streaming
    )
    {
        var client = new OpenAIClient(apiKey).GetResponsesClient();
        var options = new CreateResponseOptions
        {
            Model = plan.Model,
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ResponseReasoningEffortLevel.Low,
            },
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(plan.Prompt));

        if (!streaming)
        {
            var response = await client.CreateResponseAsync(options);
            return ProbeReport.FromSuccess(plan, response.Value, BuildOpenAiResponsesSummary(response.Value));
        }

        options.StreamingEnabled = true;

        var updates = new List<object>();
        await foreach (var update in client.CreateResponseStreamingAsync(options))
        {
            updates.Add(BuildOpenAiStreamingUpdateSummary(update));
        }

        return ProbeReport.FromSuccess(
            plan,
            updates,
            new Dictionary<string, object?>
            {
                ["streaming"] = true,
                ["updateCount"] = updates.Count,
                ["updateTypes"] = updates.Select(update => update.GetType().FullName).ToList(),
            }
        );
    }

    private static async Task<ProbeReport> RunOpenAiErrorAsync(string apiKey, ProbePlan plan)
    {
        var client = new ChatClient("this-model-should-not-exist", apiKey);
        try
        {
            await client.CompleteChatAsync(
                new ChatMessage[] { new UserChatMessage("Trigger an invalid model error.") }
            );

            throw new InvalidOperationException(
                "The OpenAI error probe unexpectedly succeeded with an invalid model."
            );
        }
        catch (Exception ex)
        {
            return ProbeReport.FromSuccess(
                plan,
                ExceptionSnapshot.FromException(ex),
                new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().FullName,
                    ["message"] = ex.Message,
                    ["isException"] = true,
                }
            );
        }
    }

    private static async Task<ProbeReport> RunAnthropicTextAsync(string apiKey, ProbePlan plan)
    {
        var client = new AnthropicClient(apiKey);
        var response = await client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model = plan.Model,
                MaxTokens = 512,
                Temperature = 0.2m,
                Messages = new List<Message> { new(RoleType.User, plan.Prompt) },
                System = new List<SystemMessage> { new("You are a response-shape probe.") },
            }
        );

        return ProbeReport.FromSuccess(plan, response, BuildAnthropicSummary(response));
    }

    private static async Task<ProbeReport> RunAnthropicToolAsync(string apiKey, ProbePlan plan)
    {
        var client = new AnthropicClient(apiKey);
        var tool = new AnthropicTool(
            new Function(
                "record_probe",
                "Record a probe result into structured JSON.",
                JsonNode.Parse(
                    """
                    {
                      "type": "object",
                      "properties": {
                        "status": { "type": "string" },
                        "code": { "type": "integer" }
                      },
                      "required": ["status", "code"]
                    }
                    """
                )
            )
        );

        var response = await client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model = plan.Model,
                MaxTokens = 512,
                Temperature = 0.2m,
                Messages = new List<Message> { new(RoleType.User, plan.Prompt) },
                System = new List<SystemMessage> { new("You are a response-shape probe.") },
                Tools = new List<AnthropicTool> { tool },
                ToolChoice = new ToolChoice
                {
                    Type = ToolChoiceType.Tool,
                    Name = "record_probe",
                },
            }
        );

        return ProbeReport.FromSuccess(plan, response, BuildAnthropicSummary(response));
    }

    private static async Task<ProbeReport> RunAnthropicThinkingAsync(
        string apiKey,
        ProbePlan plan,
        int thinkingBudget
    )
    {
        var client = new AnthropicClient(apiKey);
        var response = await client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model = plan.Model,
                MaxTokens = Math.Max(1024, thinkingBudget + 256),
                Temperature = 1m,
                Messages = new List<Message> { new(RoleType.User, plan.Prompt) },
                System = new List<SystemMessage> { new("You are a response-shape probe.") },
                Thinking = new ThinkingParameters
                {
                    BudgetTokens = thinkingBudget,
                },
            }
        );

        return ProbeReport.FromSuccess(plan, response, BuildAnthropicSummary(response));
    }

    private static async Task<ProbeReport> RunAnthropicErrorAsync(string apiKey, ProbePlan plan)
    {
        var client = new AnthropicClient(apiKey);
        try
        {
            await client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model = "this-model-should-not-exist",
                    MaxTokens = 128,
                    Messages = new List<Message>
                    {
                        new(RoleType.User, "Trigger an invalid model error."),
                    },
                }
            );

            throw new InvalidOperationException(
                "The Anthropic error probe unexpectedly succeeded with an invalid model."
            );
        }
        catch (Exception ex)
        {
            return ProbeReport.FromSuccess(
                plan,
                ExceptionSnapshot.FromException(ex),
                new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().FullName,
                    ["message"] = ex.Message,
                    ["isException"] = true,
                }
            );
        }
    }

    private static Dictionary<string, object?> BuildOpenAiChatSummary(ChatCompletion completion)
    {
        return new Dictionary<string, object?>
        {
            ["finishReason"] = completion.FinishReason.ToString(),
            ["role"] = completion.Role.ToString(),
            ["contentPartTypes"] = completion.Content?.Select(part => part.GetType().FullName).ToList(),
            ["contentText"] = completion.Content?.Select(part => part.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToList(),
            ["toolCallCount"] = completion.ToolCalls.Count,
            ["toolCallKinds"] = completion.ToolCalls.Select(call => call.Kind.ToString()).ToList(),
            ["toolCallNames"] = completion.ToolCalls.Select(call => call.FunctionName).ToList(),
            ["refusal"] = completion.Refusal,
            ["usage"] = ReflectionJsonDumper.ToObjectDictionary(completion.Usage),
        };
    }

    private static Dictionary<string, object?> BuildOpenAiResponsesSummary(ResponseResult response)
    {
        var outputItems = TryGetPropertyValue(response, "OutputItems") as IEnumerable;
        var itemList = outputItems?.Cast<object>().ToList() ?? new List<object>();

        return new Dictionary<string, object?>
        {
            ["responseType"] = response.GetType().FullName,
            ["status"] = TryGetPropertyValue(response, "Status")?.ToString(),
            ["error"] = ReflectionJsonDumper.ToObjectDictionary(TryGetPropertyValue(response, "Error")),
            ["outputText"] = TryGetPropertyValue(response, "OutputText")?.ToString(),
            ["outputItemTypes"] = itemList.Select(item => item.GetType().FullName).ToList(),
            ["usage"] = ReflectionJsonDumper.ToObjectDictionary(TryGetPropertyValue(response, "Usage")),
        };
    }

    private static object BuildOpenAiStreamingUpdateSummary(StreamingResponseUpdate update)
    {
        var summary = new Dictionary<string, object?>
        {
            ["updateType"] = update.GetType().FullName,
        };

        if (TryGetPropertyValue(update, "Item") is { } item)
        {
            summary["itemType"] = item.GetType().FullName;
            summary["item"] = ReflectionJsonDumper.ToJsonCompatible(item);
        }

        if (TryGetPropertyValue(update, "Delta") is { } delta)
        {
            summary["delta"] = delta.ToString();
        }

        if (TryGetPropertyValue(update, "ResponseId") is { } responseId)
        {
            summary["responseId"] = responseId.ToString();
        }

        return summary;
    }

    private static Dictionary<string, object?> BuildAnthropicSummary(object response)
    {
        var content = TryGetPropertyValue(response, "Content") as IEnumerable;
        var items = content?.Cast<object>().ToList() ?? new List<object>();

        return new Dictionary<string, object?>
        {
            ["responseType"] = response.GetType().FullName,
            ["contentTypes"] = items.Select(item => item.GetType().FullName).ToList(),
            ["textSegments"] = items
                .OfType<TextContent>()
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList(),
            ["toolNames"] = items.OfType<ToolUseContent>().Select(tool => tool.Name).ToList(),
            ["thinkingBlocks"] = items.OfType<ThinkingContent>().Select(thinking => thinking.Thinking).ToList(),
            ["redactedThinkingBlockCount"] = items.OfType<RedactedThinkingContent>().Count(),
            ["usage"] = ReflectionJsonDumper.ToObjectDictionary(TryGetPropertyValue(response, "Usage")),
            ["stopReason"] = TryGetPropertyValue(response, "StopReason")?.ToString(),
        };
    }

    private static object? TryGetPropertyValue(object? instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        var property = instance
            .GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

        return property?.CanRead == true ? property.GetValue(instance) : null;
    }

    private static string RequireEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing required environment variable {name}."
            );
        }

        return value;
    }
}

internal sealed record ProbeReport(
    string Provider,
    string Scenario,
    string ApiSurface,
    string Model,
    bool Success,
    Dictionary<string, object?> Summary,
    object? Payload,
    ExceptionSnapshot? Error
)
{
    public static ProbeReport FromSuccess(
        ProbePlan plan,
        object payload,
        Dictionary<string, object?> summary
    ) =>
        new(
            plan.Provider,
            plan.Scenario,
            plan.ApiSurface,
            plan.Model,
            true,
            summary,
            ReflectionJsonDumper.ToJsonCompatible(payload),
            null
        );

    public static ProbeReport Failure(ProbePlan plan, Exception ex) =>
        new(
            plan.Provider,
            plan.Scenario,
            plan.ApiSurface,
            plan.Model,
            false,
            new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message,
            },
            null,
            ExceptionSnapshot.FromException(ex)
        );
}

internal sealed record ExceptionSnapshot(
    string Type,
    string Message,
    Dictionary<string, object?> Properties,
    ExceptionSnapshot? Inner
)
{
    public static ExceptionSnapshot FromException(Exception exception) =>
        new(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            ReflectionJsonDumper.ToObjectDictionary(exception),
            exception.InnerException == null ? null : FromException(exception.InnerException)
        );
}

internal static class ReflectionJsonDumper
{
    private const int MaxDepth = 5;
    private const int MaxCollectionItems = 25;
    private const int MaxStringLength = 4000;

    public static object? ToJsonCompatible(object? value)
    {
        var node = ToJsonNode(value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return node == null ? null : JsonSerializer.Deserialize<object>(node.ToJsonString());
    }

    public static Dictionary<string, object?> ToObjectDictionary(object? value)
    {
        var compatible = ToJsonCompatible(value);
        return compatible as Dictionary<string, object?> ?? new Dictionary<string, object?>
        {
            ["value"] = compatible,
        };
    }

    private static JsonNode? ToJsonNode(
        object? value,
        int depth,
        HashSet<object> seen
    )
    {
        if (value == null)
        {
            return null;
        }

        if (depth >= MaxDepth)
        {
            return JsonValue.Create($"<max-depth:{value.GetType().FullName}>");
        }

        switch (value)
        {
            case string text:
                return JsonValue.Create(
                    text.Length <= MaxStringLength
                        ? text
                        : $"{text[..MaxStringLength]}...<truncated>"
                );
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or
                float or double or decimal:
                return JsonValue.Create((dynamic)value);
            case Guid guid:
                return JsonValue.Create(guid.ToString());
            case DateTime dateTime:
                return JsonValue.Create(dateTime);
            case DateTimeOffset dateTimeOffset:
                return JsonValue.Create(dateTimeOffset);
            case TimeSpan timeSpan:
                return JsonValue.Create(timeSpan.ToString());
            case Enum enumValue:
                return JsonValue.Create(enumValue.ToString());
            case Uri uri:
                return JsonValue.Create(uri.ToString());
            case JsonNode node:
                return node.DeepClone();
            case JsonElement element:
                return JsonNode.Parse(element.GetRawText());
            case BinaryData binaryData:
                return DumpBinaryData(binaryData);
        }

        if (!value.GetType().IsValueType)
        {
            if (!seen.Add(value))
            {
                return JsonValue.Create($"<cycle:{value.GetType().FullName}>");
            }
        }

        if (value is IEnumerable enumerable and not IDictionary)
        {
            return DumpEnumerable(enumerable, depth, seen);
        }

        if (value is IDictionary dictionary)
        {
            return DumpDictionary(dictionary, depth, seen);
        }

        return DumpObject(value, depth, seen);
    }

    private static JsonNode DumpBinaryData(BinaryData binaryData)
    {
        var text = binaryData.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return JsonValue.Create(string.Empty);
        }

        try
        {
            return JsonNode.Parse(text) ?? JsonValue.Create(text);
        }
        catch
        {
            return JsonValue.Create(
                text.Length <= MaxStringLength ? text : $"{text[..MaxStringLength]}...<truncated>"
            );
        }
    }

    private static JsonArray DumpEnumerable(IEnumerable enumerable, int depth, HashSet<object> seen)
    {
        var array = new JsonArray();
        var count = 0;
        foreach (var item in enumerable)
        {
            if (count >= MaxCollectionItems)
            {
                array.Add($"<truncated after {MaxCollectionItems} items>");
                break;
            }

            array.Add(ToJsonNode(item, depth + 1, seen));
            count++;
        }

        return array;
    }

    private static JsonObject DumpDictionary(IDictionary dictionary, int depth, HashSet<object> seen)
    {
        var obj = new JsonObject();
        obj["$type"] = dictionary.GetType().FullName;

        foreach (DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString() ?? "<null>";
            obj[key] = ToJsonNode(entry.Value, depth + 1, seen);
        }

        return obj;
    }

    private static JsonObject DumpObject(object value, int depth, HashSet<object> seen)
    {
        var obj = new JsonObject
        {
            ["$type"] = value.GetType().FullName,
        };

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                obj[property.Name] = ToJsonNode(property.GetValue(value), depth + 1, seen);
            }
            catch (TargetInvocationException ex)
            {
                obj[property.Name] = $"<error:{ex.InnerException?.Message ?? ex.Message}>";
            }
            catch (Exception ex)
            {
                obj[property.Name] = $"<error:{ex.Message}>";
            }
        }

        return obj;
    }
}

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static ReferenceEqualityComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
