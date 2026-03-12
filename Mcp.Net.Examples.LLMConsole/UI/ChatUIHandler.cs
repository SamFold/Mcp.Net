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
    private readonly Dictionary<string, string> _renderedAssistantTextByEntryId =
        new(StringComparer.Ordinal);
    private CancellationTokenSource? _thinkingCts;
    private Task? _thinkingTask;
    private bool _assistantMessageInProgress;

    public ChatUIHandler(ChatUI ui, IChatSessionEvents sessionEvents, ILogger<ChatUIHandler> logger)
    {
        _ui = ui;
        _logger = logger;

        _logger.LogDebug("Drawing chat interface");
        _ui.DrawChatInterface();

        // Subscribe to events
        sessionEvents.TranscriptChanged += OnTranscriptChanged;
        sessionEvents.ActivityChanged += OnActivityChanged;
        sessionEvents.ToolCallActivityChanged += OnToolCallActivityChanged;
    }

    private void OnTranscriptChanged(object? sender, ChatTranscriptChangedEventArgs args)
    {
        if (args.Entry == null)
        {
            if (args.ChangeKind is ChatTranscriptChangeKind.Reset or ChatTranscriptChangeKind.Loaded)
            {
                _renderedAssistantTextByEntryId.Clear();
                _assistantMessageInProgress = false;
            }
            return;
        }

        switch (args.Entry)
        {
            case AssistantChatEntry assistant:
                DisplayAssistantUpdate(assistant);
                break;

            case ErrorChatEntry error:
                StopThinkingAnimation();
                CompleteAssistantMessageIfNeeded();
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

            case ChatSessionActivity.ExecutingTool:
                StopThinkingAnimation();
                CompleteAssistantMessageIfNeeded();
                break;

            case ChatSessionActivity.Idle:
                StopThinkingAnimation();
                CompleteAssistantMessageIfNeeded();
                break;
        }
    }

    private void OnToolCallActivityChanged(object? sender, ToolCallActivityChangedEventArgs args)
    {
        switch (args.ExecutionState)
        {
            case ToolCallExecutionState.Running:
                CompleteAssistantMessageIfNeeded();
                _logger.LogDebug("Displaying tool execution start for {ToolName}", args.ToolName);
                _ui.DisplayToolExecution(args.ToolName);
                break;

            case ToolCallExecutionState.Completed:
                if (args.Result != null)
                {
                    CompleteAssistantMessageIfNeeded();
                    _logger.LogDebug("Displaying tool execution results for {ToolName}", args.ToolName);
                    _ui.DisplayToolResults(args.Result);
                }
                break;

            case ToolCallExecutionState.Failed:
                CompleteAssistantMessageIfNeeded();
                var error = args.ErrorMessage ?? "Tool execution failed";
                _logger.LogDebug("Displaying tool failure for {ToolName}: {Error}", args.ToolName, error);
                _ui.DisplayToolError(args.ToolName, error);
                break;

            case ToolCallExecutionState.Cancelled:
                CompleteAssistantMessageIfNeeded();
                var canceled = args.ErrorMessage ?? "Tool execution canceled";
                _logger.LogDebug("Displaying tool cancellation for {ToolName}: {Error}", args.ToolName, canceled);
                _ui.DisplayToolError(args.ToolName, canceled);
                break;
        }
    }

    private void DisplayAssistantUpdate(AssistantChatEntry assistant)
    {
        StopThinkingAnimation();

        var textBlocks = assistant.Blocks.OfType<TextAssistantBlock>().ToList();
        if (textBlocks.Count == 0)
        {
            return;
        }

        var message = string.Join(Environment.NewLine, textBlocks.Select(block => block.Text));
        _renderedAssistantTextByEntryId.TryGetValue(assistant.Id, out var previousMessage);

        if (
            !string.IsNullOrEmpty(previousMessage)
            && !message.StartsWith(previousMessage, StringComparison.Ordinal)
        )
        {
            _logger.LogDebug(
                "Assistant entry {AssistantEntryId} changed non-monotonically; rendering full message.",
                assistant.Id
            );

            CompleteAssistantMessageIfNeeded();
            _ui.DisplayAssistantMessage(message);
            _assistantMessageInProgress = false;
            _renderedAssistantTextByEntryId[assistant.Id] = message;
            return;
        }

        var delta = previousMessage == null ? message : message[previousMessage.Length..];
        if (delta.Length == 0)
        {
            _renderedAssistantTextByEntryId[assistant.Id] = message;
            return;
        }

        _logger.LogDebug("Displaying assistant delta for entry {AssistantEntryId}", assistant.Id);
        _ui.DisplayAssistantDelta(delta);
        _assistantMessageInProgress = true;
        _renderedAssistantTextByEntryId[assistant.Id] = message;
    }

    private void CompleteAssistantMessageIfNeeded()
    {
        if (!_assistantMessageInProgress)
        {
            return;
        }

        _ui.CompleteAssistantMessage();
        _assistantMessageInProgress = false;
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
