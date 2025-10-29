using OpenAI;
using OpenAI.Chat;

namespace Mcp.Net.WebUi.LLM.Services;

/// <summary>
/// Implementation of title generation service using OpenAI models
/// </summary>
public class TitleGenerationService : ITitleGenerationService
{
    private readonly ILogger<TitleGenerationService> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private const string PROMPT_TEMPLATE =
        "Generate a very short, concise title (2-4 words) for a chat that starts with this message. Respond with only the title, no explanations or quotes: {0}";

    public TitleGenerationService(
        ILogger<TitleGenerationService> logger,
        IConfiguration configuration
    )
    {
        _logger = logger;
        _apiKey =
            configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? "";
        _model = configuration["TitleGeneration:Model"] ?? "gpt-5-nano";
    }

    /// <inheritdoc/>
    public async Task<string> GenerateTitleAsync(string initialMessage)
    {
        try
        {
            _logger.LogDebug("Generating title for message: {InitialMessage}", initialMessage);

            var openAIClient = new OpenAIClient(_apiKey);
            var chatClient = openAIClient.GetChatClient(_model);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(
                    "You create short, concise titles (2-4 words) for chats based on their first message. Respond with only the title."
                ),
                new UserChatMessage(string.Format(PROMPT_TEMPLATE, initialMessage)),
            };

            var options = new ChatCompletionOptions();
            var response = await chatClient.CompleteChatAsync(messages, options);

            string title = response.Value.Content[0].Text.Trim();

            _logger.LogInformation("Generated title: {Title}", title);

            // Fallback if response is empty or too long
            if (string.IsNullOrWhiteSpace(title) || title.Length > 50)
            {
                _logger.LogWarning("Generated title was invalid, using default");
                return $"New Chat {DateTime.UtcNow:g}";
            }

            return title;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating title");
            return $"New Chat {DateTime.UtcNow:g}";
        }
    }
}
