using Mcp.Net.LLM.Events;
using Mcp.Net.LLM.Interfaces;
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
        sessionEvents.AssistantMessageReceived += OnAssistantMessageReceived;
        sessionEvents.ToolExecutionUpdated += OnToolExecutionUpdated;
        sessionEvents.ThinkingStateChanged += OnThinkingStateChanged;
    }

    private void OnSessionStarted(object? sender, EventArgs e)
    {
        _logger.LogDebug("Session started, drawing chat interface");
        _ui.DrawChatInterface();
    }

    private void OnAssistantMessageReceived(object? sender, string message)
    {
        _logger.LogDebug("Displaying assistant message");
        _ui.DisplayAssistantMessage(message);
    }

    private void OnToolExecutionUpdated(object? sender, ToolExecutionEventArgs args)
    {
        switch (args.ExecutionState)
        {
            case ToolExecutionState.Starting:
                _logger.LogDebug("Displaying tool execution start for {ToolName}", args.ToolName);
                _ui.DisplayToolExecution(args.ToolName);
                break;

            case ToolExecutionState.Completed:
                if (args.Success && args.Result != null)
                {
                    _logger.LogDebug("Displaying tool execution results for {ToolName}", args.ToolName);
                    _ui.DisplayToolResults(args.Result);
                }
                else
                {
                    var message = args.ErrorMessage ?? "Tool returned an error";
                    _logger.LogDebug("Displaying tool error for {ToolName}: {Error}", args.ToolName, message);
                    _ui.DisplayToolError(args.ToolName, message);
                }
                break;

            case ToolExecutionState.Failed:
            default:
                var error = args.ErrorMessage ?? "Tool execution failed";
                _logger.LogDebug("Displaying tool failure for {ToolName}: {Error}", args.ToolName, error);
                _ui.DisplayToolError(args.ToolName, error);
                break;
        }
    }

    private void OnThinkingStateChanged(object? sender, ThinkingStateEventArgs args)
    {
        if (args.IsThinking)
        {
            StartThinkingAnimation(args.Context);
        }
        else
        {
            StopThinkingAnimation();
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
