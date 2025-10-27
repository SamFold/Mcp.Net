using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mcp.Net.LLM.Models;

/// <summary>
/// Describes a tool invocation requested by the LLM.
/// </summary>
public sealed class ToolInvocation
{
    public ToolInvocation(
        string id,
        string name,
        IReadOnlyDictionary<string, object?> arguments
    )
    {
        Id = id;
        Name = name;
        Arguments = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(arguments));
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyDictionary<string, object?> Arguments { get; }

    public override string ToString() => $"{Name} ({Id})";
}
