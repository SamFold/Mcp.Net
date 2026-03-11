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

    public IChatCompletionStream SendAsync(
        ChatClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var latestUserMessage = request
            .Transcript
            .OfType<UserChatEntry>()
            .LastOrDefault()
            ?.Content ?? "(no user message)";

        _logger.LogInformation("[STUB] Received request with latest user message: {Content}", latestUserMessage);

        var response =
            $"[DEBUG] This is a stub response to your request: '{latestUserMessage}' at {DateTime.Now}";

        return ChatCompletionStream.FromResult(
            new ChatClientAssistantTurn(
                Guid.NewGuid().ToString("n"),
                _provider.ToString().ToLowerInvariant(),
                _options.Model ?? "stub",
                new AssistantContentBlock[] { new TextAssistantBlock(Guid.NewGuid().ToString("n"), response) }
            )
        );
    }
}
