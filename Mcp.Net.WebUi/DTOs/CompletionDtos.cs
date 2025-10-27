using System;
using System.Collections.Generic;

namespace Mcp.Net.WebUi.DTOs;

public class CompletionRequestDto
{
    public string Scope { get; set; } = string.Empty; // "prompt" or "resource"
    public string Identifier { get; set; } = string.Empty;
    public string ArgumentName { get; set; } = string.Empty;
    public string? CurrentValue { get; set; }
    public Dictionary<string, string>? Context { get; set; }
}

public class CompletionResultDto
{
    public IReadOnlyList<string> Values { get; set; } = Array.Empty<string>();
    public int? Total { get; set; }
    public bool? HasMore { get; set; }
}
