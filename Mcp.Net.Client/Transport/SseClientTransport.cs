using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Exceptions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Client.Transport;

/// <summary>
/// Client transport implementation that uses Server-Sent Events (SSE) for receiving data
/// and HTTP POST for sending data over the unified MCP endpoint.
/// </summary>
public class SseClientTransport : ClientTransportBase
{
    private static int _count = 0;
    private const string DefaultEndpointPath = "/mcp";
    private readonly HttpClient _requestHttpClient;
    private readonly HttpClient _streamHttpClient;
    private readonly bool _ownsRequestHttpClient;
    private readonly bool _ownsStreamHttpClient;
    private readonly Uri _endpointUri;
    private readonly string? _apiKey;
    private readonly OAuthTokenManager _tokenManager;

    private Task? _sseListenTask;
    private HttpResponseMessage? _sseResponse;
    private StreamReader? _sseReader;
    private string? _sessionId;
    private string? _negotiatedProtocolVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class.
    /// </summary>
    /// <param name="baseUrl">The base URL or MCP endpoint URL of the server.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="tokenProvider">Optional OAuth token provider used to acquire bearer tokens.</param>
    public SseClientTransport(
        string baseUrl,
        ILogger? logger = null,
        string? apiKey = null,
        IOAuthTokenProvider? tokenProvider = null
    )
        : this(
            CreateRequestClient(baseUrl, out var endpointUri, out var authorityUri),
            CreateStreamClient(authorityUri),
            logger,
            apiKey,
            endpointUri,
            tokenProvider,
            ownsRequestClient: true,
            ownsStreamClient: true
        ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for communication.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="tokenProvider">Optional OAuth token provider used to acquire bearer tokens.</param>
    /// <param name="streamHttpClient">
    /// Optional HTTP client dedicated to the SSE stream. When not supplied, a new client is created
    /// based on the request client's authority.
    /// </param>
    public SseClientTransport(
        HttpClient httpClient,
        ILogger? logger = null,
        string? apiKey = null,
        IOAuthTokenProvider? tokenProvider = null,
        HttpClient? streamHttpClient = null
    )
        : this(
            httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            streamHttpClient
                ?? CreateStreamClient(GetAuthorityUri(ResolveEndpointUri(httpClient?.BaseAddress))),
            logger,
            apiKey,
            ResolveEndpointUri(httpClient?.BaseAddress),
            tokenProvider,
            ownsRequestClient: false,
            ownsStreamClient: streamHttpClient == null
        ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class using dedicated clients for requests and streaming.
    /// </summary>
    /// <param name="requestHttpClient">HTTP client used for JSON-RPC POST requests.</param>
    /// <param name="streamHttpClient">HTTP client used for the SSE stream.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="tokenProvider">Optional OAuth token provider used to acquire bearer tokens.</param>
    public SseClientTransport(
        HttpClient requestHttpClient,
        HttpClient streamHttpClient,
        ILogger? logger = null,
        string? apiKey = null,
        IOAuthTokenProvider? tokenProvider = null
    )
        : this(
            requestHttpClient ?? throw new ArgumentNullException(nameof(requestHttpClient)),
            streamHttpClient ?? throw new ArgumentNullException(nameof(streamHttpClient)),
            logger,
            apiKey,
            ResolveEndpointUri(
                requestHttpClient?.BaseAddress ?? streamHttpClient?.BaseAddress
            ),
            tokenProvider,
            ownsRequestClient: false,
            ownsStreamClient: false
        ) { }

    private SseClientTransport(
        HttpClient httpClient,
        HttpClient streamHttpClient,
        ILogger? logger,
        string? apiKey,
        Uri endpointUri,
        IOAuthTokenProvider? tokenProvider,
        bool ownsRequestClient,
        bool ownsStreamClient
    )
        : base(new JsonRpcMessageParser(), logger ?? NullLogger.Instance, (Interlocked.Increment(ref _count) - 1).ToString())
    {
        _requestHttpClient = httpClient;
        _streamHttpClient = streamHttpClient;
        _endpointUri = endpointUri;
        _ownsRequestHttpClient = ownsRequestClient;
        _ownsStreamHttpClient = ownsStreamClient;
        _apiKey = apiKey;
        _tokenManager = new OAuthTokenManager(tokenProvider, logger);

        EnsureClientHeaders(_requestHttpClient, endpointUri);
        EnsureClientHeaders(_streamHttpClient, endpointUri);

        if (!string.IsNullOrEmpty(_apiKey))
        {
            AddApiKeyHeader(_requestHttpClient, _apiKey);
            AddApiKeyHeader(_streamHttpClient, _apiKey);
        }
    }

    private static void EnsureClientHeaders(HttpClient client, Uri endpointUri)
    {
        if (client.BaseAddress == null)
        {
            client.BaseAddress = new Uri(endpointUri.GetLeftPart(UriPartial.Authority));
        }

        if (client.Timeout != Timeout.InfiniteTimeSpan)
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }
    }

    /// <summary>
    /// Gets the current session identifier once established.
    /// </summary>
    internal string? SessionId => _sessionId;

    /// <inheritdoc />
    public override async Task StartAsync()
    {
        if (IsStarted)
        {
            throw new InvalidOperationException("Transport already started");
        }

        Logger.LogDebug("Opening SSE stream at {Endpoint}", _endpointUri);

        var response = await OpenSseStreamAsync();
        Logger.LogInformation(
            "SSE stream HTTP status: {StatusCode}",
            response.StatusCode
        );

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(CancellationTokenSource.Token);
            throw new HttpRequestException(
                $"Failed to open SSE stream: {(int)response.StatusCode} {response.StatusCode}. {body}"
            );
        }

        _sessionId = ExtractHeader(response.Headers, "Mcp-Session-Id");
        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            Logger.LogError(
                "SSE stream established but server did not provide Mcp-Session-Id header. Headers: {Headers}",
                string.Join(
                    "; ",
                    response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")
                )
            );
            response.Dispose();
            throw new InvalidOperationException("Server did not return a session identifier.");
        }

