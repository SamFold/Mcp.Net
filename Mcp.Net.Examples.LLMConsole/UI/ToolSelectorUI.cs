using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Agent.Tools;

namespace Mcp.Net.Examples.LLMConsole.UI;

public class ToolSelectorUI
{
    private static readonly ConsoleColor Dim = ConsoleColor.DarkGray;
    private static readonly ConsoleColor Accent = ConsoleColor.Cyan;
    private static readonly ConsoleColor Ok = ConsoleColor.Green;

    private int _menuHeight;
    private int _menuStartPosition;

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

        _menuHeight = 3 + flattenedGroups.Count + 4;

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

        Console.ForegroundColor = Accent;
        Console.Write("  Select tools");
        Console.ForegroundColor = Dim;
        Console.WriteLine($"  {selectedTools.Count} selected");
        Console.ResetColor();
        Console.WriteLine();

        DrawToolList(toolGroups, currentIndex, selectedTools);

        Console.WriteLine();
        Console.ForegroundColor = Dim;
        Console.WriteLine("  arrows navigate  space toggle  a all  n none  enter confirm  esc cancel");
        Console.ResetColor();
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

    private static void DrawGroupHeader(
        ToolGroupItem groupItem,
        bool isHighlighted,
        bool allSelected
    )
    {
        var formattedName = groupItem.GroupName.TrimEnd('_');
        var indicator = allSelected ? "+" : "-";
        var color = isHighlighted ? Accent : ConsoleColor.White;
        var indicatorColor = allSelected ? Ok : Dim;

        Console.ForegroundColor = indicatorColor;
        Console.Write($"  {indicator} ");
        Console.ForegroundColor = color;
        Console.WriteLine(formattedName.ToUpperInvariant());
        Console.ResetColor();
    }

    private static void DrawToolItem(Tool tool, bool isHighlighted, bool isSelected)
    {
        var indicator = isSelected ? "+" : "-";
        var color = isHighlighted ? Accent : (isSelected ? ConsoleColor.White : Dim);
        var indicatorColor = isSelected ? Ok : Dim;

        Console.ForegroundColor = indicatorColor;
        Console.Write($"    {indicator} ");
        Console.ForegroundColor = color;
        Console.Write(tool.Name);

        if (isHighlighted && !string.IsNullOrEmpty(tool.Description))
        {
            Console.ForegroundColor = Dim;
            Console.Write($"  {tool.Description}");
        }

        Console.WriteLine();
        Console.ResetColor();
    }

    private static bool AreAllToolsInGroupSelected(
        ToolGroupItem groupItem,
        HashSet<string> selectedTools
    ) =>
        groupItem.ToolNames.Count > 0 && groupItem.ToolNames.All(selectedTools.Contains);

    private static void ToggleToolGroup(
        ToolGroupItem groupItem,
        HashSet<string> selectedTools,
        bool selectAll
    )
    {
        foreach (var toolName in groupItem.ToolNames)
        {
            if (selectAll)
                selectedTools.Add(toolName);
            else
                selectedTools.Remove(toolName);
        }
    }

    private static List<ToolGroupItem> BuildGroupItems(
        Tool[] availableTools,
        IReadOnlyList<ToolCategoryDescriptor> categories,
        IReadOnlyDictionary<string, Tool> toolLookup
    )
    {
        var result = new List<ToolGroupItem>();

        var allNames = availableTools.Select(t => t.Name).ToList();
        result.Add(new ToolGroupItem { IsGroup = true, GroupName = "All", ToolNames = allNames });

        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in categories)
        {
            var names = descriptor.ToolNames
                .Where(toolLookup.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0) continue;

            result.Add(new ToolGroupItem { IsGroup = true, GroupName = descriptor.DisplayName, ToolNames = names });

            foreach (var name in names)
            {
                result.Add(new ToolGroupItem { Tool = toolLookup[name] });
                assigned.Add(name);
            }
        }

        var unassigned = availableTools.Where(t => !assigned.Contains(t.Name)).Select(t => t.Name).ToList();

        if (unassigned.Count > 0)
        {
            result.Add(new ToolGroupItem { IsGroup = true, GroupName = "Other", ToolNames = unassigned });
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
