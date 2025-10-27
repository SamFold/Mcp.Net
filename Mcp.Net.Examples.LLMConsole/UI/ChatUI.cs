using System.Text.Json;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Examples.LLMConsole.UI;

public class ChatUI
{
    // For colorful console output
    private static readonly ConsoleColor DefaultColor = Console.ForegroundColor;
    private static readonly ConsoleColor UserColor = ConsoleColor.DarkCyan;
    private static readonly ConsoleColor AssistantColor = ConsoleColor.DarkGreen;
    private static readonly ConsoleColor ToolColor = ConsoleColor.Yellow;
    private static readonly ConsoleColor SystemColor = ConsoleColor.DarkMagenta;
    private static readonly ConsoleColor ErrorColor = ConsoleColor.Red;

    // Constants for chat UI layout - fixed width that will never change
    private const int CHAT_WIDTH = 70; // Total width including borders
    private const int CHAT_CONTENT_WIDTH = 66; // Content area width (CHAT_WIDTH - 4 for "│ " and " │")

    private int _messageCount = 0;

    private void ColorWrite(string text, ConsoleColor color)
    {
        var original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = original;
    }

    private void ColorWriteLine(string text, ConsoleColor color)
    {
        ColorWrite(text, color);
        Console.WriteLine();
    }

    private void WritePaddedChatLine(string text, ConsoleColor textColor, ConsoleColor borderColor)
    {
        ColorWrite("│", borderColor);
        ColorWrite(" ", SystemColor);

        // If text is too long, truncate it
        if (text.Length > CHAT_CONTENT_WIDTH - 2) // -2 for the spaces before and after
        {
            // Truncate and add ellipsis
            string truncated = text.Substring(0, CHAT_CONTENT_WIDTH - 5) + "...";
            ColorWrite(truncated, textColor);
        }
        else
        {
            ColorWrite(text, textColor);

            // Calculate padding to align right border consistently
            int padding = CHAT_CONTENT_WIDTH - text.Length - 1; // -1 for the space we've already written
            ColorWrite(new string(' ', padding), DefaultColor);
        }

        ColorWriteLine("│", borderColor);
    }

    public void DrawChatInterface()
    {
        // Don't clear the console to preserve the banner
        // Just add some spacing to separate the banner from the chat
        Console.WriteLine("\n\n");

        string timestamp = DateTime.Now.ToString("yyyy.MM.dd");
        string sessionId = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();

        // Create horizontal lines with fixed width
        string horizontalLine = new string('─', CHAT_CONTENT_WIDTH);

        // Draw top border with fixed width
        ColorWrite("╭", SystemColor);
        ColorWrite(horizontalLine, SystemColor);
        ColorWriteLine("╮", SystemColor);

        // Header with fixed width and padding
        ColorWrite("│", SystemColor);
        ColorWrite(" ", DefaultColor);
        ColorWrite("MEGA AGENT INTERFACE", UserColor);
        ColorWrite(" • ", DefaultColor);
        ColorWrite($"SESSION: {sessionId}", AssistantColor);
        ColorWrite(" • ", DefaultColor);
        ColorWrite($"DATE: {timestamp}", ToolColor);

        // Calculate padding for fixed right border
        string header = $"MEGA AGENT INTERFACE • SESSION: {sessionId} • DATE: {timestamp}";
        int headerPadding = Math.Max(0, CHAT_CONTENT_WIDTH - header.Length - 1);
        ColorWrite(new string(' ', headerPadding), DefaultColor);

        ColorWriteLine("│", SystemColor);

        // Divider with fixed width
        ColorWrite("├", SystemColor);
        ColorWrite(horizontalLine, SystemColor);
        ColorWriteLine("┤", SystemColor);

        // Information lines with fixed width
        WritePaddedChatLine(
            "Welcome to the Mega Agent interface.",
            textColor: DefaultColor,
            SystemColor
        );
        WritePaddedChatLine(
            "Type your message below to communicate with the LLM.",
            DefaultColor,
            SystemColor
        );
        WritePaddedChatLine("Use Ctrl+C to exit the session.", DefaultColor, SystemColor);

        // Bottom border with fixed width
        ColorWrite("╰", SystemColor);
        ColorWrite(horizontalLine, SystemColor);
        ColorWriteLine("╯", SystemColor);

        Console.WriteLine();
    }

