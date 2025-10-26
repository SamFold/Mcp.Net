using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Mcp.Net.Core.Models.Messages;

namespace Mcp.Net.Core.Models.Completion;

/// <summary>
/// Parameters for the <c>completion/complete</c> JSON-RPC call.
/// </summary>
public sealed class CompletionCompleteParams : IMcpRequest
{
    /// <summary>
    /// The reference that describes which prompt or resource should be completed.
    /// </summary>
    [JsonPropertyName("ref")]
    public CompletionReference Reference { get; set; } = new();

    /// <summary>
    /// The argument being completed.
    /// </summary>
    [JsonPropertyName("argument")]
    public CompletionArgument Argument { get; set; } = new();

    /// <summary>
    /// Optional context that may include previously resolved arguments.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionContext? Context { get; set; }
}

/// <summary>
/// Identifies the subject of a completion request.
/// </summary>
public sealed class CompletionReference
{
    /// <summary>
    /// The reference type (e.g., <c>ref/prompt</c> or <c>ref/resource</c>).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The prompt name when <see cref="Type"/> is <c>ref/prompt</c>.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// The resource URI when <see cref="Type"/> is <c>ref/resource</c>.
    /// </summary>
    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uri { get; set; }
}

/// <summary>
/// Describes the argument the client is attempting to complete.
/// </summary>
public sealed class CompletionArgument
{
    /// <summary>
    /// The name of the argument being completed.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The current value typed by the user.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Additional data provided by the client to help produce completions.
/// </summary>
public sealed class CompletionContext
{
    /// <summary>
    /// Previously resolved argument values.
    /// </summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Arguments { get; set; }
}

/// <summary>
/// Response payload returned from <c>completion/complete</c>.
/// </summary>
public sealed class CompletionCompleteResult
{
    /// <summary>
    /// The completion payload describing suggestions and metadata.
    /// </summary>
    [JsonPropertyName("completion")]
    public CompletionValues Completion { get; set; } = new();
}

/// <summary>
/// Represents the suggested values for an argument.
/// </summary>
public sealed class CompletionValues
{
    /// <summary>
    /// Suggested values ordered by relevance.
    /// </summary>
    [JsonPropertyName("values")]
    public IReadOnlyList<string> Values { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional total count of suggestions that match the request.
    /// </summary>
    [JsonPropertyName("total")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Total { get; set; }

    /// <summary>
    /// Indicates whether additional results are available beyond those returned.
    /// </summary>
    [JsonPropertyName("hasMore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasMore { get; set; }
}
