using System.Net;
using System.Net.Sockets;
using System.Reflection;
using FluentAssertions;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.ServerBuilder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Tests.Server;

public class SseServerBuilderTests
{
    [Fact]
    public async Task ConfigureWebApplication_Should_Serve_ConfiguredSsePath_InsteadOf_DefaultPath()
    {
        var port = GetFreeTcpPort();
        var options = new SseServerOptions
        {
            Name = "BuilderPathTest",
            Version = "1.0.0",
            Hostname = "127.0.0.1",
            Port = port,
            Scheme = "http",
            SsePath = "/custom-mcp",
            HealthCheckPath = "/custom-health",
            EnableCors = false,
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var serverBuilder = McpServerBuilder.ForSse(options).WithLoggerFactory(loggerFactory);
        var server = serverBuilder.Build();
        var transportBuilder = GetSseTransportBuilder(serverBuilder);

        await using var app = BuildWebApplication(transportBuilder, options.Args, server);
        app.Urls.Add($"http://127.0.0.1:{port}");

        await app.StartAsync();

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            using var customPathResponse = await client.GetAsync(
                options.SsePath,
                HttpCompletionOption.ResponseHeadersRead
            );
            customPathResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            customPathResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

            using var customHealthResponse = await client.GetAsync(options.HealthCheckPath);
            customHealthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var defaultPathResponse = await client.GetAsync("/mcp");
            defaultPathResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var defaultHealthResponse = await client.GetAsync("/health");
            defaultHealthResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ConfigureWebApplication_Should_AuthenticateHostedSseConnection_OnlyOnce()
    {
        var port = GetFreeTcpPort();
        var authHandler = new CountingSuccessAuthHandler();
        var options = new SseServerOptions
        {
            Name = "BuilderAuthTest",
            Version = "1.0.0",
            Hostname = "127.0.0.1",
            Port = port,
            Scheme = "http",
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var serverBuilder = McpServerBuilder
            .ForSse(options)
            .WithLoggerFactory(loggerFactory)
            .WithAuthentication(builder => builder.WithHandler(authHandler));
        var server = serverBuilder.Build();
        var transportBuilder = GetSseTransportBuilder(serverBuilder);

        await using var app = BuildWebApplication(transportBuilder, options.Args, server);
        app.Urls.Add($"http://127.0.0.1:{port}");

        await app.StartAsync();

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, options.SsePath);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                "test-token"
            );

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            authHandler.InvocationCount.Should().Be(1);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static SseServerBuilder GetSseTransportBuilder(McpServerBuilder serverBuilder)
    {
        var transportBuilderField = typeof(McpServerBuilder).GetField(
            "_transportBuilder",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        transportBuilderField.Should().NotBeNull();

        transportBuilderField!
            .GetValue(serverBuilder)
            .Should()
            .BeOfType<SseServerBuilder>();

        return (SseServerBuilder)transportBuilderField.GetValue(serverBuilder)!;
    }

    private static WebApplication BuildWebApplication(
        SseServerBuilder transportBuilder,
        string[] args,
        global::McpServer server
    )
    {
        var configureWebApplication = typeof(SseServerBuilder).GetMethod(
            "ConfigureWebApplication",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        configureWebApplication.Should().NotBeNull();

        var result = configureWebApplication!.Invoke(
            transportBuilder,
            new object?[] { args, server }
        );

        result.Should().NotBeNull();

        return result switch
        {
            ValueTuple<WebApplication, global::Mcp.Net.Server.ServerConfiguration> tuple => tuple.Item1,
            _ => throw new InvalidOperationException(
                $"Unexpected ConfigureWebApplication return type: {result!.GetType().FullName}"
            ),
        };
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class CountingSuccessAuthHandler : IAuthHandler
    {
        private int _invocationCount;

        public string SchemeName => "Bearer";

        public int InvocationCount => _invocationCount;

        public Task<AuthResult> AuthenticateAsync(HttpContext context)
        {
            Interlocked.Increment(ref _invocationCount);
            return Task.FromResult(
                AuthResult.Success(
                    "builder-test-user",
                    new Dictionary<string, string> { ["role"] = "tester" }
                )
            );
        }
    }
}