    public string GetUserInput()
    {
        // Always show USER header for all messages
        string userHeader = "USER";
        string horizontalLine = new string('─', 16); // Fixed width for user/assistant prompt

        // Draw header with USER text
        int paddingLeft = Math.Max(0, (horizontalLine.Length - userHeader.Length) / 2);
        int paddingRight = Math.Max(0, horizontalLine.Length - userHeader.Length - paddingLeft);

        ColorWrite("╭", UserColor);
        ColorWrite(new string('─', paddingLeft), UserColor);
        ColorWrite(userHeader, UserColor);
        ColorWrite(new string('─', paddingRight), UserColor);
        ColorWriteLine("", UserColor);

        ColorWrite("│", UserColor);
        ColorWrite(" ", UserColor);

        string input = Console.ReadLine() ?? string.Empty;

        Console.WriteLine();

        _messageCount++;
        return input;
    }

    public void DisplayAssistantMessage(string message)
    {
        string assistantHeader = "ASSISTANT";
        string horizontalLine = new string('─', 20);

        // Draw assistant header with fixed width
        ColorWrite("╭", AssistantColor);
        int paddingLeft = Math.Max(0, (horizontalLine.Length - assistantHeader.Length) / 2);
        int paddingRight = Math.Max(
            0,
            horizontalLine.Length - assistantHeader.Length - paddingLeft
        );
        ColorWrite(new string('─', paddingLeft), AssistantColor);
        ColorWrite(assistantHeader, AssistantColor);
        ColorWrite(new string('─', paddingRight), AssistantColor);
        ColorWriteLine("", AssistantColor);

        // Split by paragraphs to make it more readable
        var paragraphs = message.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
                continue;

            // For each line, ensure proper formatting
            var lines = paragraph.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Get console width for proper line wrapping
                int consoleWidth = Math.Max(80, Console.WindowWidth); // Default to 80 if console width is unavailable
                int textWidth = consoleWidth - 2; // Account for left border and space

                // Use AssistantColor for the left border for the first line
                ColorWrite("│", AssistantColor);
                ColorWrite(" ", AssistantColor);

                if (line.Length <= textWidth)
                {
                    // Line fits within console width, display normally
                    ColorWrite(line, DefaultColor);
                    Console.WriteLine();
                }
                else
                {
                    // Line is too long, wrap it manually with proper left border
                    int position = 0;
                    bool isFirstLine = true;

                    while (position < line.Length)
                    {
                        // First line already has the border drawn
                        if (!isFirstLine)
                        {
                            ColorWrite("│", AssistantColor);
                            ColorWrite(" ", AssistantColor);
                        }

                        // Calculate how much text we can fit on this line
                        int remainingLength = line.Length - position;
                        int chunkSize = Math.Min(remainingLength, textWidth);

                        // Write the chunk of text
                        ColorWrite(line.Substring(position, chunkSize), DefaultColor);
                        Console.WriteLine();

                        // Move to next position
                        position += chunkSize;
                        isFirstLine = false;
                    }
                }
            }

