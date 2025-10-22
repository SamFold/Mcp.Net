using System.Reflection;
using System.Text.Json;

namespace Mcp.Net.Server.Tools;

/// <summary>
/// Immutable description of a tool discovered from an assembly.
/// </summary>
internal sealed class ToolDescriptor
{
    public ToolDescriptor(
        string name,
        string description,
        Type declaringType,
        MethodInfo method,
        JsonElement inputSchema
    )
    {
        Name = name;
        Description = description;
        DeclaringType = declaringType;
        Method = method;
        InputSchema = inputSchema;
    }

    public string Name { get; }
    public string Description { get; }
    public Type DeclaringType { get; }
    public MethodInfo Method { get; }
    public JsonElement InputSchema { get; }
}
