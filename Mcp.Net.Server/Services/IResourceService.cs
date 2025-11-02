using Mcp.Net.Core.Models.Resources;
using Mcp.Net.Core.Models.Content;

namespace Mcp.Net.Server.Services;

public interface IResourceService
{
    void RegisterResource(
        Resource resource,
        Func<CancellationToken, Task<ResourceContent[]>> reader,
        bool overwrite = false
    );

    void RegisterResource(Resource resource, ResourceContent[] contents, bool overwrite = false);

    bool UnregisterResource(string uri);

    IReadOnlyCollection<Resource> ListResources();

    Task<ResourceContent[]> ReadResourceAsync(string uri);
}

