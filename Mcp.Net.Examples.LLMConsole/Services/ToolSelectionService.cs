using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Examples.LLMConsole.UI;
using Mcp.Net.LLM.Tools;

namespace Mcp.Net.Examples.LLMConsole;

public class ToolSelectionService
{
    private readonly ToolSelectorUI _toolSelectorUI;

    public ToolSelectionService()
    {
        _toolSelectorUI = new ToolSelectorUI();
    }

    public Tool[] PromptForToolSelection(ToolRegistry toolRegistry)
    {
        if (toolRegistry == null)
        {
            throw new ArgumentNullException(nameof(toolRegistry));
        }

        var availableTools = toolRegistry.AllTools.ToArray();
        var categoryDescriptors = toolRegistry.GetCategoryDescriptors();

        Console.WriteLine("Select which tools to enable:");
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);

        var selectedToolNames = _toolSelectorUI.SelectTools(availableTools, categoryDescriptors);

        return availableTools
            .Where(t => selectedToolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }
}
