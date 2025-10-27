using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.LLM.Core;
using Mcp.Net.LLM.Events;
using Mcp.Net.LLM.Interfaces;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.LLMConsole.UI;

/// <summary>
/// Console-specific adapter for the ChatSession that handles input/output
/// </summary>
public class ConsoleAdapter : IDisposable
{
    private readonly ChatSession _chatSession;
    private readonly ChatUI _chatUI;
    private readonly ILogger<ConsoleAdapter> _logger;
    private readonly IPromptResourceCatalog _catalog;
    private readonly ICompletionService _completionService;
    private readonly CancellationTokenSource _cts = new();
    private Task? _runTask;

    public ConsoleAdapter(
        ChatSession chatSession,
        ChatUI chatUI,
        ILogger<ConsoleAdapter> logger,
        IPromptResourceCatalog catalog,
        ICompletionService completionService
    )
    {
        _chatSession = chatSession;
        _chatUI = chatUI;
        _logger = logger;
        _catalog = catalog;
        _completionService = completionService;

        // Subscribe to events
        _chatSession.SessionStarted += OnSessionStarted;
        _chatSession.UserMessageReceived += OnUserMessageReceived;
        _chatSession.AssistantMessageReceived += OnAssistantMessageReceived;
        _chatSession.ToolExecutionUpdated += OnToolExecutionUpdated;
        _chatSession.ThinkingStateChanged += OnThinkingStateChanged;
        _catalog.PromptsUpdated += OnPromptCatalogUpdated;
        _catalog.ResourcesUpdated += OnResourceCatalogUpdated;
    }

