using System;
using System.Net;

namespace Mcp.Net.Client.Exceptions;

/// <summary>
/// Represents an HTTP-level failure encountered by the MCP client transport.
/// Provides access to the underlying status code, response body, and associated request metadata.
/// </summary>
public sealed class McpClientHttpException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientHttpException"/> class.
    /// </summary>
    public McpClientHttpException(
        HttpStatusCode statusCode,
        string message,
        string? responseBody,
        string? contentType,
        Uri? requestUri,
        string? requestMethod
    )
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ContentType = contentType;
        RequestUri = requestUri;
        RequestMethod = requestMethod;
    }

    /// <summary>
    /// Gets the HTTP status code returned by the MCP server.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the raw response body returned by the server, when available.
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Gets the response content type when provided by the server.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// Gets the request URI that produced the error.
    /// </summary>
    public Uri? RequestUri { get; }

    /// <summary>
    /// Gets the HTTP method used for the request that produced the error.
    /// </summary>
    public string? RequestMethod { get; }
}
