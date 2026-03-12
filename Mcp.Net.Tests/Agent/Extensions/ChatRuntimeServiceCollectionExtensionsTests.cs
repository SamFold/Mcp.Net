using FluentAssertions;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Extensions;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Mcp.Net.Tests.Agent.Extensions;

public class ChatRuntimeServiceCollectionExtensionsTests
{
    [Fact]
    public void AddChatRuntimeServices_ShouldRegisterOnlyChatRuntimeSurface()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddChatRuntimeServices();

        var provider = services.BuildServiceProvider();

        provider.GetService<IChatSessionFactory>().Should().NotBeNull();
        provider.GetService<IToolRegistry>().Should().BeNull();
        provider.GetService<ToolRegistry>().Should().BeNull();
        provider.GetService<IToolExecutor>().Should().BeNull();
    }

    [Fact]
    public void AddToolRegistry_ShouldRegisterToolRegistrySurface()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddToolRegistry();

        var provider = services.BuildServiceProvider();

        provider.GetService<IToolRegistry>().Should().BeOfType<ToolRegistry>();
        provider.GetService<ToolRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void AddChatSessionFactory_ShouldRegisterIChatSessionFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddChatSessionFactory();

        var provider = services.BuildServiceProvider();

        provider.GetService<IChatSessionFactory>().Should().NotBeNull();
    }

    [Fact]
    public void LegacyAgentManagementTypes_ShouldNotExistInAgentAssembly()
    {
        var assembly = typeof(ChatSession).Assembly;

        assembly.GetType("Mcp.Net.Agent.Models.AgentDefinition").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Models.AgentExecutionDefaults").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Models.AgentCategory").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Interfaces.IAgentFactory").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Interfaces.IAgentManager").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Interfaces.IAgentRegistry").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Interfaces.IAgentStore").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Agents.AgentFactory").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Agents.AgentManager").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Agents.AgentRegistry").Should().BeNull();
        assembly.GetType("Mcp.Net.Agent.Agents.DefaultAgentManager").Should().BeNull();
    }
}
