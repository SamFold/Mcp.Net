using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Completion;

namespace Mcp.Net.LLM.Interfaces;

/// <summary>
/// Provides a high-level facade over the MCP completion API so UI layers can request
/// suggestions without dealing with protocol specifics.
/// </summary>
public interface ICompletionService
{
    /// <summary>
    /// Requests completion suggestions for a prompt argument.
    /// </summary>
    /// <param name="promptName">The prompt to complete.</param>
    /// <param name="argumentName">The argument currently being edited.</param>
    /// <param name="currentValue">The partial value entered by the user.</param>
    /// <param name="contextArguments">
    /// Previously resolved argument values that should be supplied to the server.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the request if the UI abandons it.</param>
    /// <returns>The completion values returned by the MCP server.</returns>
    Task<CompletionValues> CompletePromptAsync(
        string promptName,
        string argumentName,
        string currentValue,
        IReadOnlyDictionary<string, string>? contextArguments = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Requests completion suggestions for a resource argument.
    /// </summary>
    /// <param name="resourceUri">The resource template URI being completed.</param>
    /// <param name="argumentName">The argument currently being edited.</param>
    /// <param name="currentValue">The partial value entered by the user.</param>
    /// <param name="contextArguments">
    /// Optional context containing arguments that have already been specified.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the request if the UI abandons it.</param>
    /// <returns>The completion values returned by the MCP server.</returns>
    Task<CompletionValues> CompleteResourceAsync(
        string resourceUri,
        string argumentName,
        string currentValue,
        IReadOnlyDictionary<string, string>? contextArguments = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Clears cached completions for a specific prompt so fresh data is fetched next time.
    /// </summary>
    /// <param name="promptName">The prompt whose cache entries should be removed.</param>
    void InvalidatePrompt(string promptName);

    /// <summary>
    /// Clears cached completions for a specific resource.
    /// </summary>
    /// <param name="resourceUri">The resource whose cache entries should be removed.</param>
    void InvalidateResource(string resourceUri);

    /// <summary>
    /// Clears all cached completion responses.
    /// </summary>
    void Clear();
}
