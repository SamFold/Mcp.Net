using Mcp.Net.Core.Models.Resources;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Server.Models;

namespace Mcp.Net.Server.Services;

public interface IResourceService
{
    void RegisterResource(
        Resource resource,
        Func<HandlerRequestContext?, CancellationToken, Task<ResourceContent[]>> reader,
        bool overwrite = false
    );

    void RegisterResource(
        Resource resource,
        Func<CancellationToken, Task<ResourceContent[]>> reader,
        bool overwrite = false
    );

    void RegisterResource(Resource resource, ResourceContent[] contents, bool overwrite = false);

    bool UnregisterResource(string uri);

    IReadOnlyCollection<Resource> ListResources();

    Task<ResourceContent[]> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default,
        HandlerRequestContext? requestContext = null
    );
}
