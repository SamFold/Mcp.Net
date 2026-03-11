using System;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.LLMConsole;

public class ProviderSelectionService
{
    private readonly ILogger<ProviderSelectionService> _logger;

    public ProviderSelectionService(ILogger<ProviderSelectionService> logger)
    {
        _logger = logger;
    }

    public LlmProvider PromptForProviderSelection()
    {
        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Select provider:");
            Console.ResetColor();
            Console.WriteLine("    1  Anthropic (Claude)");
            Console.WriteLine("    2  OpenAI");
            Console.Write("  > ");

            var input = (Console.ReadLine() ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            if (IsAnthropicSelection(input))
            {
                _logger.LogInformation("Provider selected interactively: Anthropic");
                return LlmProvider.Anthropic;
            }

            if (IsOpenAiSelection(input))
            {
                _logger.LogInformation("Provider selected interactively: OpenAI");
                return LlmProvider.OpenAI;
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Invalid selection.");
            Console.ResetColor();
        }
    }

    private static bool IsOpenAiSelection(string input) =>
        input.Equals("2", StringComparison.OrdinalIgnoreCase)
        || input.Equals("o", StringComparison.OrdinalIgnoreCase)
        || input.Equals("openai", StringComparison.OrdinalIgnoreCase)
        || input.Equals("open", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnthropicSelection(string input) =>
        input.Equals("1", StringComparison.OrdinalIgnoreCase)
        || input.Equals("a", StringComparison.OrdinalIgnoreCase)
        || input.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
        || input.Equals("claude", StringComparison.OrdinalIgnoreCase);
}
