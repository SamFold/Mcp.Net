namespace Mcp.Net.Server.ServerBuilder;

/// <summary>
/// Represents parsed command-line options for the MCP server
/// </summary>
public class CommandLineOptions
{
    /// <summary>
    /// Gets a value indicating whether to use stdio transport
    /// </summary>
    public bool UseStdio { get; private set; }

    /// <summary>
    /// Gets a value indicating whether to run in debug mode
    /// </summary>
    public bool DebugMode { get; private set; }

    /// <summary>
    /// Gets the path to the log file
    /// </summary>
    public string LogPath { get; private set; } = "mcp-server.log";

    /// <summary>
    /// Gets the port to listen on when using HTTP transport
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Gets the hostname to bind to when using HTTP transport
    /// </summary>
    public string? Hostname { get; private set; }

    /// <summary>
    /// Gets the URL scheme to use when using HTTP transport
    /// </summary>
    public string? Scheme { get; private set; }

    /// <summary>
    /// Gets the original command-line arguments
    /// </summary>
    public string[] Args { get; private set; }

    private CommandLineOptions(string[] args)
    {
        Args = args;
    }

    /// <summary>
    /// Parses command-line arguments and returns a CommandLineOptions instance
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <returns>Parsed CommandLineOptions</returns>
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions(args)
        {
            UseStdio = args.Contains("--stdio") || args.Contains("-s"),
            DebugMode = args.Contains("--debug") || args.Contains("-d"),
            LogPath = GetArgumentValue(args, "--log-path") ?? "mcp-server.log",
        };

        // Parse network options
        string? portArg = GetArgumentValue(args, "--port");
        if (portArg != null && int.TryParse(portArg, out int port))
        {
            options.Port = port;
        }

        options.Hostname = GetArgumentValue(args, "--hostname");
        options.Scheme = GetArgumentValue(args, "--scheme");

        return options;
    }

    /// <summary>
    /// Gets the value of a command-line argument
    /// </summary>
    /// <param name="args">Array of command-line arguments</param>
    /// <param name="argName">Name of the argument to find</param>
    /// <returns>The value of the argument, or null if not found</returns>
    private static string? GetArgumentValue(string[] args, string argName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == argName)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
