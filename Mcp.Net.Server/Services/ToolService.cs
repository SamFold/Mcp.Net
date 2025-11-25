using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Tools;
using Microsoft.Extensions.Logging;
using Mcp.Net.Server.Logging;

namespace Mcp.Net.Server.Services;

internal sealed class ToolService : IToolService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Tool> _tools = new();
    private readonly Dictionary<string, Func<JsonElement?, string, Task<ToolCallResult>>> _handlers = new();
    private readonly ServerCapabilities _capabilities;
    private readonly ILogger<ToolService> _logger;
    private readonly IToolInvocationContextAccessor _contextAccessor;

    public ToolService(
        ServerCapabilities capabilities,
        ILogger<ToolService> logger,
        IToolInvocationContextAccessor contextAccessor
    )
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextAccessor =
            contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
    }

    public void RegisterTool(
        string name,
        string? description,
        JsonElement inputSchema,
        Func<JsonElement?, Task<ToolCallResult>> handler,
        IDictionary<string, object?>? annotations = null
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tool name must be provided.", nameof(name));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        EnsureToolsCapability();

        var tool = new Tool
        {
            Name = name,
            Description = description,
            InputSchema = inputSchema,
            Annotations = annotations != null ? CopyAnnotations(annotations) : null,
        };

        Func<JsonElement?, string, Task<ToolCallResult>> wrappedHandler = async (args, sessionId) =>
        {
            try
            {
                using var scope = _contextAccessor.Push(sessionId);
                _logger.LogInformation("Tool {ToolName} invoked", name);
                return await handler(args).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in tool handler: {ToolName}", name);
                return new ToolCallResult
                {
                    IsError = true,
                    Content = new[]
                    {
                        new TextContent { Text = ex.Message },
                        new TextContent { Text = $"Stack trace:\n{ex.StackTrace}" },
                    },
                };
            }
        };

        lock (_sync)
        {
            _tools[name] = tool;
            _handlers[name] = wrappedHandler;
        }

        _logger.LogInformation(
            "Registered tool: {ToolName} - {Description}",
            name,
            description ?? "No description"
        );
    }

    public IReadOnlyCollection<Tool> GetTools()
    {
        lock (_sync)
        {
            return _tools.Values.ToList().AsReadOnly();
        }
    }

    public async Task<ToolCallResult> ExecuteAsync(
        string toolName,
        JsonElement? arguments,
        string sessionId
    )
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier must be provided.", nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new McpException(ErrorCode.InvalidParams, "Tool name cannot be empty");
        }

        Func<JsonElement?, string, Task<ToolCallResult>> handler;
        lock (_sync)
        {
            if (!_handlers.TryGetValue(toolName, out var resolved) || resolved == null)
            {
                throw new McpException(ErrorCode.InvalidParams, $"Tool not found: {toolName}");
            }

            handler = resolved;
        }

        using (_logger.BeginToolScope<string>(toolName))
        using (_logger.BeginTimingScope($"Execute{toolName}Tool", LogLevel.Information))
        {
            if (arguments.HasValue)
            {
                var argsJson = arguments.Value.ToString();
                var truncated =
                    argsJson.Length > 500 ? $"{argsJson[..500]}... [truncated]" : argsJson;
                _logger.LogInformation(
                    "Executing tool {ToolName} with parameters: {Parameters}",
                    toolName,
                    truncated
                );
            }
            else
            {
                _logger.LogInformation("Executing tool {ToolName} with no parameters", toolName);
            }

            var result = await handler(arguments, sessionId).ConfigureAwait(false);

            if (result.IsError)
            {
                var errorMessage = result.Content?.FirstOrDefault()
                    is TextContent textContent
                    ? textContent.Text
                    : "Unknown error";

                _logger.LogWarning(
                    "Tool {ToolName} execution failed: {ErrorMessage}",
                    toolName,
                    errorMessage
                );
            }
            else
            {
                var contentCount = result.Content?.Count() ?? 0;
                _logger.LogInformation(
                    "Tool {ToolName} executed successfully, returned {ContentCount} content items",
                    toolName,
                    contentCount
                );
            }

            return result;
        }
    }

    private void EnsureToolsCapability()
    {
        if (_capabilities.Tools == null)
        {
            _capabilities.Tools = new { };
        }
    }

    private static IDictionary<string, object?> CopyAnnotations(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }
}
