using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Mcp.Net.Examples.SimpleServer.Services;

/// <summary>
/// Executes user-provided C# snippets using Roslyn scripting with optional wrapping modes.
/// </summary>
public class CSharpCodeExecutionService
{
    private static readonly ScriptOptions DefaultScriptOptions =
        ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Console).Assembly,
                typeof(Task).Assembly,
                typeof(Enumerable).Assembly,
                typeof(StringBuilder).Assembly
            )
            .AddImports(
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Text",
                "System.Threading",
                "System.Threading.Tasks"
            );

    /// <summary>
    /// Executes C# code with the provided options, returning captured output or error details.
    /// </summary>
    /// <param name="code">Code to execute.</param>
    /// <param name="mode">Execution wrapping mode.</param>
    /// <param name="timeoutMs">Execution timeout in milliseconds.</param>
    /// <param name="cancellationToken">Token to cancel the execution.</param>
    public async Task<CodeExecutionResult> ExecuteAsync(
        string code,
        CodeExecutionMode mode,
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code cannot be empty.", nameof(code));
        }

        if (timeoutMs != Timeout.Infinite && timeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutMs),
                timeoutMs,
                "Timeout must be positive or Timeout.Infinite."
            );
        }

        string scriptContent = mode switch
        {
            CodeExecutionMode.RawScript => code,
            CodeExecutionMode.SelfContained => WrapInEntryPoint(code),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported mode."),
        };

        var result = new CodeExecutionResult();
        var stopwatch = Stopwatch.StartNew();

        var originalOut = Console.Out;
        var outputBuilder = new StringBuilder();

        try
        {
            using var writer = new StringWriter(outputBuilder);
            Console.SetOut(writer);

            using var linkedCts = timeoutMs == Timeout.Infinite
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (timeoutMs != Timeout.Infinite)
            {
                linkedCts.CancelAfter(timeoutMs);
            }

            try
            {
                var scriptState = await CSharpScript.RunAsync(
                    scriptContent,
                    DefaultScriptOptions,
                    cancellationToken: linkedCts.Token
                );

                if (scriptState.ReturnValue is not null)
                {
                    outputBuilder.AppendLine($"Return value: {scriptState.ReturnValue}");
                }

                result.Success = true;
                result.Output = outputBuilder.ToString();
            }
            catch (CompilationErrorException ex)
            {
                result.Success = false;
                result.Error = "Compilation error:\n" + string.Join("\n", ex.Diagnostics);
            }
            catch (OperationCanceledException)
            {
                if (linkedCts.IsCancellationRequested && timeoutMs != Timeout.Infinite)
                {
                    result.Success = false;
                    result.Error = $"Execution timed out after {timeoutMs}ms.";
                }
                else
                {
                    result.Success = false;
                    result.Error = "Execution cancelled.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Runtime error: {ex.Message}\n{ex.StackTrace}";
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
        }

        return result;
    }

    private static string WrapInEntryPoint(string body)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using System.Text;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine();
        builder.AppendLine("void UserMain()");
        builder.AppendLine("{");

        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            builder.Append("    ").AppendLine(line);
        }

        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("UserMain();");

        return builder.ToString();
    }
}

/// <summary>
/// Result payload returned by the execution service.
/// </summary>
public class CodeExecutionResult
{
    public bool Success { get; set; }

    public string? Output { get; set; }

    public string? Error { get; set; }

    public long ExecutionTimeMs { get; set; }
}

/// <summary>
/// Supported execution modes for user-provided code.
/// </summary>
public enum CodeExecutionMode
{
    SelfContained,
    RawScript,
}
