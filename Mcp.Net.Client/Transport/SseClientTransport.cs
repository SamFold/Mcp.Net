using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    private const string DefaultEndpointPath = "/mcp";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _endpointUri;
    private readonly string? _apiKey;

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
    public SseClientTransport(string baseUrl, ILogger? logger = null, string? apiKey = null)
        : this(
            CreateHttpClient(baseUrl, out var endpointUri),
            logger,
            apiKey,
            endpointUri,
            ownsClient: true
        ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for communication.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    public SseClientTransport(HttpClient httpClient, ILogger? logger = null, string? apiKey = null)
        : this(
            httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            logger,
            apiKey,
            ResolveEndpointUri(httpClient.BaseAddress),
            ownsClient: false
        ) { }

    private SseClientTransport(
        HttpClient httpClient,
        ILogger? logger,
        string? apiKey,
        Uri endpointUri,
        bool ownsClient
    )
        : base(new JsonRpcMessageParser(), logger ?? NullLogger.Instance)
    {
        _httpClient = httpClient;
        _endpointUri = endpointUri;
        _ownsHttpClient = ownsClient;
        _apiKey = apiKey;

        if (
            !string.IsNullOrEmpty(_apiKey)
            && !_httpClient.DefaultRequestHeaders.Contains("X-API-Key")
        )
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
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

        var request = new HttpRequestMessage(HttpMethod.Get, _endpointUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationTokenSource.Token
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
            response.Dispose();
            throw new InvalidOperationException("Server did not return a session identifier.");
        }

        _sseResponse = response;
        _sseReader = new StreamReader(
            await response.Content.ReadAsStreamAsync(CancellationTokenSource.Token),
            Encoding.UTF8
        );

        _sseListenTask = Task.Run(() => ListenToServerEventsAsync(), CancellationTokenSource.Token);
        IsStarted = true;
        Logger.LogInformation("SSE transport started with session {SessionId}", _sessionId);
    }

    /// <inheritdoc />
    public override async Task<object> SendRequestAsync(string method, object? parameters = null)
    {
        EnsureActiveSession();

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

        var notification = new JsonRpcNotificationMessage(
            "2.0",
            method,
            parameters != null ? JsonSerializer.SerializeToElement(parameters) : null
        );

        await SendJsonRpcPayloadAsync(notification);
    }

    private async Task SendJsonRpcPayloadAsync(object payload)
    {
        var payloadJson = SerializeMessage(payload);
        using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpointUri)
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

        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationTokenSource.Token
        );

        await HandleHttpResponseAsync(response);
    }

    private async Task HandleHttpResponseAsync(HttpResponseMessage response)
    {
        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(
                    CancellationTokenSource.Token
                );
                throw new HttpRequestException(
                    $"HTTP error: {(int)response.StatusCode} {response.StatusCode} - {errorBody}"
                );
            }

            var sessionHeader = ExtractHeader(response.Headers, "Mcp-Session-Id");
            if (!string.IsNullOrWhiteSpace(sessionHeader))
            {
                _sessionId = sessionHeader;
            }

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
                    break;
                }

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
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
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

    private static HttpClient CreateHttpClient(string baseUrl, out Uri endpointUri)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or empty.", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Base URL must be an absolute URI.", nameof(baseUrl));
        }

        endpointUri = ResolveEndpointUri(uri);
        return new HttpClient();
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
}
