using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.LLM.Interfaces;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Completions;

/// <summary>
/// Default implementation that mediates calls to <see cref="IMcpClient.CompleteAsync"/> with a
/// small in-memory cache so repeated UI lookups do not flood the transport.
/// </summary>
public sealed class CompletionService : ICompletionService
{
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<CompletionService> _logger;
    private readonly ConcurrentDictionary<CompletionCacheKey, CompletionValues> _cache = new();

    public CompletionService(IMcpClient mcpClient, ILogger<CompletionService> logger)
    {
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CompletionValues> CompletePromptAsync(
        string promptName,
        string argumentName,
        string currentValue,
        IReadOnlyDictionary<string, string>? contextArguments = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptName);
        ArgumentException.ThrowIfNullOrWhiteSpace(argumentName);

        var reference = new CompletionReference { Type = "ref/prompt", Name = promptName };
        return CompleteAsync(reference, argumentName, currentValue, contextArguments, cancellationToken);
    }

    public Task<CompletionValues> CompleteResourceAsync(
        string resourceUri,
        string argumentName,
        string currentValue,
        IReadOnlyDictionary<string, string>? contextArguments = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(argumentName);

        var reference = new CompletionReference { Type = "ref/resource", Uri = resourceUri };
        return CompleteAsync(reference, argumentName, currentValue, contextArguments, cancellationToken);
    }

    public void InvalidatePrompt(string promptName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptName);
        Invalidate(entry => entry.ReferenceId.Equals(promptName, StringComparison.OrdinalIgnoreCase));
    }

    public void InvalidateResource(string resourceUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceUri);
        Invalidate(entry => entry.ReferenceId.Equals(resourceUri, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear() => _cache.Clear();

    private void Invalidate(Func<CompletionCacheKey, bool> predicate)
    {
        foreach (var key in _cache.Keys.Where(predicate))
        {
            _cache.TryRemove(key, out _);
        }
    }

    private async Task<CompletionValues> CompleteAsync(
        CompletionReference reference,
        string argumentName,
        string currentValue,
        IReadOnlyDictionary<string, string>? contextArguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var contextFingerprint = CompletionCacheKey.BuildContextFingerprint(contextArguments);
        var cacheKey = new CompletionCacheKey(reference.Type, reference.Name ?? reference.Uri ?? string.Empty, argumentName, currentValue, contextFingerprint);

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            _logger.LogDebug(
                "Completion cache hit for {Type}:{Id} argument {Argument}",
                reference.Type,
                cacheKey.ReferenceId,
                argumentName
            );
            return Clone(cached);
        }

        _logger.LogDebug(
            "Requesting completions for {Type}:{Id} argument {Argument} with value '{Value}'",
            reference.Type,
            cacheKey.ReferenceId,
            argumentName,
            currentValue
        );

        var context = BuildContext(contextArguments);
        var argument = new CompletionArgument { Name = argumentName, Value = currentValue };

        CompletionValues completion;
        try
        {
            completion = await _mcpClient.CompleteAsync(reference, argument, context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Completion request failed for {Type}:{Id} argument {Argument}",
                reference.Type,
                cacheKey.ReferenceId,
                argumentName
            );
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();

        _cache[cacheKey] = Clone(completion);
        return Clone(completion);
    }

    private static CompletionContext? BuildContext(IReadOnlyDictionary<string, string>? contextArguments)
    {
        if (contextArguments == null || contextArguments.Count == 0)
        {
            return null;
        }

        return new CompletionContext
        {
            Arguments = new Dictionary<string, string>(contextArguments, StringComparer.Ordinal)
        };
    }

    private static CompletionValues Clone(CompletionValues source)
    {
        return new CompletionValues
        {
            Values = source.Values.ToArray(),
            Total = source.Total,
            HasMore = source.HasMore
        };
    }

    private readonly record struct CompletionCacheKey(
        string ReferenceType,
        string ReferenceId,
        string ArgumentName,
        string CurrentValue,
        string ContextFingerprint
    )
    {
        public static string BuildContextFingerprint(IReadOnlyDictionary<string, string>? context)
        {
            if (context == null || context.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(
                '|',
                context
                    .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                    .Select(kvp => $"{kvp.Key}={kvp.Value}")
            );
        }
    }
}
