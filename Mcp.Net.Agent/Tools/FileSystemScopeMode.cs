namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Controls whether filesystem tools are contained to the configured base path
/// or can address paths anywhere on the host filesystem.
/// </summary>
public enum FileSystemScopeMode
{
    BoundedToBasePath,
    Unbounded,
}
