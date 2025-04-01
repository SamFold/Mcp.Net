using System.Linq;
using System.Text.Json;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Examples.LLM.Interfaces;
using Mcp.Net.Examples.LLM.Models;
using Mcp.Net.Examples.LLM.UI;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.LLM;

public class ChatSession
{
    private readonly IChatClient _llmClient;
    private readonly IMcpClient _mcpClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<ChatSession> _logger;
    private readonly ChatUI _ui;

    public ChatSession(
        IChatClient llmClient,
        IMcpClient mcpClient,
        ToolRegistry toolRegistry,
        ILogger<ChatSession> logger
    )
    {
        _llmClient = llmClient;
        _mcpClient = mcpClient;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _ui = new ChatUI();
    }

    public async Task Start()
    {
        // Draw futuristic chat interface
        _ui.DrawChatInterface();

        while (true)
        {
            var userInput = _ui.GetUserInput();
            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            _logger.LogDebug("Getting initial response for user message");
            var responseQueue = new Queue<LlmResponse>(await ProcessUserMessage(userInput));
            _logger.LogDebug("Initial response queue has {Count} items", responseQueue.Count);

            // Process the current "turn" of the conversation
            while (responseQueue.Count > 0)
            {
                // First, handle all text responses from Claude
                List<LlmResponse> textResponses = new();
                List<LlmResponse> toolResponses = new();

                // Sort responses by type
                while (responseQueue.Count > 0)
                {
                    var response = responseQueue.Dequeue();
                    if (response.MessageType == MessageType.Assistant)
                    {
                        textResponses.Add(response);
                    }
                    else if (response.MessageType == MessageType.Tool)
                    {
                        toolResponses.Add(response);
                    }
                }

                // Display all text responses
                foreach (var textResponse in textResponses)
                {
                    _logger.LogDebug(
                        "Processing assistant message: {MessagePreview}...",
                        textResponse.Text.Substring(0, Math.Min(30, textResponse.Text.Length))
                    );
                    await ProcessMessageResponse(textResponse);
                }

                // If we have tool responses, process all of them and batch the results
                if (toolResponses.Count > 0)
                {
                    List<Models.ToolCall> allToolResults = new();

                    // Execute all tool calls and collect their results
                    foreach (var toolResponse in toolResponses)
                    {
                        var toolCalls = toolResponse.ToolCalls;
                        _logger.LogDebug(
                            "Found {Count} tool calls to process in response",
                            toolCalls.Count
                        );

                        var toolCallResults = await ExecuteToolCalls(toolCalls);
                        _logger.LogDebug("Got {Count} tool results back", toolCallResults.Count);

                        allToolResults.AddRange(toolCallResults);
                    }

                    _logger.LogDebug("Total of {Count} tool results to send", allToolResults.Count);

                    // Check if we're using AnthropicChatClient for optimized processing
                    if (_llmClient is AnthropicChatClient anthropicClient)
                    {
                        // Add all tool results to history first
                        foreach (var toolResult in allToolResults)
                        {
                            _logger.LogDebug(
                                "Adding tool result for {ToolName} with ID {ToolId} to history",
                                toolResult.Name,
                                toolResult.Id
                            );
                            anthropicClient.AddToolResultToHistory(
                                toolResult.Id,
                                toolResult.Name,
                                toolResult.Results
                            );
                        }

                        // Now make a single API call to get the next response
                        _logger.LogDebug("Making single API call after adding all tool results");
                        var nextResponses = await anthropicClient.GetLlmResponse();

                        // Enqueue all new responses
                        foreach (var response in nextResponses)
                        {
                            _logger.LogDebug(
                                "Enqueueing response of type {MessageType} from batch call",
                                response.MessageType
                            );
                            responseQueue.Enqueue(response);
                        }
                    }
                    else
                    {
                        // Fallback for other client types - process one by one
                        foreach (var toolResult in allToolResults)
                        {
                            _logger.LogDebug(
                                "Sending individual tool result for {ToolName}",
                                toolResult.Name
                            );
                            var newResponses = await SendToolResult(toolResult);

                            foreach (var response in newResponses)
                            {
                                responseQueue.Enqueue(response);
                            }
                        }
                    }
                }
            }
        }
    }

