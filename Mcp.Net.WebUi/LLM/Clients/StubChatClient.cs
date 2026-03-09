using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;

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

    public Task<ChatClientTurnResult> SendMessageAsync(string userMessage)
    {
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
        IEnumerable<ToolInvocationResult> toolResults
    )
    {
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

}
