namespace Mcp.Net.LLM.Platform;

/// <summary>
/// Provides access to environment variables. Allows substituting implementations for testing.
/// </summary>
public interface IEnvironmentVariableProvider
{
    /// <summary>
    /// Retrieves the value of the specified environment variable.
    /// </summary>
    /// <param name="variable">The name of the environment variable.</param>
    /// <returns>The value of the environment variable, or null if it is not found.</returns>
    string? GetEnvironmentVariable(string variable);
}
