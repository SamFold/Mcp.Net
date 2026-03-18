using OpenAI.Responses;

namespace Mcp.Net.LLM.OpenAI;

#pragma warning disable OPENAI001
#pragma warning disable SCME0001

internal interface IOpenAiResponsesInvoker
{
    Task<ResponseResult> CreateResponseAsync(
        ResponsesClient client,
        CreateResponseOptions options,
        CancellationToken cancellationToken
    );
}

internal sealed class OpenAiResponsesInvoker : IOpenAiResponsesInvoker
{
    public async Task<ResponseResult> CreateResponseAsync(
        ResponsesClient client,
        CreateResponseOptions options,
        CancellationToken cancellationToken
    )
    {
        return (await client.CreateResponseAsync(options, cancellationToken)).Value;
    }
}

#pragma warning restore SCME0001
#pragma warning restore OPENAI001
