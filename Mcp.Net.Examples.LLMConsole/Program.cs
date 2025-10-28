using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Mcp.Net.Client;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Examples.LLMConsole.UI;
using Mcp.Net.LLM.Core;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Elicitation;
using Mcp.Net.Examples.LLMConsole.Elicitation;
using Mcp.Net.LLM.Tools;
using Mcp.Net.LLM.Completions;
using Mcp.Net.LLM.Catalog;
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
        try
        {
            options.Validate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid console options.");
            Console.WriteLine($"Configuration error: {ex.Message}");
            ConsoleBanner.DisplayHelp();
            return;
        }

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

        IChatClient? activeChatClient = null;
        toolRegistry.ToolsUpdated += (_, tools) =>
        {
            AvailableTools = tools.ToArray();
            if (activeChatClient != null)
            {
                try
                {
                    activeChatClient.RegisterTools(toolRegistry.EnabledTools);
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

        Console.WriteLine("Press any key to start chat session...");
        Console.ReadKey(true);

        var provider = DetermineProvider(args);
        _logger.LogInformation("Using LLM provider: {Provider}", provider);

        string? apiKey = GetApiKey(provider);
        if (string.IsNullOrEmpty(apiKey))
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

            return;
        }

        // Get model name from command line args or use default
        string modelName = GetModelName(args, provider);
        _logger.LogInformation("Using model: {Model}", modelName);

        var chatSessionLogger = loggerFactory.CreateLogger<ChatSession>();
        var openAiLogger = loggerFactory.CreateLogger<LLM.OpenAI.OpenAiChatClient>();
        var anthropicLogger = loggerFactory.CreateLogger<LLM.Anthropic.AnthropicChatClient>();
        var chatUIHandlerLogger = loggerFactory.CreateLogger<ChatUIHandler>();

        var chatClientOptions = new ChatClientOptions { ApiKey = apiKey, Model = modelName };
        var chatClient =
            provider == LlmProvider.Anthropic
                ? new LLM.Anthropic.AnthropicChatClient(chatClientOptions, anthropicLogger)
                : new LLM.OpenAI.OpenAiChatClient(chatClientOptions, openAiLogger)
                    as LLM.Interfaces.IChatClient;

        activeChatClient = chatClient;
        chatClient.RegisterTools(toolRegistry.EnabledTools);

        var chatSession = new ChatSession(chatClient, mcpClient, toolRegistry, chatSessionLogger);

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

    public static LlmProvider DetermineProvider(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string providerName = "";

            if (args[i].StartsWith("--provider="))
            {
                providerName = args[i].Split('=')[1].ToLower();
            }
            else if (args[i] == "--provider" && i + 1 < args.Length)
            {
                providerName = args[i + 1].ToLower();
            }

            if (!string.IsNullOrEmpty(providerName))
            {
                if (providerName == "openai")
                    return LlmProvider.OpenAI;
                else if (providerName == "anthropic")
                    return LlmProvider.Anthropic;

                Console.WriteLine(
                    $"Unrecognized provider '{providerName}'. Using default provider (Anthropic)."
                );
                break;
            }
        }

        var providerEnv = Environment.GetEnvironmentVariable("LLM_PROVIDER")?.ToLower();
        if (!string.IsNullOrEmpty(providerEnv))
        {
            if (providerEnv == "openai")
                return LlmProvider.OpenAI;
            else if (providerEnv == "anthropic")
                return LlmProvider.Anthropic;
        }

        // Default to Anthropic
        return LlmProvider.Anthropic;
    }

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
    /// Defaults to Sonnet 3.5 for Anthropic of 4o for OpenAI
    /// </summary>
    private static string GetDefaultModel(LlmProvider provider)
    {
        return provider switch
        {
            LlmProvider.Anthropic => "claude-3-7-sonnet-latest",
            _ => "gpt-4o",
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
