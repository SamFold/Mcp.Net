using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.Examples.LLMConsole.UI;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.LLMConsole.Elicitation;

/// <summary>
/// Console-based prompt provider that walks the operator through elicitation requests.
/// </summary>
public sealed class ConsoleElicitationPromptProvider : IElicitationPromptProvider
{
    private readonly ChatUI _chatUi;
    private readonly ILogger<ConsoleElicitationPromptProvider> _logger;

    public ConsoleElicitationPromptProvider(
        ChatUI chatUi,
        ILogger<ConsoleElicitationPromptProvider> logger
    )
    {
        _chatUi = chatUi;
        _logger = logger;
    }

    public Task<ElicitationClientResponse> PromptAsync(
        ElicitationRequestContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _chatUi.DisplayElicitationPrompt(context.Message, context.RequestedSchema);

        var action = PromptForAction(cancellationToken);

        if (action == ElicitationAction.Decline)
        {
            _chatUi.DisplayElicitationOutcome("Elicitation declined.");
            return Task.FromResult(ElicitationClientResponse.Decline());
        }

        if (action == ElicitationAction.Cancel)
        {
            _chatUi.DisplayElicitationOutcome("Elicitation cancelled.");
            return Task.FromResult(ElicitationClientResponse.Cancel());
        }

        var overrides = CollectOverrides(context, cancellationToken);
        if (overrides is null)
        {
            _chatUi.DisplayElicitationOutcome("Elicitation cancelled.");
            return Task.FromResult(ElicitationClientResponse.Cancel());
        }

        var payload =
            overrides.Count == 0
                ? JsonSerializer.SerializeToElement(new Dictionary<string, object>())
                : JsonSerializer.SerializeToElement(overrides);

        _chatUi.DisplayElicitationOutcome("Elicitation accepted.");
        return Task.FromResult(ElicitationClientResponse.Accept(payload));
    }

    private static ElicitationAction PromptForAction(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Write("Choose action ([A]ccept / [D]ecline / [C]ancel): ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            cancellationToken.ThrowIfCancellationRequested();

            switch (input)
            {
                case "a":
                case "accept":
                case "yes":
                case "y":
                    return ElicitationAction.Accept;
                case "d":
                case "decline":
                case "no":
                case "n":
                    return ElicitationAction.Decline;
                case "c":
                case "cancel":
                case "exit":
                    return ElicitationAction.Cancel;
            }

            Console.WriteLine("Please enter A, D, or C.");
        }
    }

    private Dictionary<string, object?>? CollectOverrides(
        ElicitationRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var schema = context.RequestedSchema;
        var results = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (schema.Properties.Count == 0)
        {
            _logger.LogInformation("Elicitation schema contains no properties; accepting defaults.");
            return results;
        }

        Console.WriteLine();
        Console.WriteLine("Provide values for the following fields (leave blank to keep default).");
        Console.WriteLine("Type 'cancel' at any prompt to cancel the elicitation.");
        Console.WriteLine();

        foreach (var (name, property) in schema.Properties)
        {
            var isRequired = schema.Required?.Contains(name) ?? false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine(
                    $"- {(!string.IsNullOrWhiteSpace(property.Title) ? property.Title : name)}"
                    + $"{(isRequired ? " (required)" : string.Empty)}"
                );
                if (!string.IsNullOrWhiteSpace(property.Description))
                {
                    Console.WriteLine($"  {property.Description}");
                }

                if (property.Enum is { Count: > 0 })
                {
                    Console.WriteLine($"  Options: {string.Join(", ", property.Enum)}");
                }

                Console.Write($"  Enter value for {name} [{property.Type}]: ");
                var raw = Console.ReadLine();
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(raw, "cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (isRequired)
                    {
                        Console.WriteLine("  This field is required. Please provide a value.");
                        continue;
                    }

                    Console.WriteLine("  (keeping server-provided default)");
                    break;
                }

                if (TryConvert(property, raw!, out var converted, out var error))
                {
                    results[name] = converted;
                    break;
                }

                Console.WriteLine($"  {error}");
            }

            Console.WriteLine();
        }

