using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Mcp.Net.Client;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Examples.LLMConsole.UI;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Catalog;
using Mcp.Net.Agent.Completions;
using Mcp.Net.Agent.Elicitation;
using Mcp.Net.LLM.Models;
using Mcp.Net.Examples.LLMConsole.Elicitation;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Interfaces;
using LLM = Mcp.Net.LLM;
using Mcp.Net.Examples.Shared;
using Mcp.Net.Examples.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.WebUtilities;

namespace Mcp.Net.Examples.LLMConsole;

public class Program
{
    private static Microsoft.Extensions.Logging.ILogger _logger = null!;
    private static Core.Models.Tools.Tool[] AvailableTools { get; set; } =
        Array.Empty<Core.Models.Tools.Tool>();
    private static readonly List<IDisposable> AuthDisposables = new();

    static Program()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAuthDisposables();
    }

    public static async Task Main(string[] args)
    {
        ConfigureLogging(args);

        if (args.Contains("-h") || args.Contains("--help"))
        {
            ConsoleBanner.DisplayHelp();
            return;
        }

        var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: false));
        var chatUI = new ChatUI();

        var options = ConsoleOptions.Parse(args);
        options.ApplyDefaults();
        options.Validate();

        if (options.HasTransportConfigured)
        {
            await RunWithMcpAsync(args, options, loggerFactory, chatUI);
        }
        else
        {
            await RunDirectLlmAsync(args, loggerFactory, chatUI);
        }
    }

    /// <summary>
    /// Runs a direct LLM chat session without an MCP server.
    /// Exercises the Mcp.Net.LLM layer directly for smoke testing.
    /// </summary>
    private static async Task RunDirectLlmAsync(
        string[] args,
        ILoggerFactory loggerFactory,
        ChatUI chatUI
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(chatUI);
        services.AddSingleton<ProviderSelectionService>();
        var serviceProvider = services.BuildServiceProvider();

        var providerSelectionService = serviceProvider.GetRequiredService<ProviderSelectionService>();
        var provider = ResolveProvider(args, providerSelectionService);

        string? apiKey = GetApiKey(provider);
        if (string.IsNullOrEmpty(apiKey))
        {
            PrintApiKeyError(provider);
            return;
        }

        string modelName = GetModelName(args, provider);
        _logger.LogInformation("Using model: {Model}", modelName);

        var chatClientOptions = new ChatClientOptions { ApiKey = apiKey, Model = modelName };
        var chatClient = CreateChatClient(provider, chatClientOptions, loggerFactory);

        chatUI.DrawChatInterface();

        var transcript = new List<ChatTranscriptEntry>();

        while (true)
        {
            var userInput = chatUI.GetUserInput();
            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            if (userInput.Equals(":quit", StringComparison.OrdinalIgnoreCase)
                || userInput.Equals(":exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            transcript.Add(new UserChatEntry(
                Guid.NewGuid().ToString("n"),
                DateTimeOffset.UtcNow,
                userInput
            ));

            var request = new ChatClientRequest(
                systemPrompt: null,
                transcript: transcript
            );

            var stream = chatClient.SendAsync(request);

            // Stream text incrementally as snapshots arrive
            var printedLength = 0;

            await foreach (var snapshot in stream)
            {
                var currentText = ExtractText(snapshot.Blocks);
                if (currentText.Length <= printedLength)
                {
                    continue;
                }

                // Print only the new text delta
                Console.Write(currentText.Substring(printedLength));
                printedLength = currentText.Length;
            }

            var result = await stream.GetResultAsync();

            switch (result)
            {
                case ChatClientAssistantTurn turn:
                    // Print any remaining text not yet streamed
                    var finalText = ExtractText(turn.Blocks);
                    if (finalText.Length > printedLength)
                    {
                        Console.Write(finalText.Substring(printedLength));
                    }

                    if (printedLength > 0 || finalText.Length > 0)
                    {
                        Console.WriteLine();
                    }

                    transcript.Add(new AssistantChatEntry(
                        turn.Id,
                        DateTimeOffset.UtcNow,
                        turn.Blocks,
                        Provider: turn.Provider,
                        Model: turn.Model,
                        StopReason: turn.StopReason,
                        Usage: turn.Usage
                    ));

                    if (turn.Usage is { } usage)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($"  {turn.Provider}/{turn.Model}");
                        Console.Write($"  in:{usage.InputTokens}");
                        Console.Write($" out:{usage.OutputTokens}");
                        Console.WriteLine($" total:{usage.TotalTokens}");
                        Console.ResetColor();
                    }

                    Console.WriteLine();
                    break;

                case ChatClientFailure failure:
                    chatUI.DisplayToolError("LLM", failure.Message);
                    break;
            }
        }
    }

    /// <summary>
    /// Runs a full chat session with MCP server connectivity and tool support.
    /// </summary>
    private static async Task RunWithMcpAsync(
        string[] args,
        ConsoleOptions options,
        ILoggerFactory loggerFactory,
        ChatUI chatUI
    )
    {
        var elicitationCoordinator = new ElicitationCoordinator(
            loggerFactory.CreateLogger<ElicitationCoordinator>()
        );
        var elicitationProvider = new ConsoleElicitationPromptProvider(
            chatUI,
            loggerFactory.CreateLogger<ConsoleElicitationPromptProvider>()
        );
        elicitationCoordinator.SetProvider(elicitationProvider);

        var mcpClient = await ConnectToMcpServer(
            elicitationCoordinator,
            options,
            loggerFactory
        );

        var completionLogger = loggerFactory.CreateLogger<CompletionService>();
        var promptCatalogLogger = loggerFactory.CreateLogger<PromptResourceCatalog>();

        var completionService = new CompletionService(mcpClient, completionLogger);
        var promptCatalog = new PromptResourceCatalog(mcpClient, promptCatalogLogger);
        await promptCatalog.InitializeAsync();

        var toolRegistryLogger = loggerFactory.CreateLogger<ToolRegistry>();
        var toolRegistry = new ToolRegistry(toolRegistryLogger);

        ChatSession? activeChatSession = null;
        toolRegistry.ToolsUpdated += (_, tools) =>
        {
            AvailableTools = tools.ToArray();
            if (activeChatSession != null)
            {
                try
                {
                    activeChatSession.RegisterTools(toolRegistry.EnabledTools);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to re-register tools with the active chat client."
                    );
                }
            }

            _logger.LogInformation("Tool inventory refreshed ({Count} tools)", tools.Count);
        };

        await toolRegistry.RefreshAsync(mcpClient);

        mcpClient.OnNotification += notification =>
        {
            if (notification.Method == "tools/list_changed")
            {
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await toolRegistry.RefreshAsync(mcpClient);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Failed to refresh tools after tools/list_changed notification."
                            );
                        }
                    }
                );
            }
        };

        var availablePrompts = await promptCatalog.GetPromptsAsync();
        var availableResources = await promptCatalog.GetResourcesAsync();

        ConsoleBanner.DisplayStartupBanner(
            AvailableTools,
            null,
            availablePrompts.Count,
            availableResources.Count
        );

        var services = new ServiceCollection();

        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(chatUI);
        services.AddSingleton<ToolSelectionService>();
        services.AddSingleton<ProviderSelectionService>();

        // Build temporary service provider for tool selection
        var tempServiceProvider = services.BuildServiceProvider();

        if (!options.SkipToolSelection && !options.EnableAllTools)
        {
            var toolSelectionService =
                tempServiceProvider.GetRequiredService<ToolSelectionService>();
            var selectedTools = toolSelectionService.PromptForToolSelection(toolRegistry);

            toolRegistry.SetEnabledTools(selectedTools.Select(t => t.Name));

            Console.Clear();
            ConsoleBanner.DisplayStartupBanner(
                AvailableTools,
                toolRegistry.EnabledTools.Select(t => t.Name),
                availablePrompts.Count,
                availableResources.Count
            );
        }

        var providerSelectionService = tempServiceProvider.GetRequiredService<ProviderSelectionService>();
        var provider = ResolveProvider(args, providerSelectionService);

        _logger.LogInformation("Using LLM provider: {Provider}", provider);

        string? apiKey = GetApiKey(provider);
        if (string.IsNullOrEmpty(apiKey))
        {
            PrintApiKeyError(provider);
            return;
        }

        // Get model name from command line args or use default
        string modelName = GetModelName(args, provider);
        _logger.LogInformation("Using model: {Model}", modelName);

        var chatSessionLogger = loggerFactory.CreateLogger<ChatSession>();
        var chatUIHandlerLogger = loggerFactory.CreateLogger<ChatUIHandler>();
        var toolExecutorLogger = loggerFactory.CreateLogger<McpToolExecutor>();

        var chatClientOptions = new ChatClientOptions { ApiKey = apiKey, Model = modelName };
        var chatClient = CreateChatClient(provider, chatClientOptions, loggerFactory);

        var chatSession = new ChatSession(
            chatClient,
            new McpToolExecutor(mcpClient, toolExecutorLogger),
            chatSessionLogger
        );
        chatSession.RegisterTools(toolRegistry.EnabledTools);
        activeChatSession = chatSession;

        var chatUIHandler = new ChatUIHandler(chatUI, chatSession, chatUIHandlerLogger);

        var consoleAdapterLogger = loggerFactory.CreateLogger<ConsoleAdapter>();
        var consoleAdapter = new ConsoleAdapter(
            chatSession,
            chatUI,
            consoleAdapterLogger,
            promptCatalog,
            completionService
        );

        await consoleAdapter.RunAsync();
    }

    private static IChatClient CreateChatClient(
        LlmProvider provider,
        ChatClientOptions options,
        ILoggerFactory loggerFactory
    )
    {
        return provider switch
        {
            LlmProvider.Anthropic => new LLM.Anthropic.AnthropicChatClient(
                options,
                loggerFactory.CreateLogger<LLM.Anthropic.AnthropicChatClient>()
            ),
            _ => new LLM.OpenAI.OpenAiChatClient(
                options,
                loggerFactory.CreateLogger<LLM.OpenAI.OpenAiChatClient>()
            ),
        };
    }

    private static string ExtractText(IReadOnlyList<AssistantContentBlock> blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block is TextAssistantBlock text)
            {
                sb.Append(text.Text);
            }
        }
        return sb.ToString();
    }

    private static void PrintApiKeyError(LlmProvider provider)
    {
        _logger.LogError("Missing API key for {Provider}", provider);
        Console.WriteLine($"Error: Missing API key for {provider}");
        Console.WriteLine(
            $"Please set the {GetApiKeyEnvVarName(provider)} environment variable"
        );
        Console.WriteLine("To set the environment variable:");
        Console.WriteLine(
            $"  - Bash/Zsh: export {GetApiKeyEnvVarName(provider)}=\"your-api-key\""
        );
        Console.WriteLine(
            $"  - PowerShell: $env:{GetApiKeyEnvVarName(provider)} = \"your-api-key\""
        );
        Console.WriteLine(
            $"  - Command Prompt: set {GetApiKeyEnvVarName(provider)}=your-api-key"
        );

        if (provider == LlmProvider.Anthropic)
            Console.WriteLine("Get an API key from: https://console.anthropic.com/");
        else
            Console.WriteLine("Get an API key from: https://platform.openai.com/api-keys");
    }

    private static LlmProvider ResolveProvider(
        string[] args,
        ProviderSelectionService providerSelectionService
    )
    {
        if (TryResolveProviderFromArgsOrEnvironment(args, out var provider, out var source))
        {
            if (!string.IsNullOrEmpty(source))
            {
                _logger.LogDebug(
                    "Resolved LLM provider from {Source}: {Provider}",
                    source,
                    provider
                );
            }

            return provider;
        }

        return providerSelectionService.PromptForProviderSelection();
    }

    internal static bool TryResolveProviderFromArgsOrEnvironment(
        string[] args,
        out LlmProvider provider,
        out string? source
    )
    {
        provider = LlmProvider.Anthropic;
        source = null;

        for (int i = 0; i < args.Length; i++)
        {
            string providerName = string.Empty;

            if (args[i].StartsWith("--provider="))
            {
                providerName = args[i].Split('=')[1].ToLowerInvariant();
            }
            else if (args[i] == "--provider" && i + 1 < args.Length)
            {
                providerName = args[i + 1].ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(providerName))
            {
                if (providerName == "openai")
                {
                    provider = LlmProvider.OpenAI;
                    source = "command-line";
                    return true;
                }

                if (providerName == "anthropic")
                {
                    provider = LlmProvider.Anthropic;
                    source = "command-line";
                    return true;
                }

                Console.WriteLine(
                    $"Unrecognized provider '{providerName}'. Using default provider (Anthropic)."
                );
                provider = LlmProvider.Anthropic;
                source = "command-line (fallback)";
                return true;
            }
        }

        var providerEnv = Environment.GetEnvironmentVariable("LLM_PROVIDER")?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(providerEnv))
        {
            if (providerEnv == "openai")
            {
                provider = LlmProvider.OpenAI;
                source = "environment";
                return true;
            }

            if (providerEnv == "anthropic")
            {
                provider = LlmProvider.Anthropic;
                source = "environment";
                return true;
            }

            Console.WriteLine(
                $"Unrecognized LLM_PROVIDER '{providerEnv}'. Using default provider (Anthropic)."
            );
            provider = LlmProvider.Anthropic;
            source = "environment (fallback)";
            return true;
        }

        return false;
    }

    internal static LlmProvider PeekProvider(string[] args) =>
        TryResolveProviderFromArgsOrEnvironment(args, out var provider, out _)
            ? provider
            : LlmProvider.Anthropic;

    private static string? GetApiKey(LlmProvider provider)
    {
        return provider switch
        {
            LlmProvider.Anthropic => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            _ => Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        };
    }

    private static string GetApiKeyEnvVarName(LlmProvider provider)
    {
        return provider switch
        {
            LlmProvider.Anthropic => "ANTHROPIC_API_KEY",
            _ => "OPENAI_API_KEY",
        };
    }

    public static string GetModelName(string[] args, LlmProvider provider)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string modelName = "";

            if (args[i].StartsWith("--model="))
            {
                modelName = args[i].Split('=')[1];
            }
            else if (args[i] == "--model" && i + 1 < args.Length)
            {
                modelName = args[i + 1];
            }
            else if (args[i] == "-m" && i + 1 < args.Length)
            {
                modelName = args[i + 1];
            }

            if (!string.IsNullOrEmpty(modelName))
            {
                return modelName;
            }
        }

        var envModel = Environment.GetEnvironmentVariable("LLM_MODEL");
        if (!string.IsNullOrEmpty(envModel))
        {
            return envModel;
        }

        return GetDefaultModel(provider);
    }

    /// <summary>
    /// Defaults to Claude Sonnet 4.5 for Anthropic and GPT-5 for OpenAI
    /// </summary>
    private static string GetDefaultModel(LlmProvider provider)
    {
        return provider switch
        {
            LlmProvider.OpenAI => "gpt-5",
            LlmProvider.Anthropic => "claude-sonnet-4-5-20250929",
            _ => "gpt-5",
        };
    }

    private static async Task<IMcpClient> ConnectToMcpServer(
        ElicitationCoordinator coordinator,
        ConsoleOptions options,
        ILoggerFactory loggerFactory
    )
    {
        var clientLogger = loggerFactory.CreateLogger("LLMConsole.McpClient");
        var builder = new McpClientBuilder()
            .WithName("LLMConsole")
            .WithVersion("1.0.0")
            .WithTitle("LLM Console Sample")
            .WithLogger(clientLogger)
            .WithElicitationHandler(coordinator.HandleAsync);

        HttpClient? pkceProviderHttpClient = null;
        HttpClient? pkceInteractionHttpClient = null;

        if (!string.IsNullOrWhiteSpace(options.ServerCommand))
        {
            builder.UseStdioTransport(options.ServerCommand);
            _logger.LogInformation(
                "Using stdio transport via command: {Command}",
                options.ServerCommand
            );
        }
        else if (!string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            builder.UseSseTransport(options.ServerUrl);
            _logger.LogInformation("Using SSE transport against {Url}", options.ServerUrl);

            if (options.AuthMode != ConsoleAuthMode.None)
            {
                var serverUri = EnsureAbsoluteUri(options.ServerUrl);
                var baseUri = new UriBuilder(serverUri)
                {
                    Path = string.Empty,
                    Query = string.Empty,
                    Fragment = string.Empty,
                }.Uri;

                switch (options.AuthMode)
                {
                    case ConsoleAuthMode.ClientCredentials:
                        _logger.LogInformation("Using demo OAuth client credentials flow.");
                        var clientCredentialsOptions = DemoOAuthDefaults.CreateClientOptions(
                            baseUri
                        );
                        var credentialsHttpClient = new HttpClient();
                        RegisterAuthDisposable(credentialsHttpClient);
                        builder.WithClientCredentialsAuth(
                            clientCredentialsOptions,
                            credentialsHttpClient
                        );
                        break;

                    case ConsoleAuthMode.AuthorizationCodePkce:
                        _logger.LogInformation("Using demo OAuth authorization code flow (PKCE).");

                        pkceProviderHttpClient = new HttpClient();
                        pkceInteractionHttpClient = new HttpClient(
                            new HttpClientHandler { AllowAutoRedirect = false }
                        );
                        RegisterAuthDisposable(pkceProviderHttpClient);
                        RegisterAuthDisposable(pkceInteractionHttpClient);

                        var pkceOptions = DemoOAuthDefaults.CreateClientOptions(baseUri);
                        pkceOptions.RedirectUri = DemoOAuthDefaults.DefaultRedirectUri;

                        var registration = await DemoDynamicClientRegistrar.RegisterAsync(
                            baseUri,
                            "LLM Console PKCE Sample",
                            pkceOptions.RedirectUri,
                            DemoOAuthDefaults.Scopes,
                            pkceProviderHttpClient,
                            CancellationToken.None
                        );

                        pkceOptions.ClientId = registration.ClientId;
                        pkceOptions.ClientSecret = registration.ClientSecret;
                        pkceOptions.RedirectUri = registration.RedirectUris.First();

                        builder.WithAuthorizationCodeAuth(
                            pkceOptions,
                            CreatePkceInteractionHandler(pkceInteractionHttpClient),
                            pkceProviderHttpClient
                        );
                        break;
                }
            }
            else
            {
                _logger.LogInformation(
                    "Authentication disabled; requests will be sent without bearer tokens."
                );
            }
        }
        else
        {
            throw new InvalidOperationException(
                "No transport configured. Specify --url or --command."
            );
        }

        var mcpClient = await builder.BuildAndInitializeAsync();

        var serverName = mcpClient.ServerInfo?.Name ?? "Unknown";
        var serverVersion = mcpClient.ServerInfo?.Version ?? "n/a";
        var protocol = mcpClient.NegotiatedProtocolVersion ?? "unknown";

        _logger.LogInformation(
            "Connected to MCP server {Server} v{Version} (protocol {Protocol})",
            serverName,
            serverVersion,
            protocol
        );

        if (!string.IsNullOrWhiteSpace(mcpClient.Instructions))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Server instructions:");
            Console.ResetColor();
            Console.WriteLine(mcpClient.Instructions);
            Console.WriteLine();
        }

        return mcpClient;
    }

    private static void ConfigureLogging(string[] args)
    {
        var logLevel = DetermineLogLevel(args);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code
            );

        Log.Logger = loggerConfig.CreateLogger();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        _logger = loggerFactory.CreateLogger<Program>();

        _logger.LogDebug("Logging configured at level: {LogLevel}", logLevel);

        _logger.LogDebug(
            "For more detailed logging, use --debug, --verbose, or --log-level=debug/info"
        );
    }

    public static LogEventLevel DetermineLogLevel(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--log-level" || args[i] == "-l") && i + 1 < args.Length)
            {
                return ParseLogLevel(args[i + 1]);
            }
            else if (args[i].StartsWith("--log-level="))
            {
                return ParseLogLevel(args[i].Split('=')[1]);
            }
            else if (args[i] == "--debug" || args[i] == "-d")
            {
                return LogEventLevel.Debug;
            }
            else if (args[i] == "--verbose" || args[i] == "-v")
            {
                return LogEventLevel.Verbose;
            }
        }

        var envLogLevel = Environment.GetEnvironmentVariable("LLM_LOG_LEVEL");
        if (!string.IsNullOrEmpty(envLogLevel))
        {
            return ParseLogLevel(envLogLevel);
        }

        //default = warning
        return LogEventLevel.Warning;
    }

    private static LogEventLevel ParseLogLevel(string level)
    {
        return level.ToLower() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "info" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "warn" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information,
        };
    }

    private static Uri EnsureAbsoluteUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        if (Uri.TryCreate($"http://{url}", UriKind.Absolute, out uri))
        {
            return uri;
        }

        throw new InvalidOperationException(
            $"The server URL '{url}' is not a valid absolute URI. Specify a value such as http://localhost:5000/mcp."
        );
    }

    private static void RegisterAuthDisposable(IDisposable disposable)
    {
        if (disposable == null)
        {
            return;
        }

        lock (AuthDisposables)
        {
            AuthDisposables.Add(disposable);
        }
    }

    private static void DisposeAuthDisposables()
    {
        lock (AuthDisposables)
        {
            foreach (var disposable in AuthDisposables)
            {
                try
                {
                    disposable.Dispose();
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }

            AuthDisposables.Clear();
        }
    }

    private static Func<
        AuthorizationCodeRequest,
        CancellationToken,
        Task<AuthorizationCodeResult>
    > CreatePkceInteractionHandler(HttpClient httpClient)
    {
        return async (request, cancellationToken) =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, request.AuthorizationUri);
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            if (!IsRedirectStatus(response.StatusCode))
            {
                throw new InvalidOperationException(
                    $"Authorization endpoint responded with status {response.StatusCode}."
                );
            }

            var location = response.Headers.Location
                ?? throw new InvalidOperationException(
                    "Authorization server did not provide a redirect location."
                );

            if (!location.IsAbsoluteUri)
            {
                if (request.RedirectUri == null)
                {
                    throw new InvalidOperationException(
                        "Redirect URI is relative and no base redirect URI is available."
                    );
                }

                location = new Uri(request.RedirectUri, location);
            }

            var query = QueryHelpers.ParseQuery(location.Query);
            if (!query.TryGetValue("code", out var codeValues))
            {
                throw new InvalidOperationException(
                    "Authorization server did not return an authorization code."
                );
            }

            var code = codeValues.ToString();
            var returnedState = query.TryGetValue("state", out var stateValues)
                ? stateValues.ToString()
                : string.Empty;

            return new AuthorizationCodeResult(code, returnedState);
        };
    }

    private static bool IsRedirectStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.MovedPermanently
        || statusCode == HttpStatusCode.Found
        || statusCode == HttpStatusCode.SeeOther
        || statusCode == HttpStatusCode.TemporaryRedirect
        || (int)statusCode == 308;
}
