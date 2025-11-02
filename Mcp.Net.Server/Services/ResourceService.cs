using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Resources;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Services;

internal sealed class ResourceService : IResourceService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ResourceRegistration> _resources =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _resourceOrder = new();
    private readonly ILogger<ResourceService> _logger;

    private sealed record ResourceRegistration(
        Resource Resource,
        Func<CancellationToken, Task<ResourceContent[]>> Reader
    );

    public ResourceService(ILogger<ResourceService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterResource(
        Resource resource,
        Func<CancellationToken, Task<ResourceContent[]>> reader,
        bool overwrite = false
    )
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (string.IsNullOrWhiteSpace(resource.Uri))
        {
            throw new ArgumentException("Resource URI must be specified.", nameof(resource));
        }

        lock (_sync)
        {
            var key = resource.Uri;
            if (_resources.ContainsKey(key))
            {
                if (!overwrite)
                {
                    throw new InvalidOperationException(
                        $"Resource '{resource.Uri}' is already registered."
                    );
                }
            }
            else
            {
                _resourceOrder.Add(key);
            }

            _resources[key] = new ResourceRegistration(CloneResource(resource), reader);
        }

        _logger.LogInformation("Registered resource: {Uri}", resource.Uri);
    }

    public void RegisterResource(Resource resource, ResourceContent[] contents, bool overwrite = false)
    {
        if (contents == null)
        {
            throw new ArgumentNullException(nameof(contents));
        }

        RegisterResource(resource, _ => Task.FromResult(contents), overwrite);
    }

    public bool UnregisterResource(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("Resource URI must be specified.", nameof(uri));
        }

        lock (_sync)
        {
            var removed = _resources.Remove(uri);
            if (removed)
            {
                _resourceOrder.Remove(uri);
            }

            return removed;
        }
    }

    public IReadOnlyCollection<Resource> ListResources()
    {
        lock (_sync)
        {
            var resources = new List<Resource>(_resourceOrder.Count);
            foreach (var uri in _resourceOrder)
            {
                if (_resources.TryGetValue(uri, out var registration))
                {
                    resources.Add(CloneResource(registration.Resource));
                }
            }

            return resources;
        }
    }

    public async Task<ResourceContent[]> ReadResourceAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new McpException(ErrorCode.InvalidParams, "Invalid URI");
        }

        ResourceRegistration registration;
        lock (_sync)
        {
            if (!_resources.TryGetValue(uri, out registration!))
            {
                _logger.LogWarning("Resource not found: {Uri}", uri);
                throw new McpException(ErrorCode.ResourceNotFound, $"Resource not found: {uri}");
            }
        }

        try
        {
            var contents = await registration.Reader(CancellationToken.None).ConfigureAwait(false);
            return contents ?? Array.Empty<ResourceContent>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading resource {Uri}", uri);
            throw new McpException(ErrorCode.InternalError, $"Failed to read resource: {uri}");
        }
    }

    private static Resource CloneResource(Resource source)
    {
        return new Resource
        {
            Uri = source.Uri,
            Name = source.Name,
            Description = source.Description,
            MimeType = source.MimeType,
            Annotations = CloneDictionary(source.Annotations),
            Meta = CloneDictionary(source.Meta),
        };
    }

    private static IDictionary<string, object?>? CloneDictionary(
        IDictionary<string, object?>? source
    )
    {
        if (source == null)
        {
            return null;
        }

        return new Dictionary<string, object?>(source);
    }
}
