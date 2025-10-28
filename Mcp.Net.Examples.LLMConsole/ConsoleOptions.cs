using System.Globalization;

namespace Mcp.Net.Examples.LLMConsole;

internal enum ConsoleAuthMode
{
    ClientCredentials,
    AuthorizationCodePkce,
    None,
}

internal sealed class ConsoleOptions
{
    public string? ServerUrl { get; private set; }
    public string? ServerCommand { get; private set; }
    public ConsoleAuthMode AuthMode { get; private set; } = ConsoleAuthMode.ClientCredentials;
    public bool SkipToolSelection { get; private set; }
    public bool EnableAllTools => SkipToolSelection && _enableAllToolsRequested;

    private bool _enableAllToolsRequested;

    public static ConsoleOptions Parse(string[] args)
    {
        var options = new ConsoleOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url" when i + 1 < args.Length:
                    options.ServerUrl = args[++i];
                    break;

                case "--command" when i + 1 < args.Length:
                case "--stdio" when i + 1 < args.Length:
                    options.ServerCommand = args[++i];
                    break;

                case "--auth-mode" when i + 1 < args.Length:
                    options.AuthMode = ParseAuthMode(args[++i]);
                    break;

                case "--no-auth":
                    options.AuthMode = ConsoleAuthMode.None;
                    break;

                case "--pkce":
                    options.AuthMode = ConsoleAuthMode.AuthorizationCodePkce;
                    break;

                case "--all-tools":
                    options.SkipToolSelection = true;
                    options._enableAllToolsRequested = true;
                    break;

                case "--skip-tool-selection":
                    options.SkipToolSelection = true;
                    break;
            }
        }

        return options;
    }

    public void ApplyDefaults()
    {
        if (!HasTransportConfigured)
        {
            string defaultPort = Environment.GetEnvironmentVariable("MCP_PORT") ?? "5000";
            ServerUrl = $"http://localhost:{defaultPort}/mcp";
        }
    }

    public void Validate()
    {
        if (!HasTransportConfigured)
        {
            throw new InvalidOperationException(
                "No transport configured. Specify --url for SSE or --command for stdio."
            );
        }
    }

    public bool HasTransportConfigured =>
        !string.IsNullOrWhiteSpace(ServerUrl) || !string.IsNullOrWhiteSpace(ServerCommand);

    private static ConsoleAuthMode ParseAuthMode(string value)
    {
        return value.ToLower(CultureInfo.InvariantCulture) switch
        {
            "none" or "anonymous" => ConsoleAuthMode.None,
            "pkce" or "code" => ConsoleAuthMode.AuthorizationCodePkce,
            _ => ConsoleAuthMode.ClientCredentials,
        };
    }
}
