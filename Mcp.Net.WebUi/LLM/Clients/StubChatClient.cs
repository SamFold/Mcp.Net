using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;

namespace Mcp.Net.WebUi.LLM.Clients;

/// <summary>
/// Simple stub implementation of IChatClient for debugging purposes
/// </summary>
public class StubChatClient : IChatClient
{
    private readonly ILogger<StubChatClient> _logger;
    private readonly LlmProvider _provider;
    private readonly ChatClientOptions _options;
    private string _systemPrompt = "You are a helpful AI assistant.";
    private readonly List<string> _messageHistory = new();

    public StubChatClient(LlmProvider provider, ChatClientOptions options)
    {
        _provider = provider;
        _options = options;
        _logger = LoggerFactory
            .Create(builder => builder.AddConsole())
            .CreateLogger<StubChatClient>();
        _logger.LogInformation(
            "Created stub chat client for {Provider}, model: {Model}",
            provider,
            options.Model
        );
    }

    public void RegisterTools(IEnumerable<Tool> tools)
    {
        _logger.LogInformation("Registered {Count} tools with stub chat client", tools.Count());
    }

    public Task<ChatClientTurnResult> SendMessageAsync(
        string userMessage,
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    )
    {
        _ = assistantTurnUpdates;
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("[STUB] Received message: {Content}", userMessage);
        _messageHistory.Add(userMessage);

        var response =
            $"[DEBUG] This is a stub response to your message: '{userMessage}' at {DateTime.Now}";

        _messageHistory.Add(response);

        return Task.FromResult<ChatClientTurnResult>(
            new ChatClientAssistantTurn(
                Guid.NewGuid().ToString("n"),
                _provider.ToString().ToLowerInvariant(),
                _options.Model ?? "stub",
                new AssistantContentBlock[] { new TextAssistantBlock(Guid.NewGuid().ToString("n"), response) }
            )
        );
    }

    public Task<ChatClientTurnResult> SendToolResultsAsync(
        IEnumerable<ToolInvocationResult> toolResults,
        IProgress<ChatClientAssistantTurn>? assistantTurnUpdates = null,
        CancellationToken cancellationToken = default
    )
    {
        _ = assistantTurnUpdates;
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "[STUB] Received tool results: {Count} results",
            toolResults.Count()
        );

        // Return a stub response
        var response =
            $"[DEBUG] This is a stub response to your tool results. {toolResults.Count()} tool(s) were called.";

        return Task.FromResult<ChatClientTurnResult>(
            new ChatClientAssistantTurn(
                Guid.NewGuid().ToString("n"),
                _provider.ToString().ToLowerInvariant(),
                _options.Model ?? "stub",
                new AssistantContentBlock[] { new TextAssistantBlock(Guid.NewGuid().ToString("n"), response) }
            )
        );
    }

    public void ResetConversation()
    {
        _logger.LogInformation("[STUB] Conversation reset");
        _messageHistory.Clear();
    }

    public void SetSystemPrompt(string systemPrompt)
    {
        _logger.LogInformation("[STUB] Setting system prompt: {SystemPrompt}", systemPrompt);
        _systemPrompt = systemPrompt;
    }

    public string GetSystemPrompt()
    {
        return _systemPrompt;
    }

    public ReplayTarget GetReplayTarget() =>
        new(_provider.ToString().ToLowerInvariant(), _options.Model ?? "stub");

    public void LoadReplayTranscript(ProviderReplayTranscript replayTranscript)
    {
        ArgumentNullException.ThrowIfNull(replayTranscript);

        _messageHistory.Clear();
        foreach (var entry in replayTranscript.Entries)
        {
            switch (entry)
            {
                case UserChatEntry user:
                    _messageHistory.Add(user.Content);
                    break;
                case AssistantChatEntry assistant:
                    foreach (var block in assistant.Blocks.OfType<TextAssistantBlock>())
                    {
                        _messageHistory.Add(block.Text);
                    }
                    break;
                case ToolResultChatEntry toolResult:
                    foreach (var line in toolResult.Result.Text)
                    {
                        _messageHistory.Add(line);
                    }
                    break;
            }
        }
    }
}