    private async Task<List<LlmResponse>> SendToolResult(Models.ToolCall toolCall)
    {
        // This checks if we're using AnthropicChatClient and uses the optimized approach
        if (_llmClient is AnthropicChatClient anthropicClient)
        {
            // Add tool result to history without making an API call
            anthropicClient.AddToolResultToHistory(toolCall.Id, toolCall.Name, toolCall.Results);

            // Return empty list since we're not making an API call yet
            return new List<LlmResponse>();
        }
        else
        {
            // Fallback for other client types
            return await _llmClient.SendMessageAsync(
                new LlmMessage
                {
                    Type = MessageType.Tool,
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    ToolResults = toolCall.Results,
                }
            );
        }
    }

    private async Task<List<LlmResponse>> ProcessUserMessage(string userInput)
    {
        var userMessage = new LlmMessage { Type = MessageType.User, Content = userInput };

        // Show thinking animation while waiting for response
        using (var cts = new CancellationTokenSource())
        {
            // Start the thinking animation in a separate task
            var animationTask = _ui.ShowThinkingAnimation(cts.Token);

            // Get the response from the LLM
            var response = await _llmClient.SendMessageAsync(userMessage);

            // Stop the animation
            cts.Cancel();
            try
            {
                await animationTask;
            }
            catch (TaskCanceledException)
            {
                // Expected cancellation, no need to handle
            }

            return response;
        }
    }

    /// <summary>
    /// Given a Message response back from the LLM, print it out to the screen.
    /// </summary>
    /// <param name="response"></param>
    /// <returns></returns>
    private async Task ProcessMessageResponse(LlmResponse response)
    {
        _ui.DisplayAssistantMessage(response.Text);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Given a list of ToolCalls to make, executes the ToolCalls with the MCP Server and returns a list of results
    /// </summary>
    /// <param name="toolCalls"></param>
    /// <returns></returns>
    private async Task<List<Models.ToolCall>> ExecuteToolCalls(List<Models.ToolCall> toolCalls)
    {
        var results = new List<Models.ToolCall>();

        foreach (var toolCall in toolCalls)
        {
            results.Add(await ExecuteToolCall(toolCall));
        }

        return results;
    }

    /// <summary>
    /// Given a ToolCall, execute the ToolCall (happens on the MCP Server), and return the ToolCall with its Results.
    /// </summary>
    /// <param name="toolCall"></param>
    /// <returns></returns>
    private async Task<Models.ToolCall> ExecuteToolCall(Models.ToolCall toolCall)
    {
        // Display tool execution UI
        _ui.DisplayToolExecution(toolCall.Name);

        _logger.LogInformation("Executing tool: {ToolName}", toolCall.Name);

        var tool = _toolRegistry.GetToolByName(toolCall.Name);
        try
        {
            if (tool == null)
            {
                // Display error UI
                _ui.DisplayToolError(toolCall.Name, "Tool not found");

                _logger.LogError("Tool {ToolName} not found", toolCall.Name);
                throw new NullReferenceException("Tool wasn't found");
            }

            // Call the tool through MCP with thinking animation
            _logger.LogDebug(
                "Calling tool {ToolName} with arguments: {@Arguments}",
                tool.Name,
                toolCall.Arguments
            );

            // Show thinking animation while waiting for tool execution
            using (var cts = new CancellationTokenSource())
            {
                // Start the thinking animation in a separate task
                var animationTask = _ui.ShowThinkingAnimation(cts.Token);

                // Execute the tool
                var result = await _mcpClient.CallTool(tool.Name, toolCall.Arguments);

                // Stop the animation
                cts.Cancel();
                try
                {
                    await animationTask;
                }
                catch (TaskCanceledException)
                {
                    // Expected cancellation, no need to handle
                }

                // Convert result to dictionary
                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(result)
                );

                switch (resultDict)
                {
                    case null:
                        _logger.LogError("Tool {ToolName} returned null results", toolCall.Name);
                        throw new NullReferenceException("Results were null");
                    default:
                        _logger.LogDebug("Tool {ToolName} execution successful", toolCall.Name);
                        toolCall.Results = resultDict;
                        return toolCall;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing tool {ToolName}: {ErrorMessage}",
                toolCall.Name,
                ex.Message
            );
            var errorResponse = $"Error executing tool {toolCall.Name}: {ex.Message}";
            toolCall.Results = new Dictionary<string, object> { { "Error", errorResponse } };
            return toolCall;
        }
    }
}
