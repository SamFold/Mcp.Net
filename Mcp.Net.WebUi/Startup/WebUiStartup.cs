using Mcp.Net.Agent.Extensions;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Tools;
using Mcp.Net.LLM.Anthropic;
using Mcp.Net.LLM.OpenAI;
using Mcp.Net.WebUi.Authentication;
using Mcp.Net.WebUi.LLM;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.Infrastructure.Notifications;
using Mcp.Net.WebUi.Infrastructure.Persistence;
using Mcp.Net.WebUi.LLM.Factories;
using Mcp.Net.WebUi.LLM.Services;
using Mcp.Net.WebUi.Sessions;
using Mcp.Net.WebUi.Startup.Factories;
using Mcp.Net.WebUi.Startup.Helpers;

namespace Mcp.Net.WebUi.Startup;

public class WebUiStartup
{
    private ILogger<WebUiStartup>? _logger;

    public async Task<WebApplication> CreateApplicationAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureLogging(builder, args);
        ConfigureServices(builder, args);

        var app = builder.Build();
        _logger = app.Services.GetRequiredService<ILogger<WebUiStartup>>();

        await InitializeServicesAsync(app);
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

    private void ConfigureServices(WebApplicationBuilder builder, string[] args)
    {
        // Basic services
        builder.Services.AddControllers();
        builder.Services.AddSignalR();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient("McpOAuthTokens");
        builder.Services.AddHttpClient("McpOAuthRegistration");
        builder.Services.AddHttpClient("McpOAuthInteraction")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

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

        // Agent runtime (IChatSessionFactory, IToolRegistry)
        builder.Services.AddChatRuntimeServices();

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

        // Notifications
        builder.Services.AddSingleton<SessionNotifier>();

        // LLM services
        builder.Services.AddSingleton<IOneOffLlmService, OneOffLlmService>();
        builder.Services.AddSingleton<ITitleGenerationService, TitleGenerationService>();

        // Auth
        builder.Services.AddSingleton<IMcpClientBuilderConfigurator>(sp =>
            new McpAuthenticationService(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<McpAuthenticationService>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                args
            )
        );

        // Session lifecycle
        builder.Services.AddSingleton<SessionHost>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionHost>());
    }

    private async Task InitializeServicesAsync(WebApplication app)
    {
        await InitializeToolRegistryAsync(app);
    }

    private async Task InitializeToolRegistryAsync(WebApplication app)
    {
        var toolRegistry = app.Services.GetRequiredService<ToolRegistry>();
        var configuration = app.Services.GetRequiredService<IConfiguration>();

        try
        {
            _logger!.LogInformation("Loading tools from MCP server...");

            var authConfigurator = app.Services.GetRequiredService<IMcpClientBuilderConfigurator>();

            var tempMcpClient = await McpClientFactory.CreateClientAsync(
                configuration,
                _logger!,
                authConfigurator,
                app.Lifetime.ApplicationStopping
            );

            var tools = await tempMcpClient.ListTools();
            toolRegistry.RegisterTools(tools);

            _logger!.LogInformation(
                "Successfully registered {ToolCount} tools in ToolRegistry",
                tools.Length
            );

            if (tempMcpClient is IDisposable disposableClient)
            {
                disposableClient.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger!.LogError(
                ex,
                "Failed to load tools from MCP server - ToolRegistry will remain empty"
            );
            _logger!.LogWarning("Application will continue with no tools available");
        }
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
}
