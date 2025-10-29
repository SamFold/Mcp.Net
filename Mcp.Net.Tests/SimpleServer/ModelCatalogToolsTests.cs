using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Examples.SimpleServer;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.SimpleServer;

public class ModelCatalogToolsTests
{
    private const string OpenAiResponseJson =
        """
        {
          "object": "list",
          "data": [
            {
              "id": "gpt-5",
              "object": "model",
              "created": 1750000000,
              "owned_by": "openai"
            },
            {
              "id": "gpt-5",
              "object": "model",
              "created": 1740000000,
              "owned_by": "openai"
            }
          ]
        }
        """;

    private const string AnthropicResponseJson =
        """
        {
          "data": [
            {
              "id": "claude-sonnet-4-5-20250929",
              "display_name": "Claude Sonnet 4.5",
              "description": "Balanced flagship model",
              "context_limit": 200000
            },
            {
              "id": "claude-opus-4-1-20250805",
              "display_name": "Claude Opus 4.1",
              "description": "Most capable reasoning model"
            }
          ]
        }
        """;

    [Fact]
    public async Task ListModelsAsync_ReturnsAggregatedProviders()
    {
        using var openAiClient = CreateClient(OpenAiResponseJson);
        using var anthropicClient = CreateClient(AnthropicResponseJson);

        var factory = new StubHttpClientFactory(
            new Dictionary<string, HttpClient>
            {
                [ModelCatalogTools.OpenAiClientName] = openAiClient,
                [ModelCatalogTools.AnthropicClientName] = anthropicClient,
            }
        );

        var tool = new ModelCatalogTools(factory, NullLogger<ModelCatalogTools>.Instance);
        using var env = new TemporaryEnvironment(
            ("OPENAI_API_KEY", "openai-test"),
            ("ANTHROPIC_API_KEY", "anthropic-test")
        );

        var result = await tool.ListModelsAsync();

        Assert.Equal(2, result.Providers.Count);

        var openAi = result.Providers.Single(p => p.ProviderId == "openai");
        Assert.Equal("OpenAI", openAi.ProviderName);
        Assert.Equal(2, openAi.Models.Count);
        Assert.Equal("gpt-5", openAi.Models[0].Id);
        Assert.Equal("openai", openAi.Models[0].Owner);

        var anthropic = result.Providers.Single(p => p.ProviderId == "anthropic");
        Assert.Equal("Anthropic", anthropic.ProviderName);
        Assert.Equal(2, anthropic.Models.Count);
        Assert.Contains(
            "claude-sonnet-4-5-20250929",
            anthropic.Models.Select(model => model.Id)
        );
        Assert.Contains(
            "claude-opus-4-1-20250805",
            anthropic.Models.Select(model => model.Id)
        );

        Assert.Contains("OpenAI", result.Summary);
        Assert.Contains("Anthropic", result.Summary);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ListModelsAsync_HonoursProviderFilterAndMatch()
    {
        using var openAiClient = CreateClient(OpenAiResponseJson);
        using var anthropicClient = CreateClient(AnthropicResponseJson);

        var factory = new StubHttpClientFactory(
            new Dictionary<string, HttpClient>
            {
                [ModelCatalogTools.OpenAiClientName] = openAiClient,
                [ModelCatalogTools.AnthropicClientName] = anthropicClient,
            }
        );

        var tool = new ModelCatalogTools(factory, NullLogger<ModelCatalogTools>.Instance);
        using var env = new TemporaryEnvironment(
            ("OPENAI_API_KEY", "openai-test"),
            ("ANTHROPIC_API_KEY", "anthropic-test")
        );

        var result = await tool.ListModelsAsync(
            providers: "anthropic",
            match: "sonnet",
            maxPerProvider: 10,
            includeMetadata: false
        );

        Assert.Single(result.Providers);
        var anthropic = result.Providers.Single();
        Assert.Single(anthropic.Models);
        Assert.Equal("claude-sonnet-4-5-20250929", anthropic.Models[0].Id);
        Assert.Null(anthropic.Models[0].Metadata);
    }

    [Fact]
    public async Task ListModelsAsync_ReportsMissingApiKeys()
    {
        using var openAiClient = CreateClient(OpenAiResponseJson);
        using var anthropicClient = CreateClient(AnthropicResponseJson);

        var factory = new StubHttpClientFactory(
            new Dictionary<string, HttpClient>
            {
                [ModelCatalogTools.OpenAiClientName] = openAiClient,
                [ModelCatalogTools.AnthropicClientName] = anthropicClient,
            }
        );

        var tool = new ModelCatalogTools(factory, NullLogger<ModelCatalogTools>.Instance);
        using var env = new TemporaryEnvironment(("OPENAI_API_KEY", null), ("ANTHROPIC_API_KEY", null));

        var result = await tool.ListModelsAsync();

        Assert.True(result.Warnings.Count >= 2);
        Assert.Contains(result.Warnings, w => w.Contains("OPENAI_API_KEY"));
        Assert.Contains(result.Warnings, w => w.Contains("ANTHROPIC_API_KEY"));
        Assert.All(result.Providers, provider => Assert.Empty(provider.Models));
    }

    private static HttpClient CreateClient(string jsonResponse)
    {
        var handler = new StubMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
        });

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
    }

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly IDictionary<string, HttpClient> _clients;

        public StubHttpClientFactory(IDictionary<string, HttpClient> clients)
        {
            _clients = clients;
        }

        public HttpClient CreateClient(string name)
        {
            if (_clients.TryGetValue(name, out var client))
            {
                return client;
            }

            throw new InvalidOperationException($"No HttpClient registered for '{name}'.");
        }
    }

    private sealed class TemporaryEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.OrdinalIgnoreCase);

        public TemporaryEnvironment(params (string Key, string? Value)[] variables)
        {
            foreach (var (key, value) in variables)
            {
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _previousValues)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }
    }
}