        return results;
    }

    private static bool TryConvert(
        ElicitationSchemaProperty property,
        string rawValue,
        out object? converted,
        out string? error
    )
    {
        converted = null;
        error = null;

        var type = property.Type?.ToLowerInvariant() ?? "string";

        switch (type)
        {
            case "string":
                if (!TryNormalizeString(property, rawValue, out var normalized, out error))
                {
                    return false;
                }

                converted = normalized;
                return true;

            case "number":
                if (!TryParseNumber(property, rawValue, out var number, out error))
                {
                    return false;
                }

                converted = number;
                return true;

            case "integer":
                if (!TryParseInteger(property, rawValue, out var integer, out error))
                {
                    return false;
                }

                converted = integer;
                return true;

            case "boolean":
            case "bool":
                if (
                    bool.TryParse(rawValue, out var boolean)
                    || string.Equals(rawValue, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rawValue, "y", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase)
                )
                {
                    converted =
                        boolean
                        || string.Equals(rawValue, "yes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(rawValue, "y", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase);
                    return true;
                }

                error = "Enter true/false, yes/no, or 1/0.";
                return false;

            default:
                converted = rawValue;
                return true;
        }
    }

    private enum ElicitationAction
    {
        Accept,
        Decline,
        Cancel,
    }

    private static bool TryNormalizeString(
        ElicitationSchemaProperty property,
        string rawValue,
        out string normalizedValue,
        out string? error
    )
    {
        error = null;
        normalizedValue = rawValue;

        if (property.Enum is { Count: > 0 })
        {
            var matchIndex = property
                .Enum.Select((value, index) => (Value: value, Index: index))
                .FirstOrDefault(
                    entry => string.Equals(entry.Value, rawValue, StringComparison.OrdinalIgnoreCase)
                );

            if (matchIndex == default && property.EnumNames is { Count: > 0 })
            {
                matchIndex = property
                    .EnumNames.Select((display, index) => (Value: display, Index: index))
                    .FirstOrDefault(
                        entry => string.Equals(entry.Value, rawValue, StringComparison.OrdinalIgnoreCase)
                    );
            }

            if (matchIndex == default)
            {
                error = $"Value must be one of: {string.Join(", ", property.Enum)}.";
                return false;
            }

            normalizedValue = property.Enum[matchIndex.Index];
        }

        if (!ValidateLength(property, normalizedValue, out error))
        {
            return false;
        }

        if (!ValidateFormat(property.Format, normalizedValue, out error))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateLength(
        ElicitationSchemaProperty property,
        string value,
        out string? error
    )
    {
        error = null;

        if (property.MinLength.HasValue && value.Length < property.MinLength.Value)
        {
            error = $"Value must be at least {property.MinLength.Value} characters.";
            return false;
        }

        if (property.MaxLength.HasValue && value.Length > property.MaxLength.Value)
        {
            error = $"Value must be at most {property.MaxLength.Value} characters.";
            return false;
        }

        return true;
    }

    private static bool ValidateFormat(string? format, string value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(format))
        {
            return true;
        }

        switch (format.Trim().ToLowerInvariant())
        {
            case "email":
                try
                {
                    _ = new MailAddress(value);
                    return true;
                }
                catch
                {
                    error = "Enter a valid email address.";
                    return false;
                }

            case "uri":
                if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    error = "Enter a valid absolute URI.";
                    return false;
                }

                return true;

            case "date":
                if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    error = "Enter a valid date (e.g., 2025-06-18).";
                    return false;
                }

                return true;

            case "date-time":
                if (
                    !DateTimeOffset.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out _
                    )
                    && !DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out _)
                )
                {
                    error = "Enter a valid date and time (ISO 8601).";
                    return false;
                }

                return true;

            default:
                return true;
        }
    }

    private static bool TryParseNumber(
        ElicitationSchemaProperty property,
        string rawValue,
        out double number,
        out string? error
    )
    {
        error = null;

        if (
            !double.TryParse(
                rawValue,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out number
            )
            && !double.TryParse(
                rawValue,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture,
                out number
            )
        )
        {
            error = "Enter a valid number.";
            return false;
        }

        if (property.Minimum.HasValue && number < property.Minimum.Value)
        {
            error = $"Value must be greater than or equal to {property.Minimum.Value}.";
            return false;
        }

        if (property.Maximum.HasValue && number > property.Maximum.Value)
        {
            error = $"Value must be less than or equal to {property.Maximum.Value}.";
            return false;
        }

        return true;
    }

    private static bool TryParseInteger(
        ElicitationSchemaProperty property,
        string rawValue,
        out long integer,
        out string? error
    )
    {
        error = null;

        if (
            !long.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out integer
            )
            && !long.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out integer
            )
        )
        {
            error = "Enter a valid whole number.";
            return false;
        }

        var numericValue = Convert.ToDouble(integer, CultureInfo.InvariantCulture);

        if (property.Minimum.HasValue && numericValue < property.Minimum.Value)
        {
            error = $"Value must be greater than or equal to {property.Minimum.Value}.";
            return false;
        }

        if (property.Maximum.HasValue && numericValue > property.Maximum.Value)
        {
            error = $"Value must be less than or equal to {property.Maximum.Value}.";
            return false;
        }

        return true;
    }
}
