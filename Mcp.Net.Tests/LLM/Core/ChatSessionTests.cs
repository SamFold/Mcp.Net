using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Core;
using Mcp.Net.LLM.Events;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Mcp.Net.Tests.LLM.Core;

public class ChatSessionTests
{
    [Fact]
    public async Task SendUserMessageAsync_TextResponse_ShouldRaiseAssistantEvent()
    {
        // Arrange
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        var assistantResponse = new LlmResponse
        {
            Type = MessageType.Assistant,
            Content = "Hello from the model",
        };

        llmClient
            .Setup(c => c.SendMessageAsync(It.IsAny<LlmMessage>()))
            .ReturnsAsync(new[] { assistantResponse });

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        string? capturedUserMessage = null;
        string? capturedAssistantMessage = null;
        var thinkingStates = new List<bool>();

        session.UserMessageReceived += (_, msg) => capturedUserMessage = msg;
        session.AssistantMessageReceived += (_, msg) => capturedAssistantMessage = msg;
        session.ThinkingStateChanged += (_, args) => thinkingStates.Add(args.IsThinking);

        // Act
        await session.SendUserMessageAsync("Hi there");

        // Assert
        capturedUserMessage.Should().Be("Hi there");
        capturedAssistantMessage.Should().Be("Hello from the model");

        llmClient.Verify(
            c => c.SendMessageAsync(It.Is<LlmMessage>(m => m.Type == MessageType.User)),
            Times.Once
        );

        llmClient.Verify(
            c => c.SendToolResultsAsync(It.IsAny<IEnumerable<ToolInvocationResult>>()),
            Times.Never
        );

        thinkingStates.Should().Equal(true, false);
    }

    [Fact]
    public async Task SendUserMessageAsync_ToolCall_ShouldExecuteToolAndEmitEvents()
    {
        // Arrange
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        var toolArguments = new Dictionary<string, object?> { ["a"] = 2.0, ["b"] = 3.0 };
        var toolInvocation = new ToolInvocation("call-1", "calculator_add", toolArguments);
        var toolResponse = new LlmResponse
        {
            Type = MessageType.Tool,
            ToolCalls = new List<ToolInvocation> { toolInvocation },
        };

        llmClient
            .Setup(c => c.SendMessageAsync(It.IsAny<LlmMessage>()))
            .ReturnsAsync(new[] { toolResponse });

        var finalAssistantResponse = new LlmResponse
        {
            Type = MessageType.Assistant,
            Content = "Result is 5",
        };

        List<ToolInvocationResult>? capturedToolResults = null;
        llmClient
            .Setup(c => c.SendToolResultsAsync(It.IsAny<IEnumerable<ToolInvocationResult>>()))
            .ReturnsAsync(new[] { finalAssistantResponse })
            .Callback<IEnumerable<ToolInvocationResult>>(results =>
            {
                capturedToolResults = results.ToList();
            });

        using var schemaDocument = JsonDocument.Parse("{}");
        var tool = new Tool
        {
            Name = "calculator_add",
            Description = "Adds two numbers",
            InputSchema = schemaDocument.RootElement.Clone(),
        };

        toolRegistry.Setup(r => r.GetToolByName("calculator_add")).Returns(tool);

        var toolCallResult = new ToolCallResult
        {
            IsError = false,
            Content = new ContentBase[] { new TextContent { Text = "5" } },
        };

        mcpClient
            .Setup(c => c.CallTool("calculator_add", It.IsAny<object?>()))
            .ReturnsAsync(toolCallResult);

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        var toolEvents = new List<ToolExecutionEventArgs>();
        session.ToolExecutionUpdated += (_, args) => toolEvents.Add(args);

        string? capturedAssistantMessage = null;
        session.AssistantMessageReceived += (_, msg) => capturedAssistantMessage = msg;

        var thinkingStates = new List<bool>();
        session.ThinkingStateChanged += (_, args) => thinkingStates.Add(args.IsThinking);

        // Act
        await session.SendUserMessageAsync("Please add numbers");

        // Assert
        mcpClient.Verify(
            c => c.CallTool("calculator_add", It.Is<object>(o => o is IReadOnlyDictionary<string, object?>)),
            Times.Once
        );

        toolRegistry.Verify(r => r.GetToolByName("calculator_add"), Times.Once);

        toolEvents.Should().HaveCount(2);
        toolEvents[0].ExecutionState.Should().Be(ToolExecutionState.Starting);
        toolEvents[0].Success.Should().BeTrue();
        toolEvents[0].Result.Should().BeNull();
        toolEvents[1].ExecutionState.Should().Be(ToolExecutionState.Completed);
        toolEvents[1].Success.Should().BeTrue();
        toolEvents[1].Result.Should().NotBeNull();
        toolEvents[1].Result!.Text.Should().ContainSingle().Which.Should().Be("5");

        capturedToolResults.Should().NotBeNull();
        capturedToolResults!.Should().HaveCount(1);
        capturedToolResults[0].ToolName.Should().Be("calculator_add");

        capturedAssistantMessage.Should().Be("Result is 5");
        thinkingStates.Should().Equal(true, false, true, false);

        llmClient.Verify(
            c => c.SendToolResultsAsync(It.Is<IEnumerable<ToolInvocationResult>>(r => r.Count() == 1)),
            Times.Once
        );
    }

