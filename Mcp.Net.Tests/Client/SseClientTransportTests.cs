using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO.Pipelines;
using FluentAssertions;
using Mcp.Net.Client;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.Client;

public class SseClientTransportTests
{
    [Fact]
    public async Task StartAsync_ShouldOpenSseStreamAndCaptureSession()
    {
        var sseStream = new TestSseStream();
        var streamHandler = new TestMessageHandler();
        var requestHandler = new TestMessageHandler();

        streamHandler.EnqueueResponse(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri.Should().Be(new Uri("http://localhost:5000/mcp"));
            request.Headers.Accept.Should().ContainSingle(h => h.MediaType == "text/event-stream");

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-123");
            response.Content = new StreamContent(sseStream);
            return response;
        });

        using var streamClient = new HttpClient(streamHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        using var requestClient = new HttpClient(requestHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(requestClient, streamClient, NullLogger.Instance);

        await transport.StartAsync();

        streamHandler.RequestCount.Should().Be(1);

        await sseStream.CompleteAsync();
        await transport.CloseAsync();
    }

    [Fact]
    public async Task SendRequestAsync_ShouldIncludeSessionAndNegotiatedHeaders()
    {
        var sseStream = new TestSseStream();
        var streamHandler = new TestMessageHandler();
        var requestHandler = new TestMessageHandler();

        streamHandler.EnqueueResponse(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-456");
            response.Content = new StreamContent(sseStream);
            return response;
        });

        string? initializeRequestId = null;

        requestHandler.EnqueueResponse(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.Headers.TryGetValues("Mcp-Session-Id", out var sessionHeaders)
                .Should()
                .BeTrue();
            sessionHeaders!.Should().Contain("session-456");
            var acceptMediaTypes = request.Headers.Accept.Select(a => a.MediaType).ToArray();
            acceptMediaTypes.Should().Contain(new[] { "application/json", "text/event-stream" });

            var body = request.Content!.ReadAsStringAsync().Result;
            using var doc = JsonDocument.Parse(body);
            initializeRequestId = doc.RootElement.GetProperty("id").GetString();

            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-456");
            response.Headers.TryAddWithoutValidation("MCP-Protocol-Version", McpClient.LatestProtocolVersion);
            response.Content = new StringContent(string.Empty);
            return response;
        });

