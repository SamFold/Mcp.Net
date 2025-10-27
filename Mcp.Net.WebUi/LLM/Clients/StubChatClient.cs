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
    private readonly List<LlmMessage> _messageHistory = new();

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

    public Task<IEnumerable<LlmResponse>> SendMessageAsync(LlmMessage message)
    {
        _logger.LogInformation("[STUB] Received message: {Content}", message.Content);
        _messageHistory.Add(message);

        var response =
            $"[DEBUG] This is a stub response to your message: '{message.Content} at {DateTime.Now}";

        _messageHistory.Add(LlmMessage.FromAssistant(response));

        var llmResponse = new LlmResponse
        {
            Content = response,
            Type = MessageType.Assistant,
            Id = Guid.NewGuid().ToString(),
        };

        return Task.FromResult<IEnumerable<LlmResponse>>(new[] { llmResponse });
    }

    public Task<IEnumerable<LlmResponse>> SendToolResultsAsync(
        IEnumerable<ToolInvocationResult> toolResults
    )
    {
        _logger.LogInformation(
            "[STUB] Received tool results: {Count} results",
            toolResults.Count()
        );

        // Return a stub response
        var response = new LlmResponse
        {
            Type = MessageType.Assistant,
            Content =
                $"[DEBUG] This is a stub response to your tool results. {toolResults.Count()} tool(s) were called.",
        };

        return Task.FromResult<IEnumerable<LlmResponse>>(new[] { response });
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

    public void AddToolResultToHistory(ToolInvocationResult result)
    {
        _logger.LogInformation(
            "[STUB] Adding tool result to history: {ToolName}, {ToolCallId}",
            result.ToolName,
            result.ToolCallId
        );
    }

    public Task<List<LlmResponse>> GetLlmResponse()
    {
        var response = new LlmResponse
        {
            Type = MessageType.Assistant,
            Content = "[DEBUG] This is a stub response from GetLlmResponse()",
        };

        return Task.FromResult(new List<LlmResponse> { response });
    }
}
