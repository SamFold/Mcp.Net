using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.Core.Models.Exceptions;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Elicitation;

/// <summary>
/// Exposes methods for requesting additional user input via the MCP elicitation flow.
/// </summary>
public interface IElicitationService
{
    /// <summary>
    /// Sends an elicitation prompt to the connected client and awaits the user's response.
    /// </summary>
    /// <param name="prompt">Prompt description and schema.</param>
    /// <param name="cancellationToken">Cancellation token to abort the request.</param>
    /// <returns>The elicitation result from the client.</returns>
    Task<ElicitationResult> RequestAsync(
        ElicitationPrompt prompt,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Default implementation that uses <see cref="McpServer"/> to relay elicitation messages.
/// </summary>
public sealed class ElicitationService : IElicitationService
{
    private readonly McpServer _server;
    private readonly ILogger<ElicitationService> _logger;
    private readonly JsonSerializerOptions _serializerOptions;

    public ElicitationService(McpServer server, ILogger<ElicitationService> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    public async Task<ElicitationResult> RequestAsync(
        ElicitationPrompt prompt,
        CancellationToken cancellationToken = default
    )
    {
        if (prompt == null)
        {
            throw new ArgumentNullException(nameof(prompt));
        }

        if (string.IsNullOrWhiteSpace(prompt.Message))
        {
            throw new ArgumentException("Elicitation message must be provided.", nameof(prompt));
        }

        _logger.LogInformation("Requesting elicitation: {Message}", prompt.Message);

        var payload = new ElicitationCreateParams
        {
            Message = prompt.Message,
            RequestedSchema = prompt.RequestedSchema,
        };

        JsonRpcResponseMessage response = await _server
            .SendClientRequestAsync("elicitation/create", payload, cancellationToken)
            .ConfigureAwait(false);

        var resultElement = NormalizeResultElement(response.Result);
        ElicitationCreateResult? result;
        try
        {
            result = JsonSerializer.Deserialize<ElicitationCreateResult>(
                resultElement.GetRawText(),
                _serializerOptions
            );
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize elicitation response.");
            throw new McpException(
                ErrorCode.InternalError,
                "Client returned malformed elicitation response.",
                ex
            );
        }

        if (result == null)
        {
            throw new McpException(
                ErrorCode.InternalError,
                "Client returned empty elicitation response."
            );
        }

        var action = ParseAction(result.Action);
        var content = result.Content;

        if (action == ElicitationAction.Accept && content == null)
        {
            throw new McpException(
                ErrorCode.InvalidRequest,
                "Client accepted elicitation without providing content."
            );
        }

        _logger.LogInformation(
            "Elicitation completed with action {Action}",
            action
        );

        return new ElicitationResult(action, content);
    }

    private static ElicitationAction ParseAction(string action)
    {
        return action?.Trim().ToLowerInvariant() switch
        {
            "accept" => ElicitationAction.Accept,
            "decline" => ElicitationAction.Decline,
            "cancel" => ElicitationAction.Cancel,
            _ => throw new McpException(
                ErrorCode.InvalidRequest,
                $"Unknown elicitation action '{action}'."
            ),
        };
    }

    private JsonElement NormalizeResultElement(object? result)
    {
        if (result is JsonElement jsonElement)
        {
            return jsonElement;
        }

        if (result is null)
        {
            throw new McpException(
                ErrorCode.InternalError,
                "Client returned an unexpected elicitation response payload."
            );
        }

        try
        {
            return JsonSerializer.SerializeToElement(result, _serializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to normalize elicitation response payload.");
            throw new McpException(
                ErrorCode.InternalError,
                "Client returned an unexpected elicitation response payload.",
                ex
            );
        }
    }
}
