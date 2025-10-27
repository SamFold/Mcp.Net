using System;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.LLM.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Mcp.Net.Tests.LLM.Catalog;

public class PromptResourceCatalogTests
{
    [Fact]
    public async Task InitializeAsync_ShouldLoadPromptsAndResources()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock.Setup(m => m.ListPrompts()).ReturnsAsync(new[] { new Prompt { Name = "p" } });
        clientMock.Setup(m => m.ListResources()).ReturnsAsync(new[] { new Resource { Uri = "file://test" } });

        var catalog = new PromptResourceCatalog(clientMock.Object, NullLogger<PromptResourceCatalog>.Instance);
        await catalog.InitializeAsync();

        var prompts = await catalog.GetPromptsAsync();
        var resources = await catalog.GetResourcesAsync();

        prompts.Should().ContainSingle(p => p.Name == "p");
        resources.Should().ContainSingle(r => r.Uri == "file://test");
    }

    [Fact]
    public async Task HandleNotification_ShouldRefreshPrompts()
    {
        var clientMock = new Mock<IMcpClient>();
        var callCount = 0;
        var refreshTcs = new TaskCompletionSource<bool>();

        clientMock
            .Setup(m => m.ListPrompts())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount >= 2)
                {
                    refreshTcs.TrySetResult(true);
                }

                return callCount == 1
                    ? new[] { new Prompt { Name = "first" } }
                    : new[] { new Prompt { Name = "first" }, new Prompt { Name = "second" } };
            });

        clientMock.Setup(m => m.ListResources()).ReturnsAsync(Array.Empty<Resource>());

        var catalog = new PromptResourceCatalog(clientMock.Object, NullLogger<PromptResourceCatalog>.Instance);
        await catalog.InitializeAsync();

        catalog.HandleNotification(new JsonRpcNotificationMessage("2.0", "prompts/list_changed", null));

        await refreshTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        callCount.Should().BeGreaterThanOrEqualTo(2);

        await Task.Delay(20);

        var prompts = await catalog.GetPromptsAsync();
        prompts.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPromptsAsync_ShouldLazyLoadWhenNotInitialized()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock.Setup(m => m.ListPrompts()).ReturnsAsync(new[] { new Prompt { Name = "lazy" } });
        clientMock.Setup(m => m.ListResources()).ReturnsAsync(Array.Empty<Resource>());

        var catalog = new PromptResourceCatalog(clientMock.Object, NullLogger<PromptResourceCatalog>.Instance);

        var prompts = await catalog.GetPromptsAsync();

        prompts.Should().ContainSingle(p => p.Name == "lazy");
        clientMock.Verify(m => m.ListPrompts(), Times.Once);
    }

    [Fact]
    public async Task HandleNotification_ShouldRefreshResources()
    {
        var clientMock = new Mock<IMcpClient>();
        var resourceCalls = 0;
        var refreshTcs = new TaskCompletionSource<bool>();

        clientMock.Setup(m => m.ListPrompts()).ReturnsAsync(Array.Empty<Prompt>());
        clientMock
            .Setup(m => m.ListResources())
            .ReturnsAsync(() =>
            {
                resourceCalls++;
                if (resourceCalls >= 2)
                {
                    refreshTcs.TrySetResult(true);
                }

                return resourceCalls == 1
                    ? new[] { new Resource { Uri = "file://first" } }
                    : new[] { new Resource { Uri = "file://first" }, new Resource { Uri = "file://second" } };
            });

        var catalog = new PromptResourceCatalog(clientMock.Object, NullLogger<PromptResourceCatalog>.Instance);
        await catalog.InitializeAsync();

        catalog.HandleNotification(new JsonRpcNotificationMessage("2.0", "resources/list_changed", null));

        await refreshTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        resourceCalls.Should().BeGreaterThanOrEqualTo(2);

        await Task.Delay(20);

        var resources = await catalog.GetResourcesAsync();
        resources.Should().HaveCount(2);
    }

    [Fact]
    public async Task RefreshPromptsAsync_ShouldRaiseEvent()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock.Setup(m => m.ListPrompts()).ReturnsAsync(new[] { new Prompt { Name = "event" } });
        clientMock.Setup(m => m.ListResources()).ReturnsAsync(Array.Empty<Resource>());

        var catalog = new PromptResourceCatalog(clientMock.Object, NullLogger<PromptResourceCatalog>.Instance);
        var tcs = new TaskCompletionSource<bool>();

        catalog.PromptsUpdated += (_, prompts) =>
        {
            if (prompts.Count == 1)
            {
                tcs.TrySetResult(true);
            }
        };

        await catalog.RefreshPromptsAsync();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RefreshResourcesAsync_ShouldRaiseEvent()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock.Setup(m => m.ListPrompts()).ReturnsAsync(Array.Empty<Prompt>());
        clientMock.Setup(m => m.ListResources()).ReturnsAsync(new[] { new Resource { Uri = "file://event" } });

        var catalog = new PromptResourceCatalog(clientMock.Object, NullLogger<PromptResourceCatalog>.Instance);
        var tcs = new TaskCompletionSource<bool>();

        catalog.ResourcesUpdated += (_, resources) =>
        {
            if (resources.Count == 1)
            {
                tcs.TrySetResult(true);
            }
        };

        await catalog.RefreshResourcesAsync();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
