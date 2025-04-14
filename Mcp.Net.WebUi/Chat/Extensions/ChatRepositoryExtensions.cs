using Mcp.Net.WebUi.Chat.Interfaces;

namespace Mcp.Net.WebUi.Chat.Extensions;

/// <summary>
/// Extension methods for IChatRepository
/// </summary>
public static class ChatRepositoryExtensions
{
    /// <summary>
    /// Check if a message is the first message in a chat session
    /// </summary>
    /// <param name="repository">The chat repository</param>
    /// <param name="sessionId">The chat session ID</param>
    /// <returns>True if this is the first message in the session</returns>
    public static async Task<bool> IsFirstMessageAsync(this IChatRepository repository, string sessionId)
    {
        var messages = await repository.GetChatMessagesAsync(sessionId);
        return messages.Count == 0 || (messages.Count == 1 && messages[0].Type == "user");
    }
}