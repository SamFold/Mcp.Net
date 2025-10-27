using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.LLM.Elicitation;
using Mcp.Net.LLM.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.LLM.Elicitation;

public class ElicitationCoordinatorTests
{
    [Fact]
    public async Task HandleAsync_ShouldDecline_WhenNoProviderRegistered()
    {
        var coordinator = new ElicitationCoordinator(NullLogger<ElicitationCoordinator>.Instance);
        var context = CreateContext();

        var response = await coordinator.HandleAsync(context, CancellationToken.None);

        response.Action.Should().Be("decline");
    }

    [Fact]
    public async Task HandleAsync_ShouldDelegateToProvider()
    {
        var coordinator = new ElicitationCoordinator(NullLogger<ElicitationCoordinator>.Instance);
        var provider = new StubProvider();
        coordinator.SetProvider(provider);
        var context = CreateContext();

        var response = await coordinator.HandleAsync(context, CancellationToken.None);

        response.Action.Should().Be("accept");
        provider.Invocations.Should().Be(1);
    }

    private static ElicitationRequestContext CreateContext()
    {
        var parameters = new
        {
            message = "Provide override",
            requestedSchema = new
            {
                type = "object",
                properties = new
                {
                    sample = new { type = "string" },
                },
            },
        };

        var request = new JsonRpcRequestMessage(
            JsonRpc: "2.0",
            Id: "1",
            Method: "elicitation/create",
            Params: parameters,
            Meta: null
        );

        return new ElicitationRequestContext(request);
    }

    private sealed class StubProvider : IElicitationPromptProvider
    {
        public int Invocations { get; private set; }

        public Task<ElicitationClientResponse> PromptAsync(
            ElicitationRequestContext context,
            CancellationToken cancellationToken
        )
        {
            Invocations++;
            var payload = JsonSerializer.SerializeToElement(new { value = true });
            return Task.FromResult(ElicitationClientResponse.Accept(payload));
        }
    }
}
