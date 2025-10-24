using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Core.Models.Elicitation;

namespace Mcp.Net.Examples.SimpleClient.Elicitation;

/// <summary>
/// Sample console-based elicitation handler that walks the user through each schema property.
/// </summary>
public static class ConsoleElicitationHandler
{
    public static Task<ElicitationClientResponse> HandleAsync(
        ElicitationRequestContext context,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine();
        Console.WriteLine("=== Elicitation Request ===");
        Console.WriteLine(context.Message);
        Console.WriteLine();

        var action = PromptForAction(cancellationToken);

        if (action == ElicitationAction.Decline)
        {
            Console.WriteLine("Elicitation declined by user.");
            return Task.FromResult(ElicitationClientResponse.Decline());
        }

        if (action == ElicitationAction.Cancel)
        {
            Console.WriteLine("Elicitation cancelled by user.");
            return Task.FromResult(ElicitationClientResponse.Cancel());
        }

        var payload = CollectOverrides(context, cancellationToken);
        if (payload is null)
        {
            Console.WriteLine("Elicitation cancelled by user.");
            return Task.FromResult(ElicitationClientResponse.Cancel());
        }

        return Task.FromResult(
            payload.Count == 0
                ? ElicitationClientResponse.Accept(JsonSerializer.SerializeToElement(new Dictionary<string, object>()))
                : ElicitationClientResponse.Accept(
                    JsonSerializer.SerializeToElement(payload)
                )
        );
    }

    private static ElicitationAction PromptForAction(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Write("Choose action ([A]ccept / [D]ecline / [C]ancel): ");
            var input = Console.ReadLine();
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            switch (input.Trim().ToLowerInvariant())
            {
                case "a":
                case "accept":
                case "ok":
                case "yes":
                    return ElicitationAction.Accept;
                case "d":
                case "decline":
                case "no":
                    return ElicitationAction.Decline;
                case "c":
                case "cancel":
                case "exit":
                    return ElicitationAction.Cancel;
            }

            Console.WriteLine("Please enter A, D, or C.");
        }
    }

    private static Dictionary<string, object>? CollectOverrides(
        ElicitationRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var results = new Dictionary<string, object>(StringComparer.Ordinal);
        var required = new HashSet<string>(context.RequestedSchema.Required ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

        if (context.RequestedSchema.Properties.Count == 0)
        {
            Console.WriteLine("No structured fields defined; accepting without overrides.");
            return results;
        }

        Console.WriteLine();
        Console.WriteLine("Provide values for the following fields. Leave blank to keep the server defaults.");
        Console.WriteLine("Type 'cancel' at any prompt to cancel the elicitation.");
        Console.WriteLine();

        foreach (var (name, property) in context.RequestedSchema.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var title = string.IsNullOrWhiteSpace(property.Title) ? name : property.Title!;
            var description = property.Description;
            var isRequired = required.Contains(name);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"- {title}{(isRequired ? " (required)" : string.Empty)}");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    Console.WriteLine($"  {description}");
                }

                if (property.Enum is { Count: > 0 })
                {
                    Console.WriteLine($"  Options: {string.Join(", ", property.Enum)}");
                }

                Console.Write($"  Enter value for {name} [{property.Type}]: ");
                var input = Console.ReadLine();
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(input, "cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    if (isRequired)
                    {
                        Console.WriteLine("  This field is required. Please provide a value.");
                        continue;
                    }

                    Console.WriteLine("  (keeping server-provided value)");
                    break;
                }

                if (TryConvertValue(property, input!, out var convertedValue, out var error))
                {
                    results[name] = convertedValue!;
                    break;
                }

                Console.WriteLine($"  {error} Please try again.");
            }

            Console.WriteLine();
        }

        return results;
    }

    private static bool TryConvertValue(
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
                if (property.Enum is { Count: > 0 })
                {
                    var match = property.Enum.FirstOrDefault(
                        option => string.Equals(option, rawValue, StringComparison.OrdinalIgnoreCase)
                    );

                    if (match == null)
                    {
                        error = $"Value must be one of: {string.Join(", ", property.Enum)}.";
                        return false;
                    }

                    converted = match;
                    return true;
                }

                converted = rawValue;
                return true;

            case "boolean":
                if (
                    rawValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || rawValue.Equals("y", StringComparison.OrdinalIgnoreCase)
                )
                {
                    converted = true;
                    return true;
                }

                if (
                    rawValue.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || rawValue.Equals("no", StringComparison.OrdinalIgnoreCase)
                    || rawValue.Equals("n", StringComparison.OrdinalIgnoreCase)
                )
                {
                    converted = false;
                    return true;
                }

                error = "Enter true/false or y/n.";
                return false;

            case "number":
            case "integer":
            {
                if (!double.TryParse(rawValue, out var numeric))
                {
                    error = "Enter a numeric value.";
                    return false;
                }

                if (property.Minimum.HasValue && numeric < property.Minimum.Value)
                {
                    error = $"Value must be ≥ {property.Minimum.Value}.";
                    return false;
                }

                if (property.Maximum.HasValue && numeric > property.Maximum.Value)
                {
                    error = $"Value must be ≤ {property.Maximum.Value}.";
                    return false;
                }

                if (type == "integer")
                {
                    converted = (int)Math.Round(numeric);
                }
                else
                {
                    converted = numeric;
                }

                return true;
            }
        }

        converted = rawValue;
        return true;
    }
}
