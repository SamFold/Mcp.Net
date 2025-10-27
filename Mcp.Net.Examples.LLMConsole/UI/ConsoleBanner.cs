using Mcp.Net.LLM.Models;
using Serilog.Events;

namespace Mcp.Net.Examples.LLMConsole.UI;

public static class ConsoleBanner
{
    // For colorful console output
    private static readonly ConsoleColor DefaultColor = Console.ForegroundColor;
    private static readonly ConsoleColor AccentColor1 = ConsoleColor.Cyan;
    private static readonly ConsoleColor AccentColor2 = ConsoleColor.Magenta;
    private static readonly ConsoleColor HighlightColor = ConsoleColor.Green;
    private static readonly ConsoleColor ErrorColor = ConsoleColor.Red;

    // Critical: These constants define the exact width of the banner - NEVER CHANGE THESE VALUES
    // This is the key to having perfect right border alignment
    private const int BANNER_WIDTH = 74; // Total width including borders and spacing
    private const int CONTENT_WIDTH = 70; // Content area width (BANNER_WIDTH - 4 for borders)

    public static void DisplayStartupBanner(
        Mcp.Net.Core.Models.Tools.Tool[] availableTools,
        IEnumerable<string>? enabledToolNames = null,
        int promptCount = 0,
        int resourceCount = 0
    )
    {
        // Draw a fixed width banner
        Console.WriteLine();

        // Top border with fixed width
        Console.Write("  ");
        ColorWrite("╔", AccentColor1);
        ColorWrite(new string('═', CONTENT_WIDTH), AccentColor1);
        ColorWriteLine("╗", AccentColor1);

        // The MCP logo - centered in the banner
        string[] logoLines = new string[]
        {
            "███╗   ███╗ ██████╗██████╗    ██╗     ██╗     ███╗   ███╗",
            "████╗ ████║██╔════╝██╔══██╗   ██║     ██║     ████╗ ████║",
            "██╔████╔██║██║     ██████╔╝   ██║     ██║     ██╔████╔██║",
            "██║╚██╔╝██║██║     ██╔═══╝    ██║     ██║     ██║╚██╔╝██║",
            "██║ ╚═╝ ██║╚██████╗██║        ███████╗███████╗██║ ╚═╝ ██║",
            "╚═╝     ╚═╝ ╚═════╝╚═╝        ╚══════╝╚══════╝╚═╝     ╚═╝",
        };

        // Display each line of the logo with exact padding to maintain fixed width
        foreach (var line in logoLines)
        {
            // Calculate centering for the logo
            int logoWidth = line.Length;
            int padding = (CONTENT_WIDTH - logoWidth) / 2;

            Console.Write("  ");
            ColorWrite("║", AccentColor1);
            ColorWrite(new string(' ', padding), DefaultColor);
            ColorWrite(line, HighlightColor);
            ColorWrite(new string(' ', CONTENT_WIDTH - logoWidth - padding), DefaultColor);
            ColorWriteLine("║", AccentColor1);
        }

        // Divider with fixed width
        Console.Write("  ");
        ColorWrite("╠", AccentColor1);
        ColorWrite(new string('═', CONTENT_WIDTH), AccentColor1);
        ColorWriteLine("╣", AccentColor1);

        // Application title - centered in the banner
        string appTitle = "MCP - Function Calling Demo";
        DrawCenteredLine(appTitle, AccentColor2);

        // System info line with fixed positioning
        string date = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");
        string versionInfo = "Version: 2.1.0";

        Console.Write("  ");
        ColorWrite("║", AccentColor1);
        ColorWrite(" System Date: ", DefaultColor);
        ColorWrite(date, HighlightColor);

        // Calculate padding to position version info at right edge
        int remainingSpace = CONTENT_WIDTH - 14 - date.Length - versionInfo.Length - 1;
        ColorWrite(new string(' ', remainingSpace), DefaultColor);
        ColorWrite(versionInfo, DefaultColor);
        ColorWrite(" ", DefaultColor);
        ColorWriteLine("║", AccentColor1);

        // Divider with fixed width
        Console.Write("  ");
        ColorWrite("╠", AccentColor1);
        ColorWrite(new string('═', CONTENT_WIDTH), AccentColor1);
        ColorWriteLine("╣", AccentColor1);

        // Runtime configuration section
        DrawCenteredLine("RUNTIME CONFIGURATION", AccentColor2);

        // Get data for display
        var provider = Program.DetermineProvider(Environment.GetCommandLineArgs());
        var model = Program.GetModelName(Environment.GetCommandLineArgs(), provider);
        var logLevel = Program.DetermineLogLevel(Environment.GetCommandLineArgs());

        // Fixed-width config lines
        DrawConfigLine("Provider", provider.ToString(), HighlightColor);

        // Model might be long, so truncate if needed
        string modelDisplay = model;
        if (modelDisplay.Length > 30)
        {
            modelDisplay = modelDisplay.Substring(0, 27) + "...";
        }
        DrawConfigLine("Model", modelDisplay, HighlightColor);

        // Log level
        DrawConfigLine("Logging Level", logLevel.ToString(), HighlightColor);

        DrawConfigLine("Prompts", promptCount.ToString(), HighlightColor);
        DrawConfigLine("Resources", resourceCount.ToString(), HighlightColor);

        // API Keys - check for presence and display
        bool anthropicKeyPresent = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        );
        bool openaiKeyPresent = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        );

        string keyStatus;
        ConsoleColor keyColor;

        if (provider == LlmProvider.Anthropic)
        {
            keyStatus = anthropicKeyPresent ? "✓ (Anthropic)" : "✗ (Anthropic)";
            keyColor = anthropicKeyPresent ? HighlightColor : ErrorColor;
        }
        else
        {
            keyStatus = openaiKeyPresent ? "✓ (OpenAI)" : "✗ (OpenAI)";
            keyColor = openaiKeyPresent ? HighlightColor : ErrorColor;
        }

        DrawConfigLine("API Keys", keyStatus, keyColor);

        // If we have tools available, list them in a fixed-width section
        if (availableTools.Length > 0)
        {
            // Tools divider with fixed width
            Console.Write("  ");
            ColorWrite("╠", AccentColor1);
            ColorWrite(new string('═', CONTENT_WIDTH), AccentColor1);
            ColorWriteLine("╣", AccentColor1);

            // Create a HashSet of enabled tool names for fast lookup
            HashSet<string>? enabledTools = null;
            if (enabledToolNames != null)
            {
                enabledTools = new HashSet<string>(enabledToolNames);
            }

            // Display header with count of enabled tools if applicable
            string toolsHeader = "AVAILABLE TOOLS";
            if (enabledTools != null)
            {
                toolsHeader = $"TOOLS ({enabledTools.Count} OF {availableTools.Length} ENABLED)";
            }

            DrawCenteredLine(toolsHeader, AccentColor2);

            // Display tools with fixed width
            for (int i = 0; i < availableTools.Length; i++)
            {
                bool isEnabled =
                    enabledTools == null || enabledTools.Contains(availableTools[i].Name);
                DrawToolLine(availableTools[i], isEnabled);
            }
        }

        // Bottom border with fixed width
        Console.Write("  ");
        ColorWrite("╚", AccentColor1);
        ColorWrite(new string('═', CONTENT_WIDTH), AccentColor1);
        ColorWriteLine("╝", AccentColor1);
        Console.WriteLine();
    }

    // Helper for drawing a centered line in the banner
    private static void DrawCenteredLine(string text, ConsoleColor color)
    {
        int textLength = text.Length;
        int padding = (CONTENT_WIDTH - textLength) / 2;

        Console.Write("  ");
        ColorWrite("║", AccentColor1);
        ColorWrite(new string(' ', padding), DefaultColor);
        ColorWrite(text, color);
        ColorWrite(new string(' ', CONTENT_WIDTH - textLength - padding), DefaultColor);
        ColorWriteLine("║", AccentColor1);
    }

    // Helper for drawing a config line with label and value
    private static void DrawConfigLine(string label, string value, ConsoleColor valueColor)
    {
        string prefix = $" • {label}: ";

        Console.Write("  ");
        ColorWrite("║", AccentColor1);
        ColorWrite(prefix, DefaultColor);
        ColorWrite(value, valueColor);

        // Calculate remaining space to right border for perfect alignment
        int remaining = CONTENT_WIDTH - prefix.Length - value.Length;
        ColorWrite(new string(' ', remaining), DefaultColor);
        ColorWriteLine("║", AccentColor1);
    }

    // Helper for drawing a tool line with fixed width
    private static void DrawToolLine(Core.Models.Tools.Tool tool, bool isEnabled = true)
    {
        // Get tool name and truncate if needed
        string toolName = tool.Name;
        const int MAX_NAME_WIDTH = 20;

        if (toolName.Length > MAX_NAME_WIDTH)
        {
            toolName = toolName.Substring(0, MAX_NAME_WIDTH - 3) + "...";
        }

        // Get description and truncate if needed
        string description = tool.Description ?? "No description available";

        // Fixed column widths to ensure consistent layout
        const int NAME_COL_WIDTH = 23; // Name + padding
        int maxDescLength = CONTENT_WIDTH - NAME_COL_WIDTH - 1; // -1 for initial space

        if (description.Length > maxDescLength)
        {
            description = description.Substring(0, maxDescLength - 3) + "...";
        }

        // Write with exact, fixed spacing
        Console.Write("  ");
        ColorWrite("║", AccentColor1);

        // Show enabled/disabled status
        string statusIndicator = isEnabled ? " • " : " ○ ";
        ColorWrite(statusIndicator, isEnabled ? DefaultColor : ConsoleColor.DarkGray);

        // Name color depends on enabled status
        var nameColor = isEnabled ? HighlightColor : ConsoleColor.DarkGray;
        ColorWrite(toolName, nameColor);

        // Fixed padding between name and description
        int namePadding = NAME_COL_WIDTH - 3 - toolName.Length; // -3 for "• "
        ColorWrite(new string(' ', namePadding), DefaultColor);

        // Description with padding to right border
        var descriptionColor = isEnabled ? DefaultColor : ConsoleColor.DarkGray;
        ColorWrite(description, descriptionColor);
        int rightPadding = CONTENT_WIDTH - NAME_COL_WIDTH - description.Length;

        if (rightPadding > 0)
        {
            ColorWrite(new string(' ', rightPadding), DefaultColor);
        }

        ColorWriteLine("║", AccentColor1);
    }

    public static void DisplayHelp()
    {
        ColorWriteLine("MCP LLM Function Calling Demo", HighlightColor);
        Console.WriteLine("============================");
        Console.WriteLine("\nUsage: dotnet run --project Mcp.Net.Examples.LLMConsole [options]\n");
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help                Display this help message");
        Console.WriteLine(
            "  --provider <n>            Specify the LLM provider to use (anthropic or openai)"
        );
        Console.WriteLine("                            Alternative: --provider=<n>");
        Console.WriteLine("  -m, --model <n>           Specify the model name to use");
        Console.WriteLine("                            Alternative: --model=<n>");
        Console.WriteLine(
            "  -l, --log-level <level>   Set logging level (verbose|debug|info|warning|error|fatal)"
        );
        Console.WriteLine("                            Alternative: --log-level=<level>");
        Console.WriteLine(
            "                            Default: warning (only warnings and errors are displayed)"
        );
        Console.WriteLine("  -d, --debug               Shortcut for --log-level=debug");
        Console.WriteLine("  -v, --verbose             Shortcut for --log-level=verbose");
        Console.WriteLine(
            "  --all-tools               Use all available tools (skip tool selection)"
        );
        Console.WriteLine("  --skip-tool-selection     Same as --all-tools");
        Console.WriteLine("\nEnvironment Variables:");
        Console.WriteLine(
            "  ANTHROPIC_API_KEY         API key for Anthropic (required when using Anthropic)"
        );
        Console.WriteLine(
            "  OPENAI_API_KEY            API key for OpenAI (required when using OpenAI)"
        );
        Console.WriteLine(
            "  LLM_PROVIDER              Default LLM provider to use (anthropic or openai)"
        );
        Console.WriteLine("  LLM_MODEL                 Default model name to use");
        Console.WriteLine(
            "  LLM_LOG_LEVEL             Default logging level (verbose|debug|info|warning|error|fatal)"
        );
        Console.WriteLine("                            If not set, defaults to warning");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  dotnet run --project Mcp.Net.Examples.LLMConsole --provider anthropic");
        Console.WriteLine("  dotnet run --project Mcp.Net.Examples.LLMConsole --provider openai --model gpt-4o");
        Console.WriteLine("  dotnet run --project Mcp.Net.Examples.LLMConsole --provider=anthropic --debug");
        Console.WriteLine("  dotnet run --project Mcp.Net.Examples.LLMConsole --log-level=debug");
        Console.WriteLine("  dotnet run --project Mcp.Net.Examples.LLMConsole --all-tools");
    }

    private static void ColorWrite(string text, ConsoleColor color)
    {
        var original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = original;
    }

    private static void ColorWriteLine(string text, ConsoleColor color)
    {
        ColorWrite(text, color);
        Console.WriteLine();
    }
}
