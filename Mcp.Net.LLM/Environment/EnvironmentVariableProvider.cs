namespace Mcp.Net.LLM.Platform;

/// <summary>
/// Default environment variable provider that proxies to <see cref="System.Environment"/>.
/// </summary>
public sealed class EnvironmentVariableProvider : IEnvironmentVariableProvider
{
    /// <inheritdoc />
    public string? GetEnvironmentVariable(string variable)
    {
        return System.Environment.GetEnvironmentVariable(variable);
    }
}
