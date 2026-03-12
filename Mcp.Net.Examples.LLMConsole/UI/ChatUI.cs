using System.Text.Json;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Examples.LLMConsole.UI;

public class ChatUI
{
    private static readonly ConsoleColor Dim = ConsoleColor.DarkGray;
    private static readonly ConsoleColor Accent = ConsoleColor.Cyan;
    private static readonly ConsoleColor Success = ConsoleColor.Green;
    private static readonly ConsoleColor Warn = ConsoleColor.Yellow;
    private static readonly ConsoleColor Err = ConsoleColor.Red;

    public void DrawChatInterface()
    {
        Console.WriteLine();
        WriteColored("  mcp.net", Accent);
        WriteColored(" ready", Dim);
        Console.WriteLine();
        WriteColoredLine("  Type a message to begin. Ctrl+C to exit.", Dim);
        Console.WriteLine();
    }

    public string GetUserInput()
    {
        WriteColored("> ", Accent);
        var input = Console.ReadLine() ?? string.Empty;
        Console.WriteLine();
        return input;
    }

    public void DisplayAssistantMessage(string message)
    {
        DisplayAssistantDelta(message);
        CompleteAssistantMessage();
    }

    public void DisplayAssistantDelta(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        Console.Write(message);
    }

    public void CompleteAssistantMessage()
    {
        Console.WriteLine();
        Console.WriteLine();
    }

    public void DisplayToolExecution(string toolName)
    {
        WriteColored("  tool ", Dim);
        WriteColored(toolName, Warn);
        WriteColoredLine(" ...", Dim);
    }

    public void DisplayToolResults(ToolInvocationResult result)
    {
        var status = result.IsError ? "err" : "ok";
        var statusColor = result.IsError ? Err : Success;

        WriteColored("  tool ", Dim);
        WriteColored(result.ToolName, Warn);
        WriteColored(" [", Dim);
        WriteColored(status, statusColor);
        WriteColoredLine("]", Dim);

        foreach (var line in result.Text)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                WriteColored("    ", Dim);
                Console.WriteLine(line);
            }
        }

        if (result.Structured.HasValue)
        {
            var json = JsonSerializer.Serialize(
                result.Structured.Value,
                new JsonSerializerOptions { WriteIndented = true }
            );
            foreach (var jsonLine in json.Split('\n'))
            {
                WriteColored("    ", Dim);
                Console.WriteLine(jsonLine);
            }
        }

        if (result.ResourceLinks.Count > 0)
        {
            foreach (var link in result.ResourceLinks)
            {
                WriteColored("    ", Dim);
                Console.WriteLine($"{link.Name ?? link.Uri} ({link.Uri})");
            }
        }

        Console.WriteLine();
    }

    public void DisplayElicitationPrompt(string message, ElicitationSchema schema)
    {
        Console.WriteLine();
        WriteColoredLine("  Elicitation request:", Accent);
        Console.WriteLine($"  {message}");
        Console.WriteLine();

        if (schema.Properties.Count > 0)
        {
            foreach (var (name, property) in schema.Properties)
            {
                var required = schema.Required?.Contains(name) ?? false;
                WriteColored("    ", Dim);
                Console.Write(name);
                if (required)
                {
                    WriteColored(" (required)", Dim);
                }
                if (!string.IsNullOrWhiteSpace(property.Description))
                {
                    WriteColored($" - {property.Description}", Dim);
                }
                Console.WriteLine();
            }
            Console.WriteLine();
        }
    }

    public void DisplayElicitationOutcome(string text)
    {
        WriteColored("  ", Dim);
        Console.WriteLine(text);
        Console.WriteLine();
    }

    public void DisplayToolError(string context, string errorMessage)
    {
        WriteColored("  error", Err);
        WriteColored($" ({context}): ", Dim);
        Console.WriteLine(errorMessage);
        Console.WriteLine();
    }

    public async Task ShowThinkingAnimation(CancellationToken cancellationToken = default)
    {
        var frames = new[] { ".", "..", "..." };
        var frameIndex = 0;
        var cursorTop = Console.CursorTop;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.SetCursorPosition(0, cursorTop);
                Console.Write(new string(' ', Console.WindowWidth - 1));
                Console.SetCursorPosition(0, cursorTop);

                Console.ForegroundColor = Dim;
                Console.Write($"  thinking{frames[frameIndex]}");
                Console.ResetColor();

                frameIndex = (frameIndex + 1) % frames.Length;
                await Task.Delay(400, cancellationToken);
            }
        }
        catch (TaskCanceledException) { }
        finally
        {
            Console.SetCursorPosition(0, cursorTop);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, cursorTop);
            Console.ResetColor();
        }
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
