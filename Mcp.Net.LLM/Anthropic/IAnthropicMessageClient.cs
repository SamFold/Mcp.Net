using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace Mcp.Net.LLM.Anthropic;

/// <summary>
/// Thin abstraction over the Anthropic SDK messages client so callers can mock responses in tests.
/// </summary>
internal interface IAnthropicMessageClient
{
    Task<MessageResponse> GetResponseAsync(
        MessageParameters parameters,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<MessageResponse> StreamResponseAsync(
        MessageParameters parameters,
        CancellationToken cancellationToken = default
    );
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

    public Task<MessageResponse> GetResponseAsync(
        MessageParameters parameters,
        CancellationToken cancellationToken = default
    )
        => _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

    public IAsyncEnumerable<MessageResponse> StreamResponseAsync(
        MessageParameters parameters,
        CancellationToken cancellationToken = default
    ) => _client.Messages.StreamClaudeMessageAsync(parameters, cancellationToken);
}
