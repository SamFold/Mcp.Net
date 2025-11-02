using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Prompts;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Services;

internal sealed class PromptService : IPromptService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PromptRegistration> _prompts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _promptOrder = new();
    private readonly ServerCapabilities _capabilities;
    private readonly ILogger<PromptService> _logger;

    private sealed record PromptRegistration(
        Prompt Prompt,
        Func<CancellationToken, Task<object[]>> MessagesFactory
    );

    public PromptService(ServerCapabilities capabilities, ILogger<PromptService> logger)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterPrompt(
        Prompt prompt,
        Func<CancellationToken, Task<object[]>> messagesFactory,
        bool overwrite = false
    )
    {
        if (prompt == null)
        {
            throw new ArgumentNullException(nameof(prompt));
        }

        if (messagesFactory == null)
        {
            throw new ArgumentNullException(nameof(messagesFactory));
        }

        if (string.IsNullOrWhiteSpace(prompt.Name))
        {
            throw new ArgumentException("Prompt name must be specified.", nameof(prompt));
        }

        EnsurePromptCapabilities();

        lock (_sync)
        {
            var key = prompt.Name;
            if (_prompts.ContainsKey(key))
            {
                if (!overwrite)
                {
                    throw new InvalidOperationException(
                        $"Prompt '{prompt.Name}' is already registered."
                    );
                }
            }
            else
            {
                _promptOrder.Add(key);
            }

            _prompts[key] = new PromptRegistration(ClonePrompt(prompt), messagesFactory);
        }

        _logger.LogInformation("Registered prompt: {PromptName}", prompt.Name);
    }

    public void RegisterPrompt(Prompt prompt, object[] messages, bool overwrite = false)
    {
        if (messages == null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        RegisterPrompt(prompt, _ => Task.FromResult(messages), overwrite);
    }

    public bool UnregisterPrompt(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Prompt name must be specified.", nameof(name));
        }

        lock (_sync)
        {
            var removed = _prompts.Remove(name);
            if (removed)
            {
                _promptOrder.Remove(name);
            }

            return removed;
        }
    }

    public IReadOnlyCollection<Prompt> ListPrompts()
    {
        lock (_sync)
        {
            var prompts = new List<Prompt>(_promptOrder.Count);
            foreach (var name in _promptOrder)
            {
                if (_prompts.TryGetValue(name, out var registration))
                {
                    prompts.Add(ClonePrompt(registration.Prompt));
                }
            }

            return prompts;
        }
    }

    public async Task<object[]> GetPromptMessagesAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpException(ErrorCode.InvalidParams, "Invalid prompt name");
        }

        PromptRegistration registration;
        lock (_sync)
        {
            if (!_prompts.TryGetValue(name, out registration!))
            {
                _logger.LogWarning("Prompt not found: {Name}", name);
                throw new McpException(ErrorCode.PromptNotFound, $"Prompt not found: {name}");
            }
        }

        try
        {
            var messages = await registration.MessagesFactory(CancellationToken.None)
                .ConfigureAwait(false);
            return messages ?? Array.Empty<object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating prompt {Name}", name);
            throw new McpException(ErrorCode.InternalError, $"Failed to generate prompt: {name}");
        }
    }

    private void EnsurePromptCapabilities()
    {
        if (_capabilities.Prompts == null)
        {
            _capabilities.Prompts = new { };
        }
    }

    private static Prompt ClonePrompt(Prompt source)
    {
        return new Prompt
        {
            Name = source.Name,
            Title = source.Title,
            Description = source.Description,
            Arguments = source.Arguments?.Select(ClonePromptArgument).ToArray(),
            Annotations = CloneDictionary(source.Annotations),
            Meta = CloneDictionary(source.Meta),
        };
    }

    private static PromptArgument ClonePromptArgument(PromptArgument source)
    {
        return new PromptArgument
        {
            Name = source.Name,
            Description = source.Description,
            Required = source.Required,
            Default = source.Default,
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
