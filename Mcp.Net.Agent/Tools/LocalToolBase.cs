using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Base class for local tools that want typed argument binding instead of manual dictionary parsing.
/// </summary>
public abstract class LocalToolBase<TArgs> : ILocalTool
{
    private static readonly JsonSerializerOptions BindingJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly JsonSerializerOptions _serializerOptions;

    protected LocalToolBase(
        string name,
        string description,
        JsonSerializerOptions? serializerOptions = null
    )
        : this(CreateDescriptor(name, description, serializerOptions), serializerOptions) { }

    protected LocalToolBase(
        Tool descriptor,
        JsonSerializerOptions? serializerOptions = null
    )
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _serializerOptions = serializerOptions ?? BindingJsonOptions;
    }

    public Tool Descriptor { get; }

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        TArgs arguments;
        try
        {
            arguments = invocation.BindArguments<TArgs>(_serializerOptions);
        }
        catch (Exception ex)
            when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return CreateBindingErrorResult(invocation, ex);
        }

        return await ExecuteAsync(invocation, arguments, cancellationToken);
    }

    protected abstract Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        TArgs arguments,
        CancellationToken cancellationToken = default
    );

    protected virtual ToolInvocationResult CreateBindingErrorResult(
        ToolInvocation invocation,
        Exception exception
    ) => invocation.CreateErrorResult($"Invalid tool arguments: {exception.Message}");

    private static Tool CreateDescriptor(
        string name,
        string description,
        JsonSerializerOptions? serializerOptions
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);

        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = LocalToolSchemaGenerator.GenerateJsonSchema(typeof(TArgs), serializerOptions),
        };
    }
}