        Logger.LogInformation("SSE session established with ID {SessionId}", _sessionId);
        _sseResponse = response;
        _sseReader = new StreamReader(
            await response.Content.ReadAsStreamAsync(CancellationTokenSource.Token),
            Encoding.UTF8
        );
        Logger.LogDebug("SSE response stream acquired; launching background listener.");
        _sseListenTask = Task.Run(() => ListenToServerEventsAsync(), CancellationTokenSource.Token);
        IsStarted = true;
        Logger.LogInformation("SSE transport started with session {SessionId}", _sessionId);
    }

    /// <inheritdoc />
    public override async Task<object> SendRequestAsync(string method, object? parameters = null)
    {
        EnsureActiveSession();

        Logger.LogInformation("Preparing to send JSON-RPC request '{Method}'", method);
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        PendingRequests[id] = tcs;

        var requestMessage = new JsonRpcRequestMessage("2.0", id, method, parameters);
        try
        {
            await SendJsonRpcPayloadAsync(requestMessage);

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), CancellationTokenSource.Token);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                PendingRequests.TryRemove(id, out _);
                throw new TimeoutException($"Request timed out: {method}");
            }

            return await tcs.Task;
        }
        catch
        {
            PendingRequests.TryRemove(id, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task SendNotificationAsync(string method, object? parameters = null)
    {
        EnsureActiveSession();

        Logger.LogDebug("Sending JSON-RPC notification '{Method}'", method);
        var notification = new JsonRpcNotificationMessage(
            "2.0",
            method,
            parameters != null ? JsonSerializer.SerializeToElement(parameters) : null
        );

        await SendJsonRpcPayloadAsync(notification);
    }

    /// <inheritdoc />
    public override async Task SendResponseAsync(JsonRpcResponseMessage message)
    {
        EnsureActiveSession();

        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        Logger.LogDebug(
            "Sending JSON-RPC response for request {Id}",
            message.Id
        );

        await SendJsonRpcPayloadAsync(message);
    }

    private async Task SendJsonRpcPayloadAsync(object payload)
    {
        var payloadJson = SerializeMessage(payload);

        if (payload is JsonRpcRequestMessage requestMessage)
        {
            Logger.LogDebug(
                "Sending JSON-RPC request: {Method} (Id: {Id})",
                requestMessage.Method,
                requestMessage.Id
            );
        }
        else if (payload is JsonRpcResponseMessage responseMessage)
        {
            Logger.LogDebug(
                "Sending JSON-RPC response: Id {Id}, HasError: {HasError}",
                responseMessage.Id,
                responseMessage.Error != null
            );
        }
        else if (payload is JsonRpcNotificationMessage notificationMessage)
        {
            Logger.LogDebug("Sending JSON-RPC notification: {Method}", notificationMessage.Method);
        }
        else
        {
            Logger.LogDebug("Sending JSON-RPC payload of type {Type}", payload.GetType().Name);
        }

        var response = await SendWithAuthenticationAsync(
            () =>
            {
                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, _endpointUri)
                {
                    Content = content,
                };

                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                if (!string.IsNullOrWhiteSpace(_sessionId))
                {
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
                }

                if (!string.IsNullOrWhiteSpace(_negotiatedProtocolVersion))
                {
                    request.Headers.TryAddWithoutValidation(
                        "MCP-Protocol-Version",
                        _negotiatedProtocolVersion
                    );
                }

                return request;
            }
        );

        await HandleHttpResponseAsync(response);
        Logger.LogInformation("JSON-RPC payload delivered successfully.");
    }

    private async Task HandleHttpResponseAsync(HttpResponseMessage response)
    {
        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var exception = await CreateHttpExceptionAsync(response);
                Logger.LogWarning(
                    "HTTP request to {Uri} failed with {StatusCode}. Response body: {Body}",
                    response.RequestMessage?.RequestUri,
                    response.StatusCode,
                    exception.ResponseBody
                );
                throw exception;
            }

            var sessionHeader = ExtractHeader(response.Headers, "Mcp-Session-Id");
            if (!string.IsNullOrWhiteSpace(sessionHeader))
            {
                _sessionId = sessionHeader;
            }
            Logger.LogDebug(
                "Response headers confirmed session {SessionId} and protocol {Protocol}",
                _sessionId,
                _negotiatedProtocolVersion ?? "<not set>"
            );

            var protocolHeader = ExtractHeader(response.Headers, "MCP-Protocol-Version");
            if (!string.IsNullOrWhiteSpace(protocolHeader))
            {
                _negotiatedProtocolVersion = protocolHeader;
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> OpenSseStreamAsync()
    {
        const int maxAttempts = 3;
        McpClientHttpException? lastUnauthorized = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpointUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            await AttachAuthorizationAsync(request);

            Logger.LogDebug(
                "Opening SSE stream attempt {Attempt} to {Uri}",
                attempt + 1,
                request.RequestUri
            );
            var response = await _streamHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                CancellationTokenSource.Token
            );

            Logger.LogInformation(
                "SSE GET {Uri} returned {StatusCode}",
                response.RequestMessage?.RequestUri,
                response.StatusCode
            );

            if (response.IsSuccessStatusCode)
            {
                Logger.LogDebug(
                    "SSE response headers: {Headers}",
                    string.Join(
                        "; ",
                        response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")
                    )
                );
            }

            if (
                response.StatusCode == HttpStatusCode.Unauthorized
                && await TryHandleUnauthorizedAsync(response)
                && attempt < maxAttempts - 1
            )
            {
                Logger.LogWarning("SSE stream unauthorized. Retrying after acquiring token.");
                response.Dispose();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                lastUnauthorized = await CreateHttpExceptionAsync(response);
                response.Dispose();
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var exception = await CreateHttpExceptionAsync(response);
                response.Dispose();
                throw exception;
            }

            Logger.LogInformation(
                "SSE GET handshake succeeded with status {StatusCode}",
                response.StatusCode
            );
            return response;
        }

        throw lastUnauthorized
            ?? new McpClientHttpException(
                HttpStatusCode.Unauthorized,
                "Unable to satisfy authorization challenge for SSE stream.",
                null,
                null,
                _endpointUri,
                HttpMethod.Get.Method
            );
    }

    private async Task<HttpResponseMessage> SendWithAuthenticationAsync(
        Func<HttpRequestMessage> requestFactory
    )
    {
        const int maxAttempts = 3;
        McpClientHttpException? lastUnauthorized = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var request = requestFactory();
            await AttachAuthorizationAsync(request);

            Logger.LogInformation(
                "Sending HTTP {Method} request to {Uri} (attempt {Attempt})",
                request.Method,
                request.RequestUri,
                attempt + 1
            );
            var response = await _requestHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                CancellationTokenSource.Token
            );

            Logger.LogInformation(
                "Received HTTP {StatusCode} for {Method} {Uri}",
                response.StatusCode,
                response.RequestMessage?.Method,
                response.RequestMessage?.RequestUri
            );

            if (
                response.StatusCode == HttpStatusCode.Unauthorized
                && await TryHandleUnauthorizedAsync(response)
                && attempt < maxAttempts - 1
            )
            {
                Logger.LogWarning(
                    "Request to {Uri} unauthorized; attempting token acquisition and retry.",
                    response.RequestMessage?.RequestUri
                );
                response.Dispose();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                lastUnauthorized = await CreateHttpExceptionAsync(response);
                response.Dispose();
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var exception = await CreateHttpExceptionAsync(response);
                response.Dispose();
                throw exception;
            }

            return response;
        }

        throw lastUnauthorized
            ?? new McpClientHttpException(
                HttpStatusCode.Unauthorized,
                "Unable to satisfy authorization challenge for request.",
                null,
                null,
                _endpointUri,
                null
            );
    }

    private async Task AttachAuthorizationAsync(HttpRequestMessage request)
    {
        var token = await _tokenManager.GetAccessTokenAsync(
            _endpointUri,
            CancellationTokenSource.Token
        );

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Logger.LogTrace(
                "Attached bearer token to request {Method} {Uri}",
                request.Method,
                request.RequestUri
            );
        }
        else
        {
            Logger.LogTrace(
                "No bearer token available for request {Method} {Uri}",
                request.Method,
                request.RequestUri
            );
        }
    }

    private async Task<bool> TryHandleUnauthorizedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return false;
        }

        var challenge = OAuthChallengeParser.Parse(
            response.Headers.WwwAuthenticate.Select(v => v.ToString())
        );
        if (challenge == null)
        {
            Logger.LogWarning("Received 401 without parsable WWW-Authenticate challenge.");
            return false;
        }

        Logger.LogInformation(
            "Processing OAuth challenge (scheme={Scheme}, resource_metadata={Resource})",
            challenge.Scheme,
            challenge.ResourceMetadata
        );
        return await _tokenManager.HandleUnauthorizedAsync(
            _endpointUri,
            challenge,
            CancellationTokenSource.Token
        );
    }

    private async Task ListenToServerEventsAsync()
    {
        if (_sseReader == null)
        {
            return;
        }

        try
        {
            Logger.LogDebug("Listening for SSE events...");
            var dataBuilder = new StringBuilder();
            while (!CancellationTokenSource.IsCancellationRequested)
            {
                var line = await _sseReader.ReadLineAsync();
                if (line == null)
                {
                    Logger.LogInformation("SSE stream ended by remote host.");
                    break;
                }

                Logger.LogTrace("SSE line received: {Line}", line);

                if (line.Length == 0)
                {
                    if (dataBuilder.Length > 0)
                    {
                        var payload = dataBuilder.ToString();
                        Logger.LogTrace("Processing SSE payload: {Payload}", payload);
                        ProcessJsonRpcMessage(payload);
                        dataBuilder.Clear();
                    }

                    continue;
                }

                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    if (dataBuilder.Length > 0)
                    {
                        dataBuilder.Append('\n');
                    }

                    dataBuilder.Append(line.Substring(6));
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("SSE listen cancelled");
        }
        catch (IOException ex) when (IsCancellationException(ex))
        {
            Logger.LogDebug("SSE listen closed due to transport shutdown.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SSE connection error");
            RaiseOnError(ex);
        }
        finally
        {
            Logger.LogInformation("SSE listen task terminated");
        }
    }

    /// <inheritdoc />
    protected override async Task OnClosingAsync()
    {
        await base.OnClosingAsync();

        try
        {
            if (_sseReader != null)
            {
                _sseReader.Dispose();
                _sseReader = null;
            }

            _sseResponse?.Dispose();
            _sseResponse = null;

            if (_sseListenTask != null)
            {
                try
                {
                    await _sseListenTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
            }
        }
        finally
        {
            if (_ownsStreamHttpClient)
            {
                _streamHttpClient.Dispose();
            }

            if (_ownsRequestHttpClient)
            {
                _requestHttpClient.Dispose();
            }

            _sessionId = null;
            _negotiatedProtocolVersion = null;
        }
    }

    private void EnsureActiveSession()
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("Transport is closed");
        }

        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            throw new InvalidOperationException("SSE session has not been established.");
        }
    }

    private static string? ExtractHeader(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static HttpClient CreateRequestClient(string baseUrl, out Uri endpointUri, out Uri authorityUri)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or empty.", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("Base URL must be an absolute URI.", nameof(baseUrl));
        }

        endpointUri = ResolveEndpointUri(baseUri);
        authorityUri = GetAuthorityUri(endpointUri);
        return new HttpClient { BaseAddress = authorityUri };
    }

    private static HttpClient CreateStreamClient(Uri authorityUri)
    {
        return new HttpClient { BaseAddress = authorityUri };
    }

    private static Uri GetAuthorityUri(Uri endpointUri) =>
        new(endpointUri.GetLeftPart(UriPartial.Authority));

    private static void AddApiKeyHeader(HttpClient client, string apiKey)
    {
        if (!client.DefaultRequestHeaders.Contains("X-API-Key"))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
    }

    private static Uri ResolveEndpointUri(Uri? uri)
    {
        if (uri == null)
        {
            throw new InvalidOperationException("HttpClient must have a BaseAddress configured.");
        }

        if (string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            return new Uri(uri, DefaultEndpointPath);
        }

        return uri;
    }

    private async Task<McpClientHttpException> CreateHttpExceptionAsync(HttpResponseMessage response)
    {
        var statusCode = response.StatusCode;
        var contentType = response.Content?.Headers?.ContentType?.MediaType;
        string? body = null;

        if (response.Content != null)
        {
            try
            {
                body = await response.Content.ReadAsStringAsync(CancellationTokenSource.Token);
            }
            catch (Exception readEx)
            {
                Logger.LogDebug(
                    readEx,
                    "Failed to read response body for {Method} {Uri}.",
                    response.RequestMessage?.Method,
                    response.RequestMessage?.RequestUri
                );
            }
        }

        var summary = SummarizeError(body, contentType);
        var requestUri = response.RequestMessage?.RequestUri;
        var method = response.RequestMessage?.Method?.Method;
        var message = summary != null
            ? $"{(int)statusCode} {statusCode} returned by {method} {requestUri}: {summary}"
            : $"{(int)statusCode} {statusCode} returned by {method} {requestUri}";

        return new McpClientHttpException(
            statusCode,
            message,
            body,
            contentType,
            requestUri,
            method
        );
    }

    private static string? SummarizeError(string? body, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.Object)
                    {
                        if (
                            errorElement.TryGetProperty("message", out var messageProperty)
                            && messageProperty.ValueKind == JsonValueKind.String
                        )
                        {
                            return messageProperty.GetString();
                        }

                        if (
                            errorElement.TryGetProperty("error_description", out var descriptionProperty)
                            && descriptionProperty.ValueKind == JsonValueKind.String
                        )
                        {
                            return descriptionProperty.GetString();
                        }
                    }
                    else if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        return errorElement.GetString();
                    }
                }

                if (
                    doc.RootElement.TryGetProperty("message", out var messageElement)
                    && messageElement.ValueKind == JsonValueKind.String
                )
                {
                    return messageElement.GetString();
                }
            }
            catch (JsonException)
            {
                // Ignore malformed JSON and fall back to plain text summary.
            }
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 256 ? trimmed : $"{trimmed[..256]}…";
    }

    private static bool IsCancellationException(IOException ex)
    {
        if (ex.InnerException is SocketException socketException)
        {
            return socketException.SocketErrorCode == SocketError.OperationAborted
                || socketException.SocketErrorCode == SocketError.Interrupted
                || socketException.SocketErrorCode == SocketError.TimedOut;
        }

        return false;
    }
}
