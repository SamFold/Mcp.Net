using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;
using Moq;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;

namespace Mcp.Net.Tests.Agent.Tools;

public class CompositeToolExecutorTests
{
    [Fact]
    public async Task LocalToolExecutor_ExecuteAsync_ShouldDispatchToRegisteredLocalTool()
    {
        RuntimeToolInvocation? capturedInvocation = null;
        var expected = CreateToolResult("call-1", "search_local", "sunny");
        var tool = new TestLocalTool(
            CreateToolDescriptor("search_local", "Searches local data"),
            (invocation, _) =>
            {
                capturedInvocation = invocation;
                return Task.FromResult(expected);
            }
        );
        var executor = new LocalToolExecutor(new[] { tool });

        var result = await executor.ExecuteAsync(
            new RuntimeToolInvocation(
                "call-1",
                "search_local",
                new Dictionary<string, object?> { ["query"] = "weather" }
            )
        );

        result.Should().BeEquivalentTo(expected);
        capturedInvocation.Should().NotBeNull();
        capturedInvocation!.ToolName.Should().Be("search_local");
        capturedInvocation.Arguments.Should().Contain("query", "weather");
    }

    [Fact]
    public async Task LocalToolExecutor_ExecuteAsync_ShouldThrowForUnknownTool()
    {
        var executor = new LocalToolExecutor(
            new[]
            {
                new TestLocalTool(
                    CreateToolDescriptor("search_local", "Searches local data"),
                    (_, _) => Task.FromResult(CreateToolResult("call-1", "search_local", "sunny"))
                ),
            }
        );

        var act = () => executor.ExecuteAsync(
            new RuntimeToolInvocation("call-1", "missing_tool", new Dictionary<string, object?>())
        );

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*missing_tool*");
    }

    [Fact]
    public async Task CompositeToolExecutor_ExecuteAsync_ShouldRouteRegisteredLocalToolsToLocalExecutor()
    {
        var expected = CreateToolResult("call-1", "search_local", "sunny");
        var fallbackExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        var executor = new CompositeToolExecutor(
            new LocalToolExecutor(
                new[]
                {
                    new TestLocalTool(
                        CreateToolDescriptor("search_local", "Searches local data"),
                        (_, _) => Task.FromResult(expected)
                    ),
                }
            ),
            fallbackExecutor.Object
        );

        var result = await executor.ExecuteAsync(
            new RuntimeToolInvocation("call-1", "search_local", new Dictionary<string, object?>())
        );

        result.Should().BeEquivalentTo(expected);
        fallbackExecutor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompositeToolExecutor_ExecuteAsync_ShouldFallbackForNonLocalTools()
    {
        var expected = CreateToolResult("call-2", "search_remote", "rainy");
        var fallbackExecutor = new Mock<IToolExecutor>();
        fallbackExecutor
            .Setup(executor => executor.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation => invocation.ToolName == "search_remote"),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(expected);

        var executor = new CompositeToolExecutor(
            new LocalToolExecutor(
                new[]
                {
                    new TestLocalTool(
                        CreateToolDescriptor("search_local", "Searches local data"),
                        (_, _) => Task.FromResult(CreateToolResult("call-1", "search_local", "sunny"))
                    ),
                }
            ),
            fallbackExecutor.Object
        );

        var result = await executor.ExecuteAsync(
            new RuntimeToolInvocation("call-2", "search_remote", new Dictionary<string, object?>())
        );

        result.Should().BeEquivalentTo(expected);
        fallbackExecutor.Verify(
            executor => executor.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation => invocation.ToolName == "search_remote"),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    private static Tool CreateToolDescriptor(string name, string description)
    {
        using var schemaDocument = JsonDocument.Parse("{}");
        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = schemaDocument.RootElement.Clone(),
        };
    }

    private static ToolInvocationResult CreateToolResult(
        string toolCallId,
        string toolName,
        params string[] text
    ) =>
        new(
            toolCallId,
            toolName,
            false,
            text,
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );

    private sealed class TestLocalTool(
        Tool descriptor,
        Func<RuntimeToolInvocation, CancellationToken, Task<ToolInvocationResult>> executeAsync
    ) : ILocalTool
    {
        public Tool Descriptor { get; } = descriptor;

        public Task<ToolInvocationResult> ExecuteAsync(
            RuntimeToolInvocation invocation,
            CancellationToken cancellationToken = default
        ) => executeAsync(invocation, cancellationToken);
    }
}
