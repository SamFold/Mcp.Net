using System;
using System.Collections.Generic;

namespace Mcp.Net.WebUi.DTOs;

public class PromptArgumentDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Required { get; set; }
}

public class PromptSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<PromptArgumentDto> Arguments { get; set; } = Array.Empty<PromptArgumentDto>();
}
