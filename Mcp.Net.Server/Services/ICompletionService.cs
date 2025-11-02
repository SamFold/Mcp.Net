using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Server.Completions;

namespace Mcp.Net.Server.Services;

public interface ICompletionService
{
    void RegisterPromptCompletion(string promptName, CompletionHandler handler, bool overwrite = false);

    void RegisterResourceCompletion(string resourceUri, CompletionHandler handler, bool overwrite = false);

    Task<CompletionValues> CompleteAsync(CompletionCompleteParams request, CancellationToken cancellationToken);
}

