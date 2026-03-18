using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Startup.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.WebUi.Startup;

public class LlmSettingsFactoryTests
{
    [Theory]
    [InlineData("anthropic", LlmProvider.Anthropic, "claude-sonnet-4-6")]
    [InlineData("openai", LlmProvider.OpenAI, "gpt-5.4")]
    public void CreateDefaultSettings_WithoutConfiguredModel_ShouldUseLatestProviderDefault(
        string providerName,
        LlmProvider expectedProvider,
        string expectedModel
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["LlmProvider"] = providerName,
                }
            )
            .Build();

        var settings = LlmSettingsFactory.CreateDefaultSettings(
            configuration,
            NullLogger.Instance
        );

        settings.Provider.Should().Be(expectedProvider);
        settings.ModelName.Should().Be(expectedModel);
    }
}
