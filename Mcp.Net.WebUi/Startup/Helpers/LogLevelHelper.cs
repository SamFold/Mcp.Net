namespace Mcp.Net.WebUi.Startup.Helpers;

public static class LogLevelHelper
{
    public static LogLevel DetermineLogLevel(string[] args, IConfiguration config)
    {
        // Check command line arguments
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
                return LogLevel.Debug;
            }
            else if (args[i] == "--trace" || args[i] == "--verbose" || args[i] == "-v")
            {
                return LogLevel.Trace;
            }
        }

        // Check environment variables
        var envLogLevel = Environment.GetEnvironmentVariable("LLM_LOG_LEVEL");
        if (!string.IsNullOrEmpty(envLogLevel))
        {
            return ParseLogLevel(envLogLevel);
        }

        // Check configuration
        var configLogLevel = config["Logging:LogLevel:Default"];
        if (!string.IsNullOrEmpty(configLogLevel))
        {
            return ParseLogLevel(configLogLevel);
        }

        return LogLevel.Warning;
    }

    private static LogLevel ParseLogLevel(string levelName)
    {
        return levelName.ToLower() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "information" => LogLevel.Information,
            "info" => LogLevel.Information,
            "warning" => LogLevel.Warning,
            "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            "none" => LogLevel.None,
            _ => LogLevel.Warning,
        };
    }
}