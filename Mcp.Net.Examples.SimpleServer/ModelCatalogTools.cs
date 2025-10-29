using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Mcp.Net.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.SimpleServer;

/// <summary>
/// Tools for querying model catalogs from OpenAI and Anthropic.
/// </summary>
[McpTool(
    "model_catalog",
    "Tools for inspecting provider model catalogs",
    Category = "ai-platforms",
    CategoryDisplayName = "AI Providers"
)]
public sealed class ModelCatalogTools
{
    internal const string OpenAiClientName = "OpenAIModels";
    internal const string AnthropicClientName = "AnthropicModels";

    private const string ProviderOpenAi = "openai";
    private const string ProviderAnthropic = "anthropic";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelCatalogTools> _logger;

    public ModelCatalogTools(IHttpClientFactory httpClientFactory, ILogger<ModelCatalogTools> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Lists model metadata from OpenAI and/or Anthropic.
    /// </summary>
    [McpTool(
        "model_catalog_list_models",
        "Fetch the latest model identifiers and metadata from OpenAI and Anthropic",
        Category = "ai-platforms",
        CategoryDisplayName = "AI Providers"
    )]
    public async Task<ModelCatalogResult> ListModelsAsync(
        [McpParameter(
            description:
                "Limit results to specific providers (e.g., 'openai', 'anthropic', 'both'). Accepts comma-separated or JSON array values. Defaults to both providers."
        )]
            string? providers = null,
        [McpParameter(description: "Optional case-insensitive substring filter for model identifiers.")]
            string? match = null,
        [McpParameter(description: "Maximum number of models to return per provider. Defaults to 50.")]
            int maxPerProvider = 50,
        [McpParameter(description: "Include simple provider metadata (owner, pricing). Defaults to true.")]
            bool includeMetadata = true
    )
    {
        var normalizedProviders = NormalizeProviders(providers);
        var warnings = new List<string>();
        var providerResults = new List<ModelProviderResult>();

        int effectiveMax = Math.Clamp(maxPerProvider, 1, 500);
        string? normalizedMatch = string.IsNullOrWhiteSpace(match)
            ? null
            : match.Trim();

        if (normalizedProviders.Contains(ProviderOpenAi))
        {
            var openAiFetch = await FetchOpenAiModelsAsync(
                normalizedMatch,
                effectiveMax,
                includeMetadata
            );
            providerResults.Add(openAiFetch.Provider);
            warnings.AddRange(openAiFetch.Warnings);
        }

        if (normalizedProviders.Contains(ProviderAnthropic))
        {
            var anthropicFetch = await FetchAnthropicModelsAsync(
                normalizedMatch,
                effectiveMax,
                includeMetadata
            );
            providerResults.Add(anthropicFetch.Provider);
            warnings.AddRange(anthropicFetch.Warnings);
        }

        if (providerResults.Count == 0)
        {
            warnings.Add(
                "No providers were selected. Specify 'openai' or 'anthropic' as the provider parameter."
            );
        }

        var summary = BuildSummary(providerResults, warnings);
        return new ModelCatalogResult
        {
            Summary = summary,
            Providers = providerResults,
            Warnings = warnings,
        };
    }

    private async Task<ProviderFetchResult> FetchOpenAiModelsAsync(
        string? match,
        int maxPerProvider,
        bool includeMetadata
    )
    {
        var warnings = new List<string>();
        var providerResult = new ModelProviderResult
        {
            ProviderId = ProviderOpenAi,
            ProviderName = "OpenAI",
        };

        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            warnings.Add(
                "OPENAI_API_KEY is not set; unable to retrieve OpenAI model catalog."
            );
            return new ProviderFetchResult(providerResult, warnings);
        }

        try
        {
            var client = _httpClientFactory.CreateClient(OpenAiClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add(
                    $"OpenAI returned {(int)response.StatusCode} {response.ReasonPhrase ?? string.Empty} when querying models."
                );
                return new ProviderFetchResult(providerResult, warnings);
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(contentStream);

            if (!document.RootElement.TryGetProperty("data", out var dataElement)
                || dataElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("OpenAI response did not contain a 'data' array.");
                return new ProviderFetchResult(providerResult, warnings);
            }

            var descriptors = new List<ModelDescriptor>();
            foreach (var modelElement in dataElement.EnumerateArray())
            {
                if (modelElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!modelElement.TryGetProperty("id", out var idProperty))
                {
                    continue;
                }

                var id = idProperty.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!MatchesFilter(id, match))
                {
                    continue;
                }

                DateTimeOffset? createdAt = null;
                if (
                    modelElement.TryGetProperty("created", out var createdProperty)
                    && createdProperty.ValueKind == JsonValueKind.Number
                    && createdProperty.TryGetInt64(out var epochSeconds)
                )
                {
                    createdAt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
                }

                string? owner = null;
                if (
                    modelElement.TryGetProperty("owned_by", out var ownerProperty)
                    && ownerProperty.ValueKind == JsonValueKind.String
                )
                {
                    owner = ownerProperty.GetString();
                }

                var metadata = includeMetadata
                    ? ExtractSimpleMetadata(
                        modelElement,
                        skipProperties: new[]
                        {
                            "id",
                            "created",
                            "owned_by",
                            "permission",
                        }
                    )
                    : null;

                descriptors.Add(
                    new ModelDescriptor
                    {
                        Id = id,
                        Provider = ProviderOpenAi,
                        DisplayName = null,
                        Description = null,
                        Owner = owner,
                        CreatedAt = createdAt,
                        Metadata = metadata,
                    }
                );
            }

            providerResult = providerResult with
            {
                Models = descriptors
                    .OrderByDescending(model => model.CreatedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                    .Take(maxPerProvider)
                    .ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query OpenAI /models endpoint.");
            warnings.Add($"Exception retrieving OpenAI models: {ex.Message}");
        }

        return new ProviderFetchResult(providerResult, warnings);
    }

    private async Task<ProviderFetchResult> FetchAnthropicModelsAsync(
        string? match,
        int maxPerProvider,
        bool includeMetadata
    )
    {
        var warnings = new List<string>();
        var providerResult = new ModelProviderResult
        {
            ProviderId = ProviderAnthropic,
            ProviderName = "Anthropic",
        };

        string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            warnings.Add(
                "ANTHROPIC_API_KEY is not set; unable to retrieve Anthropic model catalog."
            );
            return new ProviderFetchResult(providerResult, warnings);
        }

        try
        {
            var client = _httpClientFactory.CreateClient(AnthropicClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add(
                    $"Anthropic returned {(int)response.StatusCode} {response.ReasonPhrase ?? string.Empty} when querying models."
                );
                return new ProviderFetchResult(providerResult, warnings);
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(contentStream);
            if (!document.RootElement.TryGetProperty("data", out var dataElement)
                || dataElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("Anthropic response did not contain a 'data' array.");
                return new ProviderFetchResult(providerResult, warnings);
            }

            var descriptors = new List<ModelDescriptor>();
            foreach (var modelElement in dataElement.EnumerateArray())
            {
                if (modelElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!modelElement.TryGetProperty("id", out var idProperty))
                {
                    continue;
                }

                var id = idProperty.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!MatchesFilter(id, match))
                {
                    continue;
                }

                string? displayName = null;
                if (
                    modelElement.TryGetProperty("display_name", out var displayProperty)
                    && displayProperty.ValueKind == JsonValueKind.String
                )
                {
                    displayName = displayProperty.GetString();
                }

                string? description = null;
                if (
                    modelElement.TryGetProperty("description", out var descriptionProperty)
                    && descriptionProperty.ValueKind == JsonValueKind.String
                )
                {
                    description = descriptionProperty.GetString();
                }

                var metadata = includeMetadata
                    ? ExtractSimpleMetadata(
                        modelElement,
                        skipProperties: new[] { "id", "display_name", "description" }
                    )
                    : null;

                descriptors.Add(
                    new ModelDescriptor
                    {
                        Id = id,
                        Provider = ProviderAnthropic,
                        DisplayName = displayName,
                        Description = description,
                        Owner = null,
                        CreatedAt = null,
                        Metadata = metadata,
                    }
                );
            }

            providerResult = providerResult with
            {
                Models = descriptors
                    .OrderBy(model => model.DisplayName ?? model.Id, StringComparer.OrdinalIgnoreCase)
                    .Take(maxPerProvider)
                    .ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Anthropic /v1/models endpoint.");
            warnings.Add($"Exception retrieving Anthropic models: {ex.Message}");
        }

        return new ProviderFetchResult(providerResult, warnings);
    }

    private static HashSet<string> NormalizeProviders(string? providers)
    {
        if (string.IsNullOrWhiteSpace(providers))
        {
            return new HashSet<string>(
                new[] { ProviderOpenAi, ProviderAnthropic },
                StringComparer.OrdinalIgnoreCase
            );
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in ParseProviderTokens(providers))
        {
            if (
                string.Equals(token, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "both", StringComparison.OrdinalIgnoreCase)
            )
            {
                normalized.Add(ProviderOpenAi);
                normalized.Add(ProviderAnthropic);
            }
            else if (string.Equals(token, ProviderOpenAi, StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add(ProviderOpenAi);
            }
            else if (string.Equals(token, ProviderAnthropic, StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add(ProviderAnthropic);
            }
        }

        if (normalized.Count == 0)
        {
            normalized.Add(ProviderOpenAi);
            normalized.Add(ProviderAnthropic);
        }

        return normalized;
    }

    private static IEnumerable<string> ParseProviderTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var trimmed = value.Trim();
        var tokens = new List<string>();
        var handled = false;

        // Attempt to parse JSON array or string
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var token = element.GetString();
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            tokens.Add(token.Trim());
                        }
                    }
                }

                handled = true;
            }

