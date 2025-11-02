using Mcp.Net.Core.Models.Prompts;

namespace Mcp.Net.Server.Services;

public interface IPromptService
{
    void RegisterPrompt(
        Prompt prompt,
        Func<CancellationToken, Task<object[]>> messagesFactory,
        bool overwrite = false
    );

    void RegisterPrompt(Prompt prompt, object[] messages, bool overwrite = false);

    bool UnregisterPrompt(string name);

    IReadOnlyCollection<Prompt> ListPrompts();

    Task<object[]> GetPromptMessagesAsync(string name);
}

