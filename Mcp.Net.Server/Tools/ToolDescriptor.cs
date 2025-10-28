using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Mcp.Net.Core.Attributes;

namespace Mcp.Net.Server.Tools;

/// <summary>
/// Immutable description of a tool discovered from an assembly.
/// </summary>
internal sealed class ToolDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDescriptor"/> class.
    /// </summary>
    /// <param name="name">Unique tool name that will be exposed to MCP clients.</param>
    /// <param name="description">Human-readable summary of the tool.</param>
    /// <param name="declaringType">Type that contains the method decorated with <see cref="McpToolAttribute"/>.</param>
    /// <param name="method">Method that will be invoked when the tool is executed.</param>
    /// <param name="inputSchema">JSON schema describing the expected tool arguments.</param>
    /// <param name="annotations">Optional annotations supplied by the tool attribute.</param>
    public ToolDescriptor(
        string name,
        string description,
        Type declaringType,
        MethodInfo method,
        JsonElement inputSchema,
        IDictionary<string, object?>? annotations
    )
    {
        Name = name;
        Description = description;
        DeclaringType = declaringType;
        Method = method;
        InputSchema = inputSchema;
        Annotations = annotations;
    }

    /// <summary>
    /// Gets the unique tool name advertised to clients.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the descriptive text supplied by <see cref="McpToolAttribute"/>.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the type that contains the tool method.
    /// </summary>
    public Type DeclaringType { get; }

    /// <summary>
    /// Gets the method that implements the tool.
    /// </summary>
    public MethodInfo Method { get; }

    /// <summary>
    /// Gets the JSON schema describing tool inputs.
    /// </summary>
    public JsonElement InputSchema { get; }

    /// <summary>
    /// Gets the optional annotations associated with the tool.
    /// </summary>
    public IDictionary<string, object?>? Annotations { get; }
}
