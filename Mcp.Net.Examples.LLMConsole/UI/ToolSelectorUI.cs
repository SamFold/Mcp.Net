using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Tools;

namespace Mcp.Net.Examples.LLMConsole.UI;

/// <summary>
/// Provides UI for selecting tools from a list using keyboard navigation.
/// </summary>
public class ToolSelectorUI
{
    private readonly ConsoleColor _defaultColor;
    private readonly ConsoleColor _highlightColor = ConsoleColor.Cyan;
    private readonly ConsoleColor _headerColor = ConsoleColor.Yellow;
    private readonly ConsoleColor _instructionsColor = ConsoleColor.DarkGray;

    public ToolSelectorUI()
    {
        _defaultColor = Console.ForegroundColor;
    }

    private int _menuHeight;
    private int _menuStartPosition;

    /// <summary>
    /// Display a list of tools and let the user select which ones to use.
    /// </summary>
    public string[] SelectTools(
        Tool[] availableTools,
        IReadOnlyList<ToolCategoryDescriptor> categories,
        string[]? preSelectedTools = null
    )
    {
        if (availableTools == null || availableTools.Length == 0)
        {
            return Array.Empty<string>();
        }

        var toolLookup = availableTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var effectiveCategories = categories?.ToList() ?? new List<ToolCategoryDescriptor>();
        if (effectiveCategories.Count == 0)
        {
            effectiveCategories.Add(
                new ToolCategoryDescriptor(
                    "default",
                    "Tools",
                    null,
                    availableTools.Select(t => t.Name).ToList()
                )
            );
        }

        var flattenedGroups = BuildGroupItems(availableTools, effectiveCategories, toolLookup);

        var selectedTools = new HashSet<string>(
            preSelectedTools ?? availableTools.Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase
        );

        int currentIndex = 0;
        bool selectionComplete = false;

        _menuHeight = 3 + 3 + flattenedGroups.Count + 4;

        bool cursorVisible = true;
        if (OperatingSystem.IsWindows())
        {
            cursorVisible = Console.CursorVisible;
            Console.CursorVisible = false;
        }

        try
        {
            Console.Clear();
            _menuStartPosition = Console.CursorTop;
            DrawUI(flattenedGroups, currentIndex, selectedTools);

            while (!selectionComplete && flattenedGroups.Count > 0)
            {
                var keyInfo = Console.ReadKey(true);
                var currentItem = flattenedGroups[currentIndex];

                bool needsRedraw = true;

                switch (keyInfo.Key)
                {
                    case ConsoleKey.UpArrow:
                        currentIndex = Math.Max(0, currentIndex - 1);
                        break;

                    case ConsoleKey.DownArrow:
                        currentIndex = Math.Min(flattenedGroups.Count - 1, currentIndex + 1);
                        break;

                    case ConsoleKey.Spacebar:
                        if (currentItem.IsGroup)
                        {
                            bool allSelected = AreAllToolsInGroupSelected(currentItem, selectedTools);
                            ToggleToolGroup(currentItem, selectedTools, !allSelected);
                        }
                        else if (currentItem.Tool != null)
                        {
                            var toolName = currentItem.Tool.Name;
                            if (selectedTools.Contains(toolName))
                                selectedTools.Remove(toolName);
                            else
                                selectedTools.Add(toolName);
                        }
                        break;

                    case ConsoleKey.A:
                        foreach (var tool in availableTools)
                        {
                            selectedTools.Add(tool.Name);
                        }
                        break;

                    case ConsoleKey.N:
                        selectedTools.Clear();
                        break;

                    case ConsoleKey.Enter:
                        selectionComplete = true;
                        needsRedraw = false;
                        break;

                    case ConsoleKey.Escape:
                        selectedTools.Clear();
                        foreach (var tool in availableTools)
                        {
                            selectedTools.Add(tool.Name);
                        }
                        selectionComplete = true;
                        needsRedraw = false;
                        break;

                    default:
                        needsRedraw = false;
                        break;
                }

                if (!selectionComplete && needsRedraw)
                {
                    DrawUI(flattenedGroups, currentIndex, selectedTools);
                }
            }
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                Console.CursorVisible = cursorVisible;
            }
        }

