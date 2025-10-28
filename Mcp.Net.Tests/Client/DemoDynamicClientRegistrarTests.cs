using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using DemoOAuthDefaults = Mcp.Net.Examples.Shared.DemoOAuthDefaults;
using Mcp.Net.Examples.Shared.Authorization;

namespace Mcp.Net.Tests.Client;

public class DemoDynamicClientRegistrarTests
{
    [Fact]
    public async Task RegisterAsync_EmitsWellFormedPayload()
    {
        var baseUri = new Uri("http://localhost:5000");

        var metadataResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "issuer": "http://localhost:5000/oauth",
                  "token_endpoint": "http://localhost:5000/oauth/token",
                  "authorization_endpoint": "http://localhost:5000/oauth/authorize",
                  "registration_endpoint": "http://localhost:5000/oauth/register"
                }
                """
            ),
        };

        var registrationResponse = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                """
                {
                  "client_id": "dynamic-client",
                  "redirect_uris": [ "https://example-app.local/oauth/callback" ],
                  "token_endpoint_auth_method": "none",
                  "grant_types": [ "authorization_code", "refresh_token" ],
                  "client_secret_expires_at": 0,
                  "client_id_issued_at": 1234567890
                }
                """
            ),
        };

        var handler = new RecordingHttpMessageHandler(new[] { metadataResponse, registrationResponse });
        var httpClient = new HttpClient(handler);

        var result = await DemoDynamicClientRegistrar.RegisterAsync(
            baseUri,
            "Test Client",
            DemoOAuthDefaults.DefaultRedirectUri,
            DemoOAuthDefaults.Scopes,
            httpClient,
            CancellationToken.None
        );

        result.ClientId.Should().Be("dynamic-client");
        result.ClientSecret.Should().BeNull();
        result.RedirectUris.Should().ContainSingle(uri => uri == DemoOAuthDefaults.DefaultRedirectUri);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Uri.Should().Be(DemoOAuthDefaults.BuildMetadataUri(baseUri));

        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].Uri.Should().Be(new Uri(baseUri, DemoOAuthDefaults.ClientRegistrationEndpointPath));

        var payloadJson = handler.Requests[1].Content;
        payloadJson.Should().NotBeNull();
        using var payload = JsonDocument.Parse(payloadJson!);
        var root = payload.RootElement;
        root.GetProperty("token_endpoint_auth_method").GetString().Should().Be("none");
        root.GetProperty("require_pkce").GetBoolean().Should().BeTrue();
        root.GetProperty("redirect_uris").EnumerateArray().Should().ContainSingle()
            .Which.GetString().Should().Be(DemoOAuthDefaults.DefaultRedirectUri.ToString());
    }

    [Fact]
    public async Task RegisterAsync_ThrowsWhenRegistrationEndpointMissing()
    {
        var baseUri = new Uri("http://localhost:5000");
        var metadataResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "issuer": "http://localhost:5000/oauth",
                  "token_endpoint": "http://localhost:5000/oauth/token"
                }
                """
            ),
        };

        var handler = new RecordingHttpMessageHandler(new[] { metadataResponse });
        var httpClient = new HttpClient(handler);

        Func<Task> act = () => DemoDynamicClientRegistrar.RegisterAsync(
            baseUri,
            "Test Client",
            DemoOAuthDefaults.DefaultRedirectUri,
            DemoOAuthDefaults.Scopes,
            httpClient,
            CancellationToken.None
        );

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*registration_endpoint*");
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public RecordingHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<(HttpMethod Method, Uri Uri, string? Content)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string? content = null;
            if (request.Content != null)
            {
                content = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            Requests.Add((request.Method, request.RequestUri!, content));

            if (_responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("No response configured."),
                };
            }

            return _responses.Dequeue();
        }
    }
}
