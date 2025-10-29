using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Examples.SimpleServer.Services;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.SimpleServer;

/// <summary>
/// Provides tools for executing small C# snippets via Roslyn scripting.
/// </summary>
[McpTool(
    "csharp_runner",
    "Execute short C# snippets for experimentation or tutoring scenarios.",
    Category = "development",
    CategoryDisplayName = "Developer Tools"
)]
public sealed class CodeExecutionTools
{
    private const int MaxOutputLength = 32768;

    private readonly CSharpCodeExecutionService _executionService;
    private readonly ILogger<CodeExecutionTools> _logger;

    public CodeExecutionTools(
        CSharpCodeExecutionService executionService,
        ILogger<CodeExecutionTools> logger
    )
    {
        _executionService = executionService;
        _logger = logger;
    }

    /// <summary>
    /// Executes the provided C# snippet and returns captured output, errors, and timing.
    /// </summary>
    [McpTool(
        "csharp_runner_execute",
        "Run a C# snippet (self-contained or raw script) and capture its console output.",
        Category = "development",
        CategoryDisplayName = "Developer Tools"
    )]
    public async Task<CodeExecutionToolResponse> ExecuteAsync(
        [McpParameter(
            required: true,
            description:
                "C# snippet to execute. For the default self-contained mode provide statements suitable for a Main method body."
        )]
            string code,
        [McpParameter(
            description:
                "Execution mode. Use 'self-contained' (default) to wrap statements inside a helper Main method, or 'raw' to run the script exactly as provided."
        )]
            string? mode = null,
        [McpParameter(
            description:
                "Maximum execution window in milliseconds. Defaults to 5000ms. Specify -1 for no timeout."
        )]
            int timeoutMs = 5000
    )
    {
        var warnings = new List<string>();
        CodeExecutionMode executionMode = ParseExecutionMode(mode, warnings);
        int effectiveTimeout = NormalizeTimeout(timeoutMs, warnings);

        try
        {
            var result = await _executionService.ExecuteAsync(
                code,
                executionMode,
                effectiveTimeout
            );

            var response = new CodeExecutionToolResponse
            {
                Success = result.Success,
                Mode = executionMode.ToString(),
                TimeoutMs = effectiveTimeout == Timeout.Infinite ? -1 : effectiveTimeout,
                ExecutionTimeMs = result.ExecutionTimeMs,
                Error = result.Error,
                Output = result.Output,
                Warnings = warnings,
            };

            if (!string.IsNullOrEmpty(result.Output))
            {
                var (trimmedOutput, wasTrimmed) = TrimOutput(result.Output);
                response.Output = trimmedOutput;
                response.OutputTruncated = wasTrimmed;

                if (wasTrimmed)
                {
                    warnings.Add(
                        $"Output exceeded {MaxOutputLength:N0} characters and was truncated."
                    );
                }
            }

            if (!result.Success && string.IsNullOrWhiteSpace(response.Error))
            {
                response.Error = "Execution failed due to an unknown error.";
            }

            return response;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "Code execution rejected due to invalid timeout.");
            warnings.Add(ex.Message);

            return CodeExecutionToolResponse.FromFailure(executionMode, effectiveTimeout, warnings);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Code execution rejected due to invalid input.");
            warnings.Add(ex.Message);

            return CodeExecutionToolResponse.FromFailure(executionMode, effectiveTimeout, warnings);
        }
    }

    private static (string Output, bool WasTrimmed) TrimOutput(string output)
    {
        if (output.Length <= MaxOutputLength)
        {
            return (output, false);
        }

        string truncated = output[..MaxOutputLength];
        return (truncated, true);
    }

    private static CodeExecutionMode ParseExecutionMode(string? mode, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return CodeExecutionMode.SelfContained;
        }

        string normalized = mode.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "self-contained":
            case "selfcontained":
            case "self_contained":
            case "sc":
            case "default":
                return CodeExecutionMode.SelfContained;
            case "raw":
            case "as-is":
            case "asis":
            case "script":
                return CodeExecutionMode.RawScript;
            default:
                warnings.Add(
                    $"Unknown execution mode '{mode}'. Falling back to 'self-contained'."
                );
                return CodeExecutionMode.SelfContained;
        }
    }

    private static int NormalizeTimeout(int timeoutMs, List<string> warnings)
    {
        if (timeoutMs == Timeout.Infinite || timeoutMs == -1)
        {
            return Timeout.Infinite;
        }

        if (timeoutMs <= 0)
        {
            warnings.Add("Timeout must be positive. Using the default of 5000ms.");
            return 5000;
        }

        const int maxTimeout = 30000;
        if (timeoutMs > maxTimeout)
        {
            warnings.Add($"Timeout capped at {maxTimeout}ms to protect the server.");
            return maxTimeout;
        }

        return timeoutMs;
    }
}

/// <summary>
/// Tool response payload describing execution output and diagnostics.
/// </summary>
public sealed class CodeExecutionToolResponse
{
    public bool Success { get; set; }

    public string Mode { get; set; } = string.Empty;

    public int TimeoutMs { get; set; }

    public long ExecutionTimeMs { get; set; }

    public string? Output { get; set; }

    public bool OutputTruncated { get; set; }

    public string? Error { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    public static CodeExecutionToolResponse FromFailure(
        CodeExecutionMode mode,
        int timeoutMs,
        IReadOnlyList<string> warnings
    )
    {
        return new CodeExecutionToolResponse
        {
            Success = false,
            Mode = mode.ToString(),
            TimeoutMs = timeoutMs == Timeout.Infinite ? -1 : timeoutMs,
            ExecutionTimeMs = 0,
            Output = null,
            Error = string.Join(Environment.NewLine, warnings),
            Warnings = warnings,
        };
    }
}