            // Add blank line between paragraphs for readability
            if (paragraphs.Length > 1)
            {
                ColorWrite("│", AssistantColor);
                ColorWrite(" ", AssistantColor);
                Console.WriteLine();
            }
        }

        Console.WriteLine();
    }

    public void DisplayToolExecution(string toolName)
    {
        // Display a futuristic tool execution banner with fixed width
        string systemHeader = "SYSTEM";
        string horizontalLine = new string('─', 15);

        // Draw system header with fixed width
        ColorWrite("╭", SystemColor);
        int paddingLeft = Math.Max(0, (horizontalLine.Length - systemHeader.Length) / 2);
        int paddingRight = Math.Max(0, horizontalLine.Length - systemHeader.Length - paddingLeft);
        ColorWrite(new string('─', paddingLeft), SystemColor);
        ColorWrite(systemHeader, SystemColor);
        ColorWrite(new string('─', paddingRight), SystemColor);
        ColorWriteLine("", SystemColor);

        ColorWrite("│", SystemColor);
        ColorWrite(" ▶ ", SystemColor);
        ColorWrite("Executing tool: ", DefaultColor);
        ColorWrite(toolName, ToolColor);
        Console.WriteLine();

        Console.WriteLine();
    }

    public void DisplayToolResults(ToolInvocationResult result)
    {
        string resultsHeader = "TOOL RESULTS";
        string horizontalLine = new string('─', 18);

        ColorWrite("╭", ToolColor);
        int paddingLeft = Math.Max(0, (horizontalLine.Length - resultsHeader.Length) / 2);
        int paddingRight = Math.Max(0, horizontalLine.Length - resultsHeader.Length - paddingLeft);
        ColorWrite(new string('─', paddingLeft), ToolColor);
        ColorWrite(resultsHeader, ToolColor);
        ColorWrite(new string('─', paddingRight), ToolColor);
        ColorWriteLine("", ToolColor);

        ColorWrite("│", ToolColor);
        ColorWrite(" ", ToolColor);
        ColorWrite("Tool: ", DefaultColor);
        ColorWrite(result.ToolName, ToolColor);
        Console.WriteLine();

        ColorWrite("│", ToolColor);
        ColorWrite(" ", ToolColor);
        ColorWrite("Status: ", DefaultColor);
        ColorWrite(result.IsError ? "error" : "ok", result.IsError ? ErrorColor : AssistantColor);
        Console.WriteLine();

        foreach (var line in result.Text)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ColorWrite("│", ToolColor);
            ColorWrite(" • ", ToolColor);
            ColorWrite(line, DefaultColor);
            Console.WriteLine();
        }

        if (result.Structured.HasValue)
        {
            ColorWrite("│", ToolColor);
            ColorWrite(" • ", ToolColor);
            ColorWrite("structured:", DefaultColor);
            Console.WriteLine();

            var structuredJson = JsonSerializer.Serialize(
                result.Structured.Value,
                new JsonSerializerOptions { WriteIndented = true }
            );

            foreach (var structuredLine in structuredJson.Split('\n'))
            {
                ColorWrite("│", ToolColor);
                ColorWrite("   ", ToolColor);
                ColorWrite(structuredLine, DefaultColor);
                Console.WriteLine();
            }
        }

        if (result.ResourceLinks.Count > 0)
        {
            ColorWrite("│", ToolColor);
            ColorWrite(" • ", ToolColor);
            ColorWrite("resources:", DefaultColor);
            Console.WriteLine();

            foreach (var link in result.ResourceLinks)
            {
                var label = link.Name ?? link.Uri;
                ColorWrite("│", ToolColor);
                ColorWrite("   ", ToolColor);
                ColorWrite($"{label} ({link.Uri})", DefaultColor);
                Console.WriteLine();
            }
        }

        if (result.Metadata.HasValue)
        {
            ColorWrite("│", ToolColor);
            ColorWrite(" • ", ToolColor);
            ColorWrite("metadata:", DefaultColor);
            Console.WriteLine();

            var metadataJson = JsonSerializer.Serialize(
                result.Metadata.Value,
                new JsonSerializerOptions { WriteIndented = true }
            );

            foreach (var metaLine in metadataJson.Split('\n'))
            {
                ColorWrite("│", ToolColor);
                ColorWrite("   ", ToolColor);
                ColorWrite(metaLine, DefaultColor);
                Console.WriteLine();
            }
        }

        ColorWrite("╰", ToolColor);
        ColorWrite(horizontalLine, ToolColor);
        ColorWriteLine("╯", ToolColor);
        Console.WriteLine();
    }

    public void DisplayElicitationPrompt(string message, ElicitationSchema schema)
    {
        Console.WriteLine();
        ColorWriteLine("=== Elicitation Request ===", SystemColor);
        Console.WriteLine(message);
        Console.WriteLine();

        if (schema.Properties.Count > 0)
        {
            Console.WriteLine("Fields:");
            foreach (var (name, property) in schema.Properties)
            {
                var required = schema.Required?.Contains(name) ?? false;
                Console.Write($" - {name}");
                if (required)
                {
                    Console.Write(" (required)");
                }

                if (!string.IsNullOrWhiteSpace(property.Description))
                {
                    Console.Write($": {property.Description}");
                }

                Console.WriteLine();
            }

            Console.WriteLine();
        }
    }

    public void DisplayElicitationOutcome(string text)
    {
        ColorWriteLine(text, SystemColor);
        Console.WriteLine();
    }

    public void DisplayToolError(string toolName, string errorMessage)
    {
        // Error banner with fixed width
        string errorHeader = "ERROR";
        string errorLine = new string('─', 15);

        ColorWrite("╭", ErrorColor);
        int errorPaddingLeft = Math.Max(0, (errorLine.Length - errorHeader.Length) / 2);
        int errorPaddingRight = Math.Max(
            0,
            errorLine.Length - errorHeader.Length - errorPaddingLeft
        );
        ColorWrite(new string('─', errorPaddingLeft), ErrorColor);
        ColorWrite(errorHeader, ErrorColor);
        ColorWrite(new string('─', errorPaddingRight), ErrorColor);
        ColorWriteLine("", ErrorColor);

        ColorWrite("│", ErrorColor);
        ColorWrite(" ", ErrorColor);
        ColorWrite($"Tool '{toolName}' not found", DefaultColor);
        Console.WriteLine();

        Console.WriteLine();
    }

    /// <summary>
    /// Displays an animated "thinking" indicator with color effects
    /// </summary>
    public async Task ShowThinkingAnimation(CancellationToken cancellationToken = default)
    {
        // Thinking animation characters and colors
        char[] spinnerChars = new[] { '⣾', '⣽', '⣻', '⢿', '⡿', '⣟', '⣯', '⣷' };
        ConsoleColor[] colors = new[]
        {
            ConsoleColor.Cyan,
            ConsoleColor.Blue,
            ConsoleColor.Magenta,
            ConsoleColor.DarkMagenta,
            ConsoleColor.DarkBlue,
            ConsoleColor.DarkCyan,
        };

        string label = " Thinking ";
        int position = 0;
        int colorIndex = 0;

        // Save current cursor position
        int cursorLeft = Console.CursorLeft;
        int cursorTop = Console.CursorTop;

        // CursorVisible is only supported on Windows
        bool cursorVisible = true;

        // Only change cursor visibility on Windows
        if (OperatingSystem.IsWindows())
        {
            cursorVisible = Console.CursorVisible;
            Console.CursorVisible = false;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Clear the line
                Console.SetCursorPosition(0, cursorTop);
                Console.Write(new string(' ', Console.WindowWidth - 1));
                Console.SetCursorPosition(0, cursorTop);

                // Display spinner character with current color
                Console.ForegroundColor = colors[colorIndex];
                Console.Write(spinnerChars[position]);

                // Display label with pulsing effect
                for (int i = 0; i < label.Length; i++)
                {
                    // Calculate color based on position and pulse effect
                    int pulseOffset = Math.Abs((i - position) % label.Length);
                    int colorOffset = Math.Min(pulseOffset, colors.Length - 1);
                    Console.ForegroundColor = colors[(colorIndex + colorOffset) % colors.Length];
                    Console.Write(label[i]);
                }

                position = (position + 1) % spinnerChars.Length;
                colorIndex = (colorIndex + 1) % colors.Length;

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            // Normal cancellation, nothing to do
        }
        finally
        {
            // Restore console state
            Console.ForegroundColor = DefaultColor;

            // Only restore cursor visibility on Windows
            if (OperatingSystem.IsWindows())
            {
                Console.CursorVisible = cursorVisible;
            }

            // Clear the animation line
            Console.SetCursorPosition(0, cursorTop);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, cursorTop);
        }
    }
}
