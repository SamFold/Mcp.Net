using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Server.Models;

namespace Mcp.Net.Server.Services;

public interface IPromptService
{
    void RegisterPrompt(
        Prompt prompt,
        Func<HandlerRequestContext?, CancellationToken, Task<object[]>> messagesFactory,
        bool overwrite = false
    );

    void RegisterPrompt(
        Prompt prompt,
        Func<CancellationToken, Task<object[]>> messagesFactory,
        bool overwrite = false
    );

    void RegisterPrompt(Prompt prompt, object[] messages, bool overwrite = false);

    bool UnregisterPrompt(string name);

    IReadOnlyCollection<Prompt> ListPrompts();

    Task<object[]> GetPromptMessagesAsync(
        string name,
        CancellationToken cancellationToken = default,
        HandlerRequestContext? requestContext = null
    );
}