        requestHandler.EnqueueResponse(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.Headers.TryGetValues("MCP-Protocol-Version", out var protocolHeaders)
                .Should()
                .BeTrue();
            protocolHeaders!.Should().Contain(McpClient.LatestProtocolVersion);

            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-456");
            response.Headers.TryAddWithoutValidation("MCP-Protocol-Version", McpClient.LatestProtocolVersion);
            response.Content = new StringContent(string.Empty);
            return response;
        });

        using var streamClient = new HttpClient(streamHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        using var requestClient = new HttpClient(requestHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(requestClient, streamClient, NullLogger.Instance);

        await transport.StartAsync();

        var initializeTask = transport.SendRequestAsync("initialize", new { });

        initializeRequestId.Should().NotBeNull();

        var initializeResponse = new
        {
            jsonrpc = "2.0",
            id = initializeRequestId,
            result = new
            {
                protocolVersion = McpClient.LatestProtocolVersion,
                capabilities = new { },
                serverInfo = new { name = "server", version = "1.0.0" },
            },
        };
        var ssePayload = $"data: {JsonSerializer.Serialize(initializeResponse)}\n\n";
        await sseStream.WriteEventAsync(ssePayload);

        await initializeTask;

        await transport.SendNotificationAsync("notifications/initialized");

        await sseStream.CompleteAsync();
        await transport.CloseAsync();
    }

    [Fact]
    public async Task StartAsync_ShouldRetryWithOAuthTokenOnUnauthorized()
    {
        var sseStream = new TestSseStream();
        var handler = new TestMessageHandler();

        handler.EnqueueResponse(request =>
        {
            request.Headers.Authorization.Should().BeNull();
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.ParseAdd(
                "Bearer resource_metadata=\"https://auth.example.com/.well-known/oauth-protected-resource\""
            );
            response.Content = new StringContent(string.Empty);
            return response;
        });

        handler.EnqueueResponse(request =>
        {
            request.Headers.Authorization.Should().NotBeNull();
            request.Headers.Authorization!.Parameter.Should().Be("token-123");
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-789");
            response.Content = new StreamContent(sseStream);
            return response;
        });

        var tokenProvider = new TestTokenProvider("token-123");
        using var streamClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var requestClient = new HttpClient(new TestMessageHandler())
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(
            requestClient,
            streamClient,
            NullLogger.Instance,
            null,
            tokenProvider
        );

        await transport.StartAsync();
        tokenProvider.AcquireCount.Should().Be(1);

        await sseStream.CompleteAsync();
        await transport.CloseAsync();
    }

    [Fact]
    public async Task SendRequestAsync_ShouldRetryWithOAuthTokenOnUnauthorized()
    {
        var sseStream = new TestSseStream();
        var streamHandler = new TestMessageHandler();
        var requestHandler = new TestMessageHandler();
        string? initializeRequestId = null;

        streamHandler.EnqueueResponse(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-abc");
            response.Content = new StreamContent(sseStream);
            return response;
        });

        requestHandler.EnqueueResponse(request =>
        {
            request.Headers.Authorization.Should().BeNull();
            var body = request.Content!.ReadAsStringAsync().Result;
            using var doc = JsonDocument.Parse(body);
            initializeRequestId = doc.RootElement.GetProperty("id").GetString();
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.ParseAdd(
                "Bearer resource_metadata=\"https://auth.example.com/.well-known/oauth-protected-resource\""
            );
            response.Content = new StringContent(string.Empty);
            return response;
        });

        requestHandler.EnqueueResponse(request =>
        {
            request.Headers.Authorization.Should().NotBeNull();
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("token-post");
            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-abc");
            response.Headers.TryAddWithoutValidation("MCP-Protocol-Version", McpClient.LatestProtocolVersion);
            response.Content = new StringContent(string.Empty);
            return response;
        });

        var tokenProvider = new TestTokenProvider("token-post");
        using var streamClient = new HttpClient(streamHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        using var requestClient = new HttpClient(requestHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(
            requestClient,
            streamClient,
            NullLogger.Instance,
            null,
            tokenProvider
        );

        await transport.StartAsync();

        var initializeTask = transport.SendRequestAsync("initialize", new { });

        tokenProvider.AcquireCount.Should().Be(1);
        initializeRequestId.Should().NotBeNull();

        var initializeResponse = new
        {
            jsonrpc = "2.0",
            id = initializeRequestId,
            result = new
            {
                protocolVersion = McpClient.LatestProtocolVersion,
                capabilities = new { },
                serverInfo = new { name = "server", version = "1.0.0" },
            },
        };

        await sseStream.WriteEventAsync(
            $"data: {JsonSerializer.Serialize(initializeResponse)}\n\n"
        );

        await initializeTask;

        await sseStream.CompleteAsync();
        await transport.CloseAsync();
    }

    [Fact]
    public async Task StartAsync_ShouldThrowWhenUnauthorizedWithoutChallenge()
    {
        var handler = new TestMessageHandler();
        handler.EnqueueResponse(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(string.Empty),
            };
            return response;
        });

        using var streamClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        using var requestClient = new HttpClient(new TestMessageHandler())
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(requestClient, streamClient, NullLogger.Instance);

        await FluentActions.Invoking(() => transport.StartAsync())
            .Should()
            .ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendRequestAsync_ShouldSurfaceForbiddenResponse()
    {
        var sseStream = new TestSseStream();
        var streamHandler = new TestMessageHandler();
        var requestHandler = new TestMessageHandler();

        streamHandler.EnqueueResponse(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session" );
            response.Content = new StreamContent(sseStream);
            return response;
        });

        requestHandler.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("forbidden")
        });

        using var streamClient = new HttpClient(streamHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        using var requestClient = new HttpClient(requestHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(requestClient, streamClient, NullLogger.Instance);
        await transport.StartAsync();

        await FluentActions.Invoking(() => transport.SendRequestAsync("tools/list", new { }))
            .Should()
            .ThrowAsync<HttpRequestException>()
            .WithMessage("*403*");
    }

    [Fact]
    public async Task SendRequestAsync_ShouldSurfaceBadRequestResponse()
    {
        var sseStream = new TestSseStream();
        var streamHandler = new TestMessageHandler();
        var requestHandler = new TestMessageHandler();

        streamHandler.EnqueueResponse(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session" );
            response.Content = new StreamContent(sseStream);
            return response;
        });

        requestHandler.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request")
        });

        using var streamClient = new HttpClient(streamHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        using var requestClient = new HttpClient(requestHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(requestClient, streamClient, NullLogger.Instance);
        await transport.StartAsync();

        await FluentActions.Invoking(() => transport.SendRequestAsync("tools/list", new { }))
            .Should()
            .ThrowAsync<HttpRequestException>()
            .WithMessage("*400*");
    }

    [Fact]
    public async Task SendRequestAsync_ShouldSurfaceTokenProviderFailures()
    {
        var sseStream = new TestSseStream();
        var streamHandler = new TestMessageHandler();
        var requestHandler = new TestMessageHandler();

        streamHandler.EnqueueResponse(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session" );
            response.Content = new StreamContent(sseStream);
            return response;
        });

        requestHandler.EnqueueResponse(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized"),
            };
            response.Headers.WwwAuthenticate.ParseAdd("Bearer");
            return response;
        });

        using var streamClient = new HttpClient(streamHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        using var requestClient = new HttpClient(requestHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
        };
        var transport = new SseClientTransport(
            requestClient,
            streamClient,
            NullLogger.Instance,
            null,
            new FailingTokenProvider()
        );
        await transport.StartAsync();

        await FluentActions.Invoking(() => transport.SendRequestAsync("tools/list", new { }))
            .Should()
            .ThrowAsync<HttpRequestException>();
    }

    private sealed class TestMessageHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public int RequestCount { get; private set; }

        public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responses.Enqueue(responder);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            if (!_responses.TryDequeue(out var responder))
            {
                throw new InvalidOperationException("No HTTP response configured for test.");
            }

            return Task.FromResult(responder(request));
        }
    }

    private sealed class TestSseStream : Stream
    {
        private readonly Pipe _pipe = new();
        private bool _completed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> destination,
            CancellationToken cancellationToken = default
        )
        {
            while (true)
            {
                var result = await _pipe.Reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;
                if (buffer.Length == 0)
                {
                    if (result.IsCompleted)
                    {
                        return 0;
                    }

                    _pipe.Reader.AdvanceTo(buffer.Start, buffer.End);
                    continue;
                }

                var toCopy = (int)Math.Min(destination.Length, buffer.Length);
                buffer.Slice(0, toCopy).CopyTo(destination.Span);
                _pipe.Reader.AdvanceTo(buffer.GetPosition(toCopy));
                return toCopy;
            }
        }

        public async Task WriteEventAsync(string data, CancellationToken cancellationToken = default)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Stream is already completed.");
            }

            await _pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(data), cancellationToken);
            await _pipe.Writer.FlushAsync(cancellationToken);
        }

        public async Task CompleteAsync()
        {
            if (!_completed)
            {
                await _pipe.Writer.CompleteAsync();
                _completed = true;
            }
        }

        public override ValueTask DisposeAsync()
        {
            return new ValueTask(CompleteAsync());
        }

        protected override void Dispose(bool disposing)
        {
            _ = CompleteAsync();
            base.Dispose(disposing);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class TestTokenProvider : IOAuthTokenProvider
    {
        private readonly string _token;

        public TestTokenProvider(string token)
        {
            _token = token;
        }

        public int AcquireCount { get; private set; }

        public Task<OAuthTokenResponse?> AcquireTokenAsync(
            OAuthTokenRequestContext context,
            CancellationToken cancellationToken
        )
        {
            AcquireCount++;
            return Task.FromResult<OAuthTokenResponse?>(
                new OAuthTokenResponse(_token, DateTimeOffset.UtcNow.AddMinutes(30))
            );
        }

        public Task<OAuthTokenResponse?> RefreshTokenAsync(
            OAuthTokenRequestContext context,
            OAuthTokenResponse currentToken,
            CancellationToken cancellationToken
        ) => Task.FromResult<OAuthTokenResponse?>(null);
    }

    private sealed class FailingTokenProvider : IOAuthTokenProvider
    {
        public Task<OAuthTokenResponse?> AcquireTokenAsync(
            OAuthTokenRequestContext context,
            CancellationToken cancellationToken
        ) => Task.FromResult<OAuthTokenResponse?>(null);

        public Task<OAuthTokenResponse?> RefreshTokenAsync(
            OAuthTokenRequestContext context,
            OAuthTokenResponse currentToken,
            CancellationToken cancellationToken
        ) => Task.FromResult<OAuthTokenResponse?>(null);
    }
}
