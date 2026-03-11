using Mcp.Net.LLM.Models;
using Serilog.Events;

namespace Mcp.Net.Examples.LLMConsole.UI;

public static class ConsoleBanner
{
    private static readonly ConsoleColor Dim = ConsoleColor.DarkGray;
    private static readonly ConsoleColor Accent = ConsoleColor.Cyan;
    private static readonly ConsoleColor Ok = ConsoleColor.Green;
    private static readonly ConsoleColor Err = ConsoleColor.Red;

    public static void DisplayStartupBanner(
        Mcp.Net.Core.Models.Tools.Tool[] availableTools,
        IEnumerable<string>? enabledToolNames = null,
        int promptCount = 0,
        int resourceCount = 0
    )
    {
        Console.WriteLine();

        // Title
        WriteColored("  mcp.net", Accent);
        WriteColoredLine(" llm console", ConsoleColor.White);
        WriteColoredLine("  ─────────────────────────────", Dim);

        // Config
        var provider = Program.PeekProvider(Environment.GetCommandLineArgs());
        var model = Program.GetModelName(Environment.GetCommandLineArgs(), provider);

        WriteConfig("provider", provider.ToString());
        WriteConfig("model", model.Length > 40 ? model[..37] + "..." : model);

        // API key status
        var hasKey = provider == LlmProvider.Anthropic
            ? !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
            : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        WriteColored("  api key   ", Dim);
        if (hasKey)
        {
            WriteColoredLine("configured", Ok);
        }
        else
        {
            WriteColoredLine("missing", Err);
        }

        // MCP resources
        if (promptCount > 0 || resourceCount > 0)
        {
            WriteConfig("prompts", promptCount.ToString());
            WriteConfig("resources", resourceCount.ToString());
        }

        // Tools
        if (availableTools.Length > 0)
        {
            Console.WriteLine();
            HashSet<string>? enabled = enabledToolNames != null
                ? new HashSet<string>(enabledToolNames, StringComparer.OrdinalIgnoreCase)
                : null;

            var enabledCount = enabled?.Count ?? availableTools.Length;
            WriteColored("  tools ", Dim);
            WriteColoredLine($"{enabledCount}/{availableTools.Length} enabled", ConsoleColor.White);

            foreach (var tool in availableTools)
            {
                var isEnabled = enabled == null || enabled.Contains(tool.Name);
                var indicator = isEnabled ? "+" : "-";
                var color = isEnabled ? ConsoleColor.White : Dim;
                var indicatorColor = isEnabled ? Ok : Dim;

                WriteColored($"    {indicator} ", indicatorColor);
                WriteColored(tool.Name, color);
                if (!string.IsNullOrWhiteSpace(tool.Description))
                {
                    var desc = tool.Description;
                    if (desc.Length > 50) desc = desc[..47] + "...";
                    WriteColored($"  {desc}", Dim);
                }
                Console.WriteLine();
            }
        }

        Console.WriteLine();
    }

    public static void DisplayHelp()
    {
        WriteColored("mcp.net", Accent);
        WriteColoredLine(" llm console", ConsoleColor.White);
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run --project Mcp.Net.Examples.LLMConsole [options]");
        Console.WriteLine();

        WriteColoredLine("Connection:", ConsoleColor.White);
        WriteHelpLine("--url <url>", "Connect to MCP server via SSE");
        WriteHelpLine("--command <cmd>", "Launch stdio server process");
        WriteHelpLine("(none)", "Direct LLM mode, no MCP server");
        Console.WriteLine();

        WriteColoredLine("Provider:", ConsoleColor.White);
        WriteHelpLine("--provider <name>", "anthropic or openai");
        WriteHelpLine("-m, --model <name>", "Model name override");
        Console.WriteLine();

        WriteColoredLine("Auth:", ConsoleColor.White);
        WriteHelpLine("--no-auth", "Disable authentication");
        WriteHelpLine("--pkce", "Use authorization code with PKCE");
        Console.WriteLine();

        WriteColoredLine("Tools:", ConsoleColor.White);
        WriteHelpLine("--all-tools", "Enable all tools, skip selection");
        WriteHelpLine("--skip-tool-selection", "Same as --all-tools");
        Console.WriteLine();

        WriteColoredLine("Logging:", ConsoleColor.White);
        WriteHelpLine("-d, --debug", "Debug log level");
        WriteHelpLine("-v, --verbose", "Verbose log level");
        WriteHelpLine("-l, --log-level <lvl>", "verbose|debug|info|warning|error|fatal");
        Console.WriteLine();

        WriteColoredLine("Environment:", ConsoleColor.White);
        WriteHelpLine("MCP_URL", "Default MCP server URL");
        WriteHelpLine("ANTHROPIC_API_KEY", "Anthropic API key");
        WriteHelpLine("OPENAI_API_KEY", "OpenAI API key");
        WriteHelpLine("LLM_PROVIDER", "Default provider (anthropic|openai)");
        WriteHelpLine("LLM_MODEL", "Default model name");
        WriteHelpLine("LLM_LOG_LEVEL", "Default log level");
        Console.WriteLine();

        WriteColoredLine("Examples:", ConsoleColor.White);
        WriteColored("  $ ", Dim);
        Console.WriteLine("dotnet run --project Mcp.Net.Examples.LLMConsole");
        WriteColored("  $ ", Dim);
        Console.WriteLine("dotnet run --project Mcp.Net.Examples.LLMConsole --provider anthropic");
        WriteColored("  $ ", Dim);
        Console.WriteLine("dotnet run --project Mcp.Net.Examples.LLMConsole --url http://localhost:5000/mcp");
        WriteColored("  $ ", Dim);
        Console.WriteLine("dotnet run --project Mcp.Net.Examples.LLMConsole --command \"dotnet run --project ../Mcp.Net.Examples.SimpleServer -- --stdio\"");
    }

    private static void WriteConfig(string label, string value)
    {
        WriteColored($"  {label,-10}", Dim);
        WriteColoredLine(value, ConsoleColor.White);
    }

    private static void WriteHelpLine(string flag, string description)
    {
        WriteColored($"  {flag,-24}", ConsoleColor.White);
        WriteColoredLine(description, Dim);
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    private static void WriteColoredLine(string text, ConsoleColor color)
    {
        WriteColored(text, color);
        Console.WriteLine();
    }
}