        return selectedTools.ToArray();
    }

    private void DrawUI(
        List<ToolGroupItem> toolGroups,
        int currentIndex,
        HashSet<string> selectedTools
    )
    {
        Console.SetCursorPosition(0, _menuStartPosition);

        for (int i = 0; i < _menuHeight; i++)
        {
            Console.SetCursorPosition(0, _menuStartPosition + i);
            Console.Write(new string(' ', Console.WindowWidth));
        }

        Console.SetCursorPosition(0, _menuStartPosition);

        DrawHeader();
        DrawToolList(toolGroups, currentIndex, selectedTools);
        DrawInstructions();
    }

    private void DrawHeader()
    {
        Console.ForegroundColor = _headerColor;
        Console.WriteLine("╭──────────────────────────────────────────────╮");
        Console.WriteLine("│            SELECT TOOLS TO ENABLE            │");
        Console.WriteLine("╰──────────────────────────────────────────────╯");
        Console.WriteLine();
        Console.ForegroundColor = _defaultColor;
    }

    private void DrawInstructions()
    {
        Console.WriteLine();
        Console.ForegroundColor = _instructionsColor;
        Console.WriteLine("╭───────────────────────────────────────────────╮");
        Console.WriteLine("│ Navigate: ↑/↓  Select: SPACE  All: A  None: N │");
        Console.WriteLine("│ Confirm: ENTER  Cancel: ESC                   │");
        Console.WriteLine("╰───────────────────────────────────────────────╯");
        Console.ForegroundColor = _defaultColor;
    }

    private void DrawToolList(
        List<ToolGroupItem> toolGroups,
        int currentIndex,
        HashSet<string> selectedTools
    )
    {
        for (int i = 0; i < toolGroups.Count; i++)
        {
            bool isHighlighted = i == currentIndex;
            var item = toolGroups[i];

            if (item.IsGroup)
            {
                var allSelected = AreAllToolsInGroupSelected(item, selectedTools);
                DrawGroupHeader(item, isHighlighted, allSelected);
            }
            else if (item.Tool != null)
            {
                bool isSelected = selectedTools.Contains(item.Tool.Name);
                DrawToolItem(item.Tool, isHighlighted, isSelected);
            }
        }
    }

    private void DrawGroupHeader(
        ToolGroupItem groupItem,
        bool isHighlighted,
        bool allSelected
    )
    {
        Console.ForegroundColor = isHighlighted ? _highlightColor : _headerColor;

        var formattedName = groupItem.GroupName;
        if (formattedName.EndsWith("_", StringComparison.Ordinal))
        {
            formattedName = formattedName.TrimEnd('_');
        }

        string selectionIndicator = allSelected ? "[*]" : "[ ]";
        Console.WriteLine($"{selectionIndicator} {formattedName.ToUpperInvariant()} TOOLS");

        Console.ForegroundColor = _defaultColor;
    }

    private void DrawToolItem(Tool tool, bool isHighlighted, bool isSelected)
    {
        Console.ForegroundColor = isHighlighted ? _highlightColor : _defaultColor;

        string selectionIndicator = isSelected ? "[*]" : "[ ]";
        string formattedName = tool.Name;

        int underscorePos = tool.Name.IndexOf('_');
        if (underscorePos > 0)
        {
            formattedName = tool.Name.Substring(underscorePos + 1);
        }

        Console.Write($"  {selectionIndicator} {formattedName}");

        if (isHighlighted && !string.IsNullOrEmpty(tool.Description))
        {
            Console.ForegroundColor = _instructionsColor;
            Console.Write($" - {tool.Description}");
        }

        Console.WriteLine();
        Console.ForegroundColor = _defaultColor;
    }

    private bool AreAllToolsInGroupSelected(
        ToolGroupItem groupItem,
        HashSet<string> selectedTools
    )
    {
        if (groupItem.ToolNames.Count == 0)
        {
            return false;
        }

        return groupItem.ToolNames.All(selectedTools.Contains);
    }

    private void ToggleToolGroup(
        ToolGroupItem groupItem,
        HashSet<string> selectedTools,
        bool selectAll
    )
    {
        foreach (var toolName in groupItem.ToolNames)
        {
            if (selectAll)
            {
                selectedTools.Add(toolName);
            }
            else
            {
                selectedTools.Remove(toolName);
            }
        }
    }

    private List<ToolGroupItem> BuildGroupItems(
        Tool[] availableTools,
        IReadOnlyList<ToolCategoryDescriptor> categories,
        IReadOnlyDictionary<string, Tool> toolLookup
    )
    {
        var result = new List<ToolGroupItem>();

        var allNames = availableTools.Select(t => t.Name).ToList();
        result.Add(
            new ToolGroupItem
            {
                IsGroup = true,
                GroupName = "All",
                ToolNames = allNames,
            }
        );

        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in categories)
        {
            var names = descriptor.ToolNames
                .Where(toolLookup.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                continue;
            }

            result.Add(
                new ToolGroupItem
                {
                    IsGroup = true,
                    GroupName = descriptor.DisplayName,
                    ToolNames = names,
                }
            );

            foreach (var name in names)
            {
                var tool = toolLookup[name];
                result.Add(new ToolGroupItem { Tool = tool });
                assigned.Add(name);
            }
        }

        var unassigned = availableTools
            .Where(t => !assigned.Contains(t.Name))
            .Select(t => t.Name)
            .ToList();

        if (unassigned.Count > 0)
        {
            result.Add(
                new ToolGroupItem
                {
                    IsGroup = true,
                    GroupName = "Uncategorised",
                    ToolNames = unassigned,
                }
            );

            foreach (var name in unassigned)
            {
                result.Add(new ToolGroupItem { Tool = toolLookup[name] });
            }
        }

        return result;
    }

    private class ToolGroupItem
    {
        public bool IsGroup { get; init; }
        public string GroupName { get; init; } = string.Empty;
        public IReadOnlyList<string> ToolNames { get; init; } = Array.Empty<string>();
        public Tool? Tool { get; init; }
    }
}