            if (!handled && doc.RootElement.ValueKind == JsonValueKind.String)
            {
                var token = doc.RootElement.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    tokens.Add(token.Trim());
                    handled = true;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON, fall through to simple parsing
        }

        if (!handled)
        {
            foreach (
                var token in trimmed.Split(
                    new[] { ',', ';', ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                tokens.Add(token.Trim());
            }
        }

        return tokens;
    }

    private static bool MatchesFilter(string id, string? match)
    {
        if (string.IsNullOrWhiteSpace(match))
        {
            return true;
        }

        return id.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IReadOnlyDictionary<string, object?>? ExtractSimpleMetadata(
        JsonElement element,
        string[] skipProperties
    )
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var skip = new HashSet<string>(skipProperties, StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            if (skip.Contains(property.Name))
            {
                continue;
            }

            var value = property.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    metadata[property.Name] = value.GetString();
                    break;
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out var longValue))
                    {
                        metadata[property.Name] = longValue;
                    }
                    else if (value.TryGetDouble(out var doubleValue))
                    {
                        metadata[property.Name] = doubleValue;
                    }
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    metadata[property.Name] = value.GetBoolean();
                    break;
            }
        }

        return metadata.Count > 0 ? metadata : null;
    }

    private static string BuildSummary(
        IReadOnlyCollection<ModelProviderResult> providers,
        IReadOnlyCollection<string> warnings
    )
    {
        var lines = new List<string>();
        if (providers.Count == 0)
        {
            lines.Add("No providers queried.");
        }

        foreach (var provider in providers)
        {
            if (provider.Models.Count == 0)
            {
                lines.Add($"{provider.ProviderName}: no models returned.");
                continue;
            }

            var headlineModels = provider.Models
                .Take(Math.Min(5, provider.Models.Count))
                .Select(model => model.Id)
                .ToArray();

            lines.Add(
                $"{provider.ProviderName}: {provider.Models.Count} models (top: {string.Join(", ", headlineModels)})."
            );
        }

        if (warnings.Count > 0)
        {
            lines.Add($"Warnings: {string.Join("; ", warnings)}");
        }

        return string.Join("\n", lines);
    }

    private sealed record ProviderFetchResult(ModelProviderResult Provider, List<string> Warnings);
}

/// <summary>
/// Result returned by the model catalog tool.
/// </summary>
public record ModelCatalogResult
{
    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<ModelProviderResult> Providers { get; init; } =
        Array.Empty<ModelProviderResult>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Models returned for a specific provider.
/// </summary>
public record ModelProviderResult
{
    public string ProviderId { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public IReadOnlyList<ModelDescriptor> Models { get; init; } = Array.Empty<ModelDescriptor>();
}

/// <summary>
/// Describes an individual AI model.
/// </summary>
public record ModelDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public string? Owner { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
