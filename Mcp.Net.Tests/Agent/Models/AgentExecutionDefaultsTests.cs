using FluentAssertions;
using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Models;
using Xunit;

namespace Mcp.Net.Tests.Agent.Models;

public class AgentExecutionDefaultsTests
{
    [Fact]
    public void ExecutionDefaults_ShouldHydrateFromLegacyParameters()
    {
        var agent = new AgentDefinition
        {
            Parameters = new Dictionary<string, object>
            {
                ["temperature"] = 0.35f,
                ["max_tokens"] = "1024",
                ["top_p"] = 1.0f,
            },
        };

        agent.ExecutionDefaults.Temperature.Should().Be(0.35f);
        agent.ExecutionDefaults.MaxOutputTokens.Should().Be(1024);
    }

    [Fact]
    public void ExecutionDefaultsSetter_ShouldUpdateLegacyParametersWithoutDiscardingOtherValues()
    {
        var agent = new AgentDefinition
        {
            Parameters = new Dictionary<string, object>
            {
                ["top_p"] = 1.0f,
            },
        };

        agent.ExecutionDefaults = new AgentExecutionDefaults
        {
            Temperature = 0.6f,
            MaxOutputTokens = 4096,
        };

        agent.Parameters.Should().Contain("temperature", 0.6f);
        agent.Parameters.Should().Contain("max_tokens", 4096);
        agent.Parameters.Should().Contain("top_p", 1.0f);
    }

    [Fact]
    public void ExecutionDefaultsSetter_ShouldRoundTripToolChoiceThroughLegacyParameters()
    {
        var agent = new AgentDefinition
        {
            Parameters = new Dictionary<string, object>
            {
                ["top_p"] = 1.0f,
            },
        };

        agent.ExecutionDefaults = new AgentExecutionDefaults
        {
            ToolChoice = ChatToolChoice.ForTool("search"),
        };

        agent.Parameters.Should().Contain("tool_choice", "specific");
        agent.Parameters.Should().Contain("tool_name", "search");
        agent.Parameters.Should().Contain("top_p", 1.0f);
        agent.ExecutionDefaults.ToolChoice.Should().BeEquivalentTo(ChatToolChoice.ForTool("search"));
    }
}
