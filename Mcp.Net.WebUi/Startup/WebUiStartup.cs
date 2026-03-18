using Mcp.Net.Agent.Extensions;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.ApiKeys;
using Mcp.Net.LLM.Anthropic;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.OpenAI;
using Mcp.Net.LLM.Platform;
using Mcp.Net.WebUi.LLM;
using Mcp.Net.WebUi.Images;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.Infrastructure.Persistence;
using Mcp.Net.WebUi.LLM.Factories;
using Mcp.Net.WebUi.LLM.Services;
using Mcp.Net.WebUi.Sessions;
using Mcp.Net.WebUi.Startup.Factories;
using Mcp.Net.WebUi.Startup.Helpers;
using Mcp.Net.WebUi.Tools;

namespace Mcp.Net.WebUi.Startup;

public class WebUiStartup
{
    private ILogger<WebUiStartup>? _logger;

    public WebApplication CreateApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureLogging(builder, args);
        ConfigureServices(builder);

        var app = builder.Build();
        _logger = app.Services.GetRequiredService<ILogger<WebUiStartup>>();

        InitializeServices(app);
        ConfigurePipeline(app);

        return app;
    }

    private void ConfigureLogging(WebApplicationBuilder builder, string[] args)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        var logLevel = LogLevelHelper.DetermineLogLevel(args, builder.Configuration);
        builder.Logging.SetMinimumLevel(logLevel);

        builder.Services.Configure<LoggerFilterOptions>(options =>
        {
            options.AddFilter("Microsoft", LogLevel.Warning);
            options.AddFilter("System", LogLevel.Warning);
            options.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

            if (logLevel <= LogLevel.Debug)
            {
                options.AddFilter("Mcp.Net.LLM", LogLevel.Debug);
            }
        });

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var startupLogger = loggerFactory.CreateLogger<WebUiStartup>();
        startupLogger.LogInformation("Log level set to: {LogLevel}", logLevel);
    }

    private void ConfigureServices(WebApplicationBuilder builder)
    {
        // Basic services
        builder.Services.AddControllers();
        builder.Services.AddSignalR();
        builder.Services.AddEndpointsApiExplorer();

        // CORS
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithOrigins("http://localhost:3000");
            });
        });

        // Agent runtime plus tool registry
        builder.Services.AddChatRuntimeServices();
        builder.Services.AddToolRegistry();

        // Shared provider helpers
        builder.Services.AddSingleton<IEnvironmentVariableProvider, EnvironmentVariableProvider>();
        builder.Services.AddSingleton<IApiKeyProvider, DefaultApiKeyProvider>();

        // Image generation
        builder.Services.AddSingleton<GeneratedImageArtifactStore>();
        builder.Services.AddSingleton<IImageGenerationService, OpenAiImageGenerationService>();

        // Local tools
        builder.Services.AddSingleton<IReadOnlyList<ILocalTool>>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            return CreateLocalTools(
                configuration,
                sp.GetRequiredService<ILogger<WebUiStartup>>(),
                sp.GetRequiredService<IImageGenerationService>(),
                sp.GetRequiredService<GeneratedImageArtifactStore>()
            );
        });

        // LLM
        builder.Services.AddSingleton<LlmClientFactory>(sp => new LlmClientFactory(
            sp.GetRequiredService<ILogger<AnthropicChatClient>>(),
            sp.GetRequiredService<ILogger<OpenAiChatClient>>(),
            sp.GetRequiredService<ILogger<LlmClientFactory>>()
        ));
        builder.Services.AddSingleton<ILlmClientProvider>(sp =>
            sp.GetRequiredService<LlmClientFactory>());

        builder.Services.AddSingleton<DefaultLlmSettings>(sp =>
        {
            return LlmSettingsFactory.CreateDefaultSettings(
                builder.Configuration,
                sp.GetRequiredService<ILogger<WebUiStartup>>()
            );
        });

        // Persistence
        builder.Services.AddSingleton<IChatHistoryManager, InMemoryChatHistoryManager>();

        // LLM services
        builder.Services.AddSingleton<ITitleGenerationService, TitleGenerationService>();

        // Session lifecycle
        builder.Services.AddSingleton<SessionHost>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionHost>());
    }

    private void InitializeServices(WebApplication app)
    {
        var toolRegistry = app.Services.GetRequiredService<ToolRegistry>();
        var localTools = app.Services.GetRequiredService<IReadOnlyList<ILocalTool>>();

        var descriptors = localTools.Select(t => t.Descriptor).ToList();
        toolRegistry.RegisterTools(descriptors);

        _logger!.LogInformation(
            "Registered {ToolCount} local tools in ToolRegistry",
            descriptors.Count
        );
    }

    private void ConfigurePipeline(WebApplication app)
    {
        app.UseRouting();
        app.UseCors();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<ChatHub>("/chatHub");
        app.MapGet("/", () => "Mcp.Net Web UI Server - API endpoints are available at /api");
    }

    private static IReadOnlyList<ILocalTool> CreateLocalTools(
        IConfiguration configuration,
        ILogger logger,
        IImageGenerationService imageGenerationService,
        GeneratedImageArtifactStore artifactStore)
    {
        var basePath = configuration["Agent:BasePath"]
            ?? Environment.CurrentDirectory;

        var fileSystemPolicy = new FileSystemToolPolicy(basePath);
        var processPolicy = new ProcessToolPolicy(basePath);

        var tools = new List<ILocalTool>
        {
            new GenerateImageTool(imageGenerationService, artifactStore),
            new ListFilesTool(fileSystemPolicy),
            new GlobTool(fileSystemPolicy),
            new ReadFileTool(fileSystemPolicy),
            new WriteFileTool(fileSystemPolicy),
            new EditFileTool(fileSystemPolicy),
        };

        if (GrepTool.TryCreate(fileSystemPolicy, out var grepTool, out var grepUnavailableReason))
        {
            tools.Add(grepTool!);
        }
        else
        {
            logger.LogWarning(
                "Skipping grep_files tool: {Reason}",
                grepUnavailableReason
            );
        }

        if (RunShellCommandTool.TryCreate(processPolicy, out var shellTool, out var shellUnavailableReason))
        {
            tools.Add(shellTool!);
        }
        else
        {
            logger.LogWarning(
                "Skipping run_shell_command tool: {Reason}",
                shellUnavailableReason
            );
        }

        logger.LogInformation("Created {Count} local tools rooted at {BasePath}", tools.Count, basePath);
        return tools;
    }
}