    /// <summary>
    /// Start the console input loop
    /// </summary>
    public async Task RunAsync()
    {
        _logger.LogDebug("Starting console adapter");

        // Start the chat session
        _chatSession.StartSession();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // Get user input from the console
                var userInput = _chatUI.GetUserInput();

                if (string.IsNullOrWhiteSpace(userInput))
                {
                    continue;
                }

                if (await TryHandleCommandAsync(userInput.Trim()))
                {
                    continue;
                }

                // Process the user message
                await _chatSession.SendUserMessageAsync(userInput);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Console adapter operation canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in console adapter: {Message}", ex.Message);
        }
    }

    private async Task<bool> TryHandleCommandAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var firstChar = input[0];
        if (firstChar is not ':' and not '/')
        {
            return false;
        }

        var tokens = SplitCommandArguments(input[1..]);
        if (tokens.Length == 0)
        {
            return true;
        }

        var command = tokens[0].ToLowerInvariant();
        try
        {
            switch (command)
            {
                case "help":
                    PrintCommandHelp();
                    break;
                case "prompts":
                    await ShowPromptsAsync();
                    break;
                case "prompt":
                    await ShowPromptDetailAsync(tokens);
                    break;
                case "resources":
                    await ShowResourcesAsync();
                    break;
                case "resource":
                    await ShowResourceDetailAsync(tokens);
                    break;
                case "complete":
                    await HandleCompletionCommandAsync(tokens);
                    break;
                default:
                    Console.WriteLine("Unknown command. Type :help for supported commands.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command processing failed for input: {Input}", input);
            Console.WriteLine($"Command failed: {ex.Message}");
        }

        return true;
    }

    private static string[] SplitCommandArguments(string commandBody)
    {
        var args = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandBody)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    args.Add(builder.ToString());
                    builder.Clear();
                }
                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            args.Add(builder.ToString());
        }

        return args.ToArray();
    }

    private void PrintCommandHelp()
    {
        Console.WriteLine("Console commands:");
        Console.WriteLine("  :prompts                 List available prompts");
        Console.WriteLine("  :prompt <name>           Show prompt details and arguments");
        Console.WriteLine("  :resources               List MCP resources");
        Console.WriteLine("  :resource <uri>          Show resource metadata");
        Console.WriteLine(
            "  :complete prompt <name> <argument> [value] [--context key=value,...]"
        );
        Console.WriteLine(
            "  :complete resource <uri> <argument> [value] [--context key=value,...]"
        );
        Console.WriteLine("  :help                    Display this help message");
    }

    private async Task ShowPromptsAsync()
    {
        var prompts = await _catalog.GetPromptsAsync();
        if (prompts.Count == 0)
        {
            Console.WriteLine("No prompts available from the server.");
            return;
        }

        Console.WriteLine("Prompts:");
        foreach (var prompt in prompts)
        {
            Console.WriteLine($"- {prompt.Name} ({prompt.Title ?? "untitled"})");
            if (!string.IsNullOrWhiteSpace(prompt.Description))
            {
                Console.WriteLine($"  {prompt.Description}");
            }

            if (prompt.Arguments is { Length: > 0 })
            {
                foreach (var argument in prompt.Arguments)
                {
                    var requirement = argument.Required ? "required" : "optional";
                    Console.WriteLine($"    • {argument.Name} ({requirement})");
                }
            }
        }
    }

    private async Task ShowPromptDetailAsync(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            Console.WriteLine("Usage: :prompt <name>");
            return;
        }

        var promptName = tokens[1];
        var prompts = await _catalog.GetPromptsAsync();
        var descriptor = prompts.FirstOrDefault(p => p.Name.Equals(promptName, StringComparison.OrdinalIgnoreCase));

        if (descriptor == null)
        {
            Console.WriteLine($"Prompt '{promptName}' not found.");
            return;
        }

        Console.WriteLine($"Prompt: {descriptor.Name}");
        if (!string.IsNullOrWhiteSpace(descriptor.Title))
        {
            Console.WriteLine($"Title: {descriptor.Title}");
        }
        if (!string.IsNullOrWhiteSpace(descriptor.Description))
        {
            Console.WriteLine($"Description: {descriptor.Description}");
        }

        if (descriptor.Arguments is { Length: > 0 })
        {
            Console.WriteLine("Arguments:");
            foreach (var argument in descriptor.Arguments)
            {
                var requirement = argument.Required ? "required" : "optional";
                Console.WriteLine($"  - {argument.Name} [{requirement}]");
                if (!string.IsNullOrWhiteSpace(argument.Description))
                {
                    Console.WriteLine($"    {argument.Description}");
                }

                if (argument.Default is { } defaultValue)
                {
                    Console.WriteLine($"    Default: {defaultValue}");
                }
            }
        }

        var payload = await _catalog.GetPromptMessagesAsync(promptName);
        if (payload.Length > 0)
        {
            Console.WriteLine("Prompt messages:");
            foreach (var message in payload)
            {
                Console.WriteLine($"  - {message}");
            }
        }
    }

    private async Task ShowResourcesAsync()
    {
        var resources = await _catalog.GetResourcesAsync();
        if (resources.Count == 0)
        {
            Console.WriteLine("No resources available from the server.");
            return;
        }

        Console.WriteLine("Resources:");
        foreach (var resource in resources)
        {
            Console.WriteLine($"- {resource.Uri}");
            if (!string.IsNullOrWhiteSpace(resource.Name))
            {
                Console.WriteLine($"  Name: {resource.Name}");
            }
            if (!string.IsNullOrWhiteSpace(resource.Description))
            {
                Console.WriteLine($"  {resource.Description}");
            }
            if (!string.IsNullOrWhiteSpace(resource.MimeType))
            {
                Console.WriteLine($"  Mime: {resource.MimeType}");
            }
        }
    }

    private async Task ShowResourceDetailAsync(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            Console.WriteLine("Usage: :resource <uri>");
            return;
        }

        var uri = tokens[1];
        var resources = await _catalog.GetResourcesAsync();
        var descriptor = resources.FirstOrDefault(r => r.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
        {
            Console.WriteLine($"Resource '{uri}' not found.");
            return;
        }

        Console.WriteLine($"Resource: {descriptor.Uri}");
        if (!string.IsNullOrWhiteSpace(descriptor.Name))
        {
            Console.WriteLine($"Name: {descriptor.Name}");
        }
        if (!string.IsNullOrWhiteSpace(descriptor.Description))
        {
            Console.WriteLine($"Description: {descriptor.Description}");
        }
        if (!string.IsNullOrWhiteSpace(descriptor.MimeType))
        {
            Console.WriteLine($"Mime Type: {descriptor.MimeType}");
        }

        try
        {
            var contents = await _catalog.ReadResourceAsync(uri);
            foreach (var content in contents)
            {
                if (!string.IsNullOrWhiteSpace(content.Text))
                {
                    Console.WriteLine("Content:");
                    Console.WriteLine(content.Text);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read resource {Uri}", uri);
        }
    }

    private async Task HandleCompletionCommandAsync(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 4)
        {
            Console.WriteLine(
                "Usage: :complete <prompt|resource> <identifier> <argument> [value] [--context key=value,...]"
            );
            return;
        }

        var scope = tokens[1].ToLowerInvariant();
        var identifier = tokens[2];
        var argumentName = tokens[3];

        string currentValue;
        Dictionary<string, string>? context = null;

        if (tokens.Count > 4)
        {
            var valueBuilder = new List<string>();
            var index = 4;
            for (; index < tokens.Count; index++)
            {
                if (tokens[index].Equals("--context", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    break;
                }

                valueBuilder.Add(tokens[index]);
            }

            currentValue = string.Join(' ', valueBuilder);

            if (index < tokens.Count)
            {
                context = ParseContextArguments(tokens, index);
            }
        }
        else
        {
            currentValue = string.Empty;
        }

        CompletionValues result;
        switch (scope)
        {
            case "prompt":
                result = await _completionService.CompletePromptAsync(
                    identifier,
                    argumentName,
                    currentValue,
                    context
                );
                break;
            case "resource":
                result = await _completionService.CompleteResourceAsync(
                    identifier,
                    argumentName,
                    currentValue,
                    context
                );
                break;
            default:
                Console.WriteLine("Scope must be either 'prompt' or 'resource'.");
                return;
        }

        Console.WriteLine("Completions:");
        foreach (var value in result.Values)
        {
            Console.WriteLine($"  - {value}");
        }

        if (result.Total.HasValue)
        {
            Console.WriteLine($"Total matches: {result.Total}");
        }
        if (result.HasMore.HasValue)
        {
            Console.WriteLine(result.HasMore.Value ? "More results available." : "No additional results.");
        }
    }

    private static Dictionary<string, string>? ParseContextArguments(IReadOnlyList<string> tokens, int startIndex)
    {
        if (startIndex >= tokens.Count)
        {
            return null;
        }

        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = startIndex; i < tokens.Count; i++)
        {
            var pairs = tokens[i].Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var kvp = pair.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (kvp.Length == 2)
                {
                    context[kvp[0]] = kvp[1];
                }
            }
        }

        return context.Count == 0 ? null : context;
    }

    /// <summary>
    /// Start the console adapter in a background task
    /// </summary>
    public void Start()
    {
        _runTask = Task.Run(async () => await RunAsync(), _cts.Token);
    }

    // Event handlers
    private void OnSessionStarted(object? sender, EventArgs e)
    {
        _logger.LogDebug("Session started");
    }

    private void OnUserMessageReceived(object? sender, string message)
    {
        _logger.LogDebug("User message received: {Message}", message);
    }

    private void OnAssistantMessageReceived(object? sender, string message)
    {
        _logger.LogDebug("Assistant message received: {Message}", message);
    }

    private void OnToolExecutionUpdated(object? sender, ToolExecutionEventArgs args)
    {
        _logger.LogDebug(
            "Tool execution updated: {ToolName}, Success: {Success}",
            args.ToolName,
            args.Success
        );
    }

    private void OnThinkingStateChanged(object? sender, ThinkingStateEventArgs args)
    {
        _logger.LogDebug(
            "Thinking state changed: {IsThinking}, Context: {Context}",
            args.IsThinking,
            args.Context
        );
    }

    private void OnPromptCatalogUpdated(object? sender, IReadOnlyList<Prompt> prompts)
    {
        Console.WriteLine($"[catalog] Prompt list refreshed ({prompts.Count} total).");
    }

    private void OnResourceCatalogUpdated(object? sender, IReadOnlyList<Resource> resources)
    {
        Console.WriteLine($"[catalog] Resource list refreshed ({resources.Count} total).");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing console adapter");

        // Unsubscribe from events
        _chatSession.SessionStarted -= OnSessionStarted;
        _chatSession.UserMessageReceived -= OnUserMessageReceived;
        _chatSession.AssistantMessageReceived -= OnAssistantMessageReceived;
        _chatSession.ToolExecutionUpdated -= OnToolExecutionUpdated;
        _chatSession.ThinkingStateChanged -= OnThinkingStateChanged;
        _catalog.PromptsUpdated -= OnPromptCatalogUpdated;
        _catalog.ResourcesUpdated -= OnResourceCatalogUpdated;

        // Cancel and wait for the task to complete
        _cts.Cancel();
        try
        {
            _runTask?.Wait(1000);
        }
        catch (AggregateException) { }

        _cts.Dispose();
    }
}
