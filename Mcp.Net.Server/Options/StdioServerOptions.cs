using System;
using Mcp.Net.Server.Authentication;

namespace Mcp.Net.Server.Options;

/// <summary>
/// Represents options for configuring a stdio-based MCP server.
/// </summary>
public class StdioServerOptions : McpServerOptions
{

    /// <summary>
    /// Gets or sets whether to use standard input/output or create custom streams.
    /// </summary>
    public bool UseStandardIO { get; set; } = true;

    /// <summary>
    /// Gets or sets the input stream to use when UseStandardIO is false.
    /// </summary>
    public Stream? InputStream { get; set; }

    /// <summary>
    /// Gets or sets the output stream to use when UseStandardIO is false.
    /// </summary>
    public Stream? OutputStream { get; set; }

    /// <summary>
    /// Validates the options are correctly configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the options are invalid.</exception>
    public void Validate()
    {
        if (!UseStandardIO)
        {
            if (InputStream == null)
            {
                throw new InvalidOperationException(
                    "InputStream must be provided when UseStandardIO is false"
                );
            }

            if (OutputStream == null)
            {
                throw new InvalidOperationException(
                    "OutputStream must be provided when UseStandardIO is false"
                );
            }
        }
    }

}
