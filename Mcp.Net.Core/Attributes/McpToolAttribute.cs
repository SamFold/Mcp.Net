namespace Mcp.Net.Core.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public Type? InputSchemaType { get; set; }
    /// <summary>
    /// Single category identifier for this tool. Optional.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Additional category identifiers for this tool. Optional.
    /// </summary>
    public string[]? Categories { get; set; }

    /// <summary>
    /// Friendly display name for the primary category. Optional.
    /// </summary>
    public string? CategoryDisplayName { get; set; }

    /// <summary>
    /// Optional ordering hint for the primary category. Set to a numeric value to control ordering; leave unset to use defaults.
    /// </summary>
    public double CategoryOrder { get; set; } = double.NaN;

    public McpToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
