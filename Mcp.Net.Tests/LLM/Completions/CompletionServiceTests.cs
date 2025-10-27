using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.LLM.Completions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Mcp.Net.Tests.LLM.Completions;

public class CompletionServiceTests
{
    [Fact]
    public async Task CompletePromptAsync_ShouldCacheResponses()
    {
        var clientMock = new Mock<IMcpClient>();
        var response = new CompletionValues { Values = new[] { "python" }, Total = 3, HasMore = true };
        var invocationCount = 0;

        clientMock
            .Setup(m => m.CompleteAsync(
                It.Is<CompletionReference>(r => r.Type == "ref/prompt" && r.Name == "code_review"),
                It.Is<CompletionArgument>(a => a.Name == "language" && a.Value == "py"),
                It.IsAny<CompletionContext?>()
            ))
            .ReturnsAsync(() =>
            {
                invocationCount++;
                return response;
            });

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        var first = await service.CompletePromptAsync("code_review", "language", "py");
        var second = await service.CompletePromptAsync("code_review", "language", "py");

        invocationCount.Should().Be(1);
        first.Values.Should().Equal("python");
        second.Values.Should().Equal("python");
        second.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidatePrompt_ShouldRemoveCachedEntries()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock
            .Setup(m => m.CompleteAsync(
                It.IsAny<CompletionReference>(),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ))
            .ReturnsAsync(new CompletionValues { Values = new[] { "value" } });

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        await service.CompletePromptAsync("code_review", "language", "py");
        service.InvalidatePrompt("code_review");
        await service.CompletePromptAsync("code_review", "language", "py");

        clientMock.Verify(
            m => m.CompleteAsync(
                It.Is<CompletionReference>(r => r.Type == "ref/prompt"),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task CompletePromptAsync_ShouldRespectContextFingerprint()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock
            .Setup(m => m.CompleteAsync(
                It.IsAny<CompletionReference>(),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ))
            .ReturnsAsync(new CompletionValues());

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        var contextA = new Dictionary<string, string> { { "language", "python" } };
        var contextB = new Dictionary<string, string> { { "language", "java" } };

        await service.CompletePromptAsync("code_review", "framework", "fl", contextA);
        await service.CompletePromptAsync("code_review", "framework", "fl", contextB);

        clientMock.Verify(
            m => m.CompleteAsync(
                It.IsAny<CompletionReference>(),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task CompletePromptAsync_ShouldPropagateExceptions()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock
            .Setup(m => m.CompleteAsync(
                It.IsAny<CompletionReference>(),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ))
            .ThrowsAsync(new InvalidOperationException("capability missing"));

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        var act = async () => await service.CompletePromptAsync("code_review", "language", "py");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompleteResourceAsync_ShouldCachePerResource()
    {
        var clientMock = new Mock<IMcpClient>();
        var response = new CompletionValues { Values = new[] { "doc.txt" } };
        var calls = 0;

        clientMock
            .Setup(m => m.CompleteAsync(
                It.Is<CompletionReference>(r => r.Type == "ref/resource" && r.Uri == "file:///{name}"),
                It.Is<CompletionArgument>(a => a.Name == "name"),
                It.IsAny<CompletionContext?>()
            ))
            .ReturnsAsync(() =>
            {
                calls++;
                return response;
            });

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        await service.CompleteResourceAsync("file:///{name}", "name", "doc");
        await service.CompleteResourceAsync("file:///{name}", "name", "doc");

        calls.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateResource_ShouldClearSpecificEntries()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock
            .Setup(m => m.CompleteAsync(
                It.IsAny<CompletionReference>(),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ))
            .ReturnsAsync(new CompletionValues());

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        await service.CompleteResourceAsync("resource://demo", "arg", "value");
        service.InvalidateResource("resource://demo");
        await service.CompleteResourceAsync("resource://demo", "arg", "value");

        clientMock.Verify(
            m => m.CompleteAsync(
                It.Is<CompletionReference>(r => r.Type == "ref/resource"),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllCachedResults()
    {
        var clientMock = new Mock<IMcpClient>();
        clientMock
            .Setup(m => m.CompleteAsync(
                It.IsAny<CompletionReference>(),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ))
            .ReturnsAsync(new CompletionValues());

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        await service.CompletePromptAsync("code_review", "language", "py");
        service.Clear();
        await service.CompletePromptAsync("code_review", "language", "py");

        clientMock.Verify(
            m => m.CompleteAsync(
                It.Is<CompletionReference>(r => r.Type == "ref/prompt"),
                It.IsAny<CompletionArgument>(),
                It.IsAny<CompletionContext?>()
            ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task CompletePromptAsync_ShouldRespectCancellationToken()
    {
        var clientMock = new Mock<IMcpClient>();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new CompletionService(clientMock.Object, NullLogger<CompletionService>.Instance);

        var act = async () => await service.CompletePromptAsync("code_review", "language", "py", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        clientMock.Verify(
            m => m.CompleteAsync(It.IsAny<CompletionReference>(), It.IsAny<CompletionArgument>(), It.IsAny<CompletionContext?>()),
            Times.Never
        );
    }
}
