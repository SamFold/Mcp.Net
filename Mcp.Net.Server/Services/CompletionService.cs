using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Server.Completions;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Services;

internal sealed class CompletionService : ICompletionService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, CompletionHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ServerCapabilities _capabilities;
    private readonly ILogger<CompletionService> _logger;

    public CompletionService(ServerCapabilities capabilities, ILogger<CompletionService> logger)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterPromptCompletion(
        string promptName,
        CompletionHandler handler,
        bool overwrite = false
    )
    {
        if (string.IsNullOrWhiteSpace(promptName))
        {
            throw new ArgumentException("Prompt name must be provided.", nameof(promptName));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var key = BuildCompletionKey("ref/prompt", promptName);
        RegisterHandler(key, handler, overwrite);

        _logger.LogInformation(
            "Registered completion handler for prompt {PromptName}",
            promptName
        );
    }

    public void RegisterResourceCompletion(
        string resourceUri,
        CompletionHandler handler,
        bool overwrite = false
    )
    {
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            throw new ArgumentException("Resource URI must be provided.", nameof(resourceUri));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var key = BuildCompletionKey("ref/resource", resourceUri);
        RegisterHandler(key, handler, overwrite);

        _logger.LogInformation(
            "Registered completion handler for resource {ResourceUri}",
            resourceUri
        );
    }

    public async Task<CompletionValues> CompleteAsync(
        CompletionCompleteParams request,
        CancellationToken cancellationToken
    )
    {
        if (request.Reference == null)
        {
            throw new McpException(ErrorCode.InvalidParams, "Completion reference is required.");
        }

        if (request.Argument == null)
        {
            throw new McpException(ErrorCode.InvalidParams, "Completion argument is required.");
        }

        var referenceType = request.Reference.Type?.Trim();
        if (string.IsNullOrWhiteSpace(referenceType))
        {
            throw new McpException(ErrorCode.InvalidParams, "Completion reference type is required.");
        }

        var (key, identifier) = ResolveCompletionKey(referenceType, request.Reference);

        CompletionHandler handler;
        lock (_sync)
        {
            if (!_handlers.TryGetValue(key, out handler!))
            {
                throw new McpException(
                    ErrorCode.InvalidParams,
                    $"No completion handler registered for {referenceType} '{identifier}'."
                );
            }
        }

        try
        {
            var context = new CompletionRequestContext(request);
            var result = await handler(context, cancellationToken).ConfigureAwait(false);
            return result ?? new CompletionValues();
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error generating completion values for {ReferenceType} '{Identifier}'.",
                referenceType,
                identifier
            );
            throw new McpException(
                ErrorCode.InternalError,
                "Failed to generate completion suggestions."
            );
        }
    }

    private void RegisterHandler(string key, CompletionHandler handler, bool overwrite)
    {
        lock (_sync)
        {
            if (!overwrite && _handlers.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    "A completion handler is already registered for the supplied reference."
                );
            }

            _handlers[key] = handler;
            EnsureCompletionCapabilityAdvertised();
        }
    }

    private static (string Key, string Identifier) ResolveCompletionKey(
        string referenceType,
        CompletionReference reference
    )
    {
        return referenceType switch
        {
            "ref/prompt" when !string.IsNullOrWhiteSpace(reference.Name)
                => (BuildCompletionKey(referenceType, reference.Name), reference.Name),
            "ref/resource" when !string.IsNullOrWhiteSpace(reference.Uri)
                => (BuildCompletionKey(referenceType, reference.Uri!), reference.Uri!),
            _ => throw new McpException(
                ErrorCode.InvalidParams,
                $"Unsupported completion reference '{referenceType}'."
            ),
        };
    }

    private void EnsureCompletionCapabilityAdvertised()
    {
        if (_capabilities.Completions == null)
        {
            _capabilities.Completions = new { };
        }
    }

    private static string BuildCompletionKey(string referenceType, string identifier)
    {
        var normalizedType = referenceType?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedIdentifier = identifier?.Trim() ?? string.Empty;
        return $"{normalizedType}::{normalizedIdentifier}";
    }
}
