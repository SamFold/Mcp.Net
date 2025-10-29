using System.Collections.Generic;
using System.Threading.Tasks;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace Mcp.Net.LLM.Anthropic;

/// <summary>
/// Thin abstraction over the Anthropic SDK messages client so callers can mock responses in tests.
/// </summary>
internal interface IAnthropicMessageClient
{
    Task<IReadOnlyList<ContentBase>> GetResponseContentAsync(MessageParameters parameters);
}

/// <summary>
/// Default wrapper that forwards calls to <see cref="AnthropicClient" />.
/// </summary>
internal sealed class AnthropicMessageClient : IAnthropicMessageClient
{
    private readonly AnthropicClient _client;

    public AnthropicMessageClient(string apiKey)
        : this(new AnthropicClient(apiKey))
    {
    }

    public AnthropicMessageClient(AnthropicClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<ContentBase>> GetResponseContentAsync(MessageParameters parameters)
    {
        var response = await _client.Messages.GetClaudeMessageAsync(parameters);
        return response.Content ?? new List<ContentBase>();
    }
}
