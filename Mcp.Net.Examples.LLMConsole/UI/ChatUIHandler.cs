using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.LLMConsole.UI;

/// <summary>
/// Handles UI updates based on chat session events
/// </summary>
public class ChatUIHandler : IUserInputProvider
{
    private readonly ChatUI _ui;
    private readonly ILogger<ChatUIHandler> _logger;
    private CancellationTokenSource? _thinkingCts;
    private Task? _thinkingTask;

    public ChatUIHandler(ChatUI ui, IChatSessionEvents sessionEvents, ILogger<ChatUIHandler> logger)
    {
        _ui = ui;
        _logger = logger;

        // Subscribe to events
        sessionEvents.SessionStarted += OnSessionStarted;
        sessionEvents.TranscriptChanged += OnTranscriptChanged;
        sessionEvents.ActivityChanged += OnActivityChanged;
        sessionEvents.ToolCallActivityChanged += OnToolCallActivityChanged;
    }

    private void OnSessionStarted(object? sender, EventArgs e)
    {
        _logger.LogDebug("Session started, drawing chat interface");
        _ui.DrawChatInterface();
    }

    private void OnTranscriptChanged(object? sender, ChatTranscriptChangedEventArgs args)
    {
        switch (args.Entry)
        {
            case AssistantChatEntry assistant:
                var textBlocks = assistant.Blocks.OfType<TextAssistantBlock>().ToList();
                if (textBlocks.Count > 0)
                {
                    var message = string.Join(
                        Environment.NewLine,
                        textBlocks.Select(b => b.Text)
                    );
                    _logger.LogDebug("Displaying assistant message");
                    _ui.DisplayAssistantMessage(message);
                }
                break;

            case ErrorChatEntry error:
                _logger.LogDebug("Displaying error: {Message}", error.Message);
                _ui.DisplayToolError("Error", error.Message);
                break;
        }
    }

    private void OnActivityChanged(object? sender, ChatSessionActivityChangedEventArgs args)
    {
        switch (args.Activity)
        {
            case ChatSessionActivity.WaitingForProvider:
                StartThinkingAnimation("Waiting for LLM");
                break;

            case ChatSessionActivity.Idle:
                StopThinkingAnimation();
                break;
        }
    }

    private void OnToolCallActivityChanged(object? sender, ToolCallActivityChangedEventArgs args)
    {
        switch (args.ExecutionState)
        {
            case ToolCallExecutionState.Running:
                _logger.LogDebug("Displaying tool execution start for {ToolName}", args.ToolName);
                _ui.DisplayToolExecution(args.ToolName);
                break;

            case ToolCallExecutionState.Completed:
                if (args.Result != null)
                {
                    _logger.LogDebug("Displaying tool execution results for {ToolName}", args.ToolName);
                    _ui.DisplayToolResults(args.Result);
                }
                break;

            case ToolCallExecutionState.Failed:
                var error = args.ErrorMessage ?? "Tool execution failed";
                _logger.LogDebug("Displaying tool failure for {ToolName}: {Error}", args.ToolName, error);
                _ui.DisplayToolError(args.ToolName, error);
                break;
        }
    }

    private void StartThinkingAnimation(string context)
    {
        _logger.LogDebug("Starting thinking animation: {Context}", context);

        StopThinkingAnimation();

        _thinkingCts = new CancellationTokenSource();
        _thinkingTask = _ui.ShowThinkingAnimation(_thinkingCts.Token);
    }

    private void StopThinkingAnimation()
    {
        if (_thinkingCts != null)
        {
            _logger.LogDebug("Stopping thinking animation");

            _thinkingCts.Cancel();
            try
            {
                if (_thinkingTask != null)
                {
                    Task.WaitAll(new[] { _thinkingTask }, 1000);
                }
            }
            catch (TaskCanceledException) { }
            catch (AggregateException ex)
                when (ex.InnerExceptions.Any(e => e is TaskCanceledException)) { }
            finally
            {
                _thinkingCts.Dispose();
                _thinkingCts = null;
                _thinkingTask = null;
            }
        }
    }

    public string GetUserInput()
    {
        _logger.LogDebug("Getting user input");
        return _ui.GetUserInput();
    }
}
