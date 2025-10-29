using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Mcp.Net.WebUi.LLM.Services;

/// <summary>
/// Implementation of one-off LLM service supporting both OpenAI and Anthropic
/// </summary>
public class OneOffLlmService : IOneOffLlmService
{
    private readonly ILogger<OneOffLlmService> _logger;
    private readonly Dictionary<LlmProvider, string> _apiKeys;

    public OneOffLlmService(ILogger<OneOffLlmService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _apiKeys = new Dictionary<LlmProvider, string>
        {
            {
                LlmProvider.OpenAI,
                configuration["OpenAI:ApiKey"]
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? ""
            },
            {
                LlmProvider.Anthropic,
                configuration["Anthropic:ApiKey"]
                    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                    ?? ""
            },
        };
    }

    /// <inheritdoc/>
    public async Task<string> GetCompletionAsync(
        string systemPrompt,
        string userPrompt,
        string model = "gpt-5",
        LlmProvider? provider = null
    )
    {
        // Determine provider based on model name if not explicitly specified
        LlmProvider resolvedProvider =
            provider ?? (model.StartsWith("claude") ? LlmProvider.Anthropic : LlmProvider.OpenAI);

        _logger.LogDebug(
            "Getting one-off LLM completion using {Provider} with model {Model}",
            resolvedProvider,
            model
        );

        try
        {
            if (resolvedProvider == LlmProvider.OpenAI)
            {
                return await GetOpenAICompletionAsync(systemPrompt, userPrompt, model);
            }
            else
            {
                return await GetAnthropicCompletionAsync(systemPrompt, userPrompt, model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error getting one-off LLM completion from {Provider}",
                resolvedProvider
            );
            return string.Empty;
        }
    }

    private async Task<string> GetOpenAICompletionAsync(
        string systemPrompt,
        string userPrompt,
        string model
    )
    {
        var openAIClient = new OpenAIClient(_apiKeys[LlmProvider.OpenAI]);
        var chatClient = openAIClient.GetChatClient(model);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        };

        var options = new ChatCompletionOptions();
        var response = await chatClient.CompleteChatAsync(messages, options);
        return response.Value.Content[0].Text.Trim();
    }

    private async Task<string> GetAnthropicCompletionAsync(
        string systemPrompt,
        string userPrompt,
        string model
    )
    {
        var client = new AnthropicClient(_apiKeys[LlmProvider.Anthropic]);

        var parameters = new MessageParameters
        {
            Model = model,
            MaxTokens = 150,
            System = new List<SystemMessage> { new SystemMessage(systemPrompt) },
            Messages = new List<Message>
            {
                new Message
                {
                    Role = RoleType.User,
                    Content = new List<ContentBase> { new TextContent { Text = userPrompt } },
                },
            },
        };

        var response = await client.Messages.GetClaudeMessageAsync(parameters);
        var textContent = response.Content.FirstOrDefault(c => c is TextContent) as TextContent;
        return textContent?.Text.Trim() ?? string.Empty;
    }
}
