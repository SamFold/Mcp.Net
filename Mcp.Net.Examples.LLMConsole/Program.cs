using System.Linq;
using Mcp.Net.Client;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Examples.LLMConsole.UI;
using Mcp.Net.LLM.Core;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Elicitation;
using Mcp.Net.Examples.LLMConsole.Elicitation;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Mcp.Net.Examples.LLMConsole;

public class Program
{
    private static Microsoft.Extensions.Logging.ILogger _logger = null!;
    private static Core.Models.Tools.Tool[] AvailableTools { get; set; } =
        Array.Empty<Core.Models.Tools.Tool>();

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

        var elicitationCoordinator = new ElicitationCoordinator(
            loggerFactory.CreateLogger<ElicitationCoordinator>()
        );
        var elicitationProvider = new ConsoleElicitationPromptProvider(
            chatUI,
            loggerFactory.CreateLogger<ConsoleElicitationPromptProvider>()
        );
        elicitationCoordinator.SetProvider(elicitationProvider);

        var mcpClient = await ConnectToMcpServer(elicitationCoordinator);

        var toolRegistry = await LoadMcpTools(mcpClient);

        ConsoleBanner.DisplayStartupBanner(AvailableTools);

        var services = new ServiceCollection();

        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(chatUI);
        services.AddSingleton<ToolSelectionService>();

        // Build temporary service provider for tool selection
        var tempServiceProvider = services.BuildServiceProvider();

        if (!args.Contains("--all-tools") && !args.Contains("--skip-tool-selection"))
        {
            var toolSelectionService =
                tempServiceProvider.GetRequiredService<ToolSelectionService>();
            var selectedTools = toolSelectionService.PromptForToolSelection(AvailableTools);

            toolRegistry.SetEnabledTools(selectedTools.Select(t => t.Name));

            Console.Clear();
            ConsoleBanner.DisplayStartupBanner(
                AvailableTools,
                toolRegistry.EnabledTools.Select(t => t.Name)
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

        chatClient.RegisterTools(toolRegistry.EnabledTools);

        var chatSession = new ChatSession(chatClient, mcpClient, toolRegistry, chatSessionLogger);

        var chatUIHandler = new ChatUIHandler(chatUI, chatSession, chatUIHandlerLogger);

        var consoleAdapterLogger = loggerFactory.CreateLogger<ConsoleAdapter>();
        var consoleAdapter = new ConsoleAdapter(chatSession, chatUI, consoleAdapterLogger);

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

    private static async Task<IMcpClient> ConnectToMcpServer(ElicitationCoordinator coordinator)
    {
        var mcpClient = await new McpClientBuilder()
            .UseSseTransport("http://localhost:5000/")
            .WithElicitationHandler(coordinator.HandleAsync)
            .BuildAndInitializeAsync();

        _logger.LogInformation("Connected to MCP server");
        return mcpClient;
    }

    private static async Task<ToolRegistry> LoadMcpTools(IMcpClient mcpClient)
    {
        var tools = await mcpClient.ListTools();
        _logger.LogInformation("Found {ToolCount} tools on the server", tools.Length);

        var registry = new ToolRegistry();
        registry.RegisterTools(tools);

        // Store tool information in a static property for the banner to access
        AvailableTools = tools;

        return registry;
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
}