    [Fact]
    public async Task SendUserMessageAsync_MissingTool_ShouldEmitFailureAndForwardErrorResult()
    {
        // Arrange
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        var toolInvocation = new ToolInvocation(
            "missing-1",
            "nonexistent_tool",
            new Dictionary<string, object?>()
        );

        llmClient
            .Setup(c => c.SendMessageAsync(It.IsAny<LlmMessage>()))
            .ReturnsAsync(new[]
            {
                new LlmResponse
                {
                    Type = MessageType.Tool,
                    ToolCalls = new List<ToolInvocation> { toolInvocation },
                },
            });

        var assistantFallback = new LlmResponse
        {
            Type = MessageType.Assistant,
            Content = "Tool failed",
        };

        List<ToolInvocationResult>? capturedToolResults = null;
        llmClient
            .Setup(c => c.SendToolResultsAsync(It.IsAny<IEnumerable<ToolInvocationResult>>()))
            .ReturnsAsync(new[] { assistantFallback })
            .Callback<IEnumerable<ToolInvocationResult>>(results =>
            {
                capturedToolResults = results.ToList();
            });

        toolRegistry.Setup(r => r.GetToolByName("nonexistent_tool")).Returns((Tool?)null);

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        var toolEvents = new List<ToolExecutionEventArgs>();
        session.ToolExecutionUpdated += (_, args) => toolEvents.Add(args);

        string? capturedAssistantMessage = null;
        session.AssistantMessageReceived += (_, msg) => capturedAssistantMessage = msg;

        var thinkingStates = new List<bool>();
        session.ThinkingStateChanged += (_, args) => thinkingStates.Add(args.IsThinking);

        // Act
        await session.SendUserMessageAsync("Invoke missing tool");

        // Assert
        mcpClient.Verify(c => c.CallTool(It.IsAny<string>(), It.IsAny<object?>()), Times.Never);

        toolRegistry.Verify(r => r.GetToolByName("nonexistent_tool"), Times.Once);

        toolEvents.Should().HaveCount(1);
        toolEvents[0].ExecutionState.Should().Be(ToolExecutionState.Failed);
        toolEvents[0].Success.Should().BeFalse();
        toolEvents[0].ErrorMessage.Should().Be("Tool not found in registry");
        toolEvents[0].Result.Should().NotBeNull();
        toolEvents[0].Result!.IsError.Should().BeTrue();
        toolEvents[0].Result!.Text.Any(text => text.Contains("Tool not found")).Should().BeTrue();

        capturedToolResults.Should().NotBeNull();
        capturedToolResults!.Should().HaveCount(1);
        capturedToolResults[0].IsError.Should().BeTrue();
        capturedToolResults[0].Text.Any(text => text.Contains("Tool not found")).Should().BeTrue();

        capturedAssistantMessage.Should().Be("Tool failed");
        thinkingStates.Should().Equal(true, false, true, false);

        llmClient.Verify(
            c => c.SendToolResultsAsync(It.Is<IEnumerable<ToolInvocationResult>>(r => r.Count() == 1)),
            Times.Once
        );
    }
}
