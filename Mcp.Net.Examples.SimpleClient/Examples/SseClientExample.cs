using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Examples.Shared;
using Mcp.Net.Examples.Shared.Authorization;
using Mcp.Net.Examples.SimpleClient.Elicitation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Mcp.Net.Core.Models.Completion;

namespace Mcp.Net.Examples.SimpleClient.Examples;

public class SseClientExample
{
    public static async Task Run(ClientOptions options)
    {
        if (string.IsNullOrEmpty(options.ServerUrl))
        {
            Console.WriteLine("Error: Server URL is required. Use --url parameter.");
            return;
        }

        Console.WriteLine($"Connecting to server at {options.ServerUrl}");

        var serverUri = new Uri(options.ServerUrl);
        var baseUriBuilder = new UriBuilder(serverUri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        var baseUri = baseUriBuilder.Uri;

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                })
                .SetMinimumLevel(LogLevel.Debug);
        });

        var clientBuilder = new McpClientBuilder()
            .WithName("SimpleClientExample")
            .WithVersion("1.0.0")
            .WithTitle("Simple Client Example")
            .WithLogger(loggerFactory.CreateLogger("SimpleClient"))
            .UseSseTransport(options.ServerUrl)
            .WithElicitationHandler(ConsoleElicitationHandler.HandleAsync);

        HttpClient? pkceProviderHttpClient = null;
        HttpClient? pkceInteractionHttpClient = null;

        switch (options.AuthMode)
        {
            case AuthMode.ClientCredentials:
                Console.WriteLine("Using demo OAuth client credentials for authentication.");
                clientBuilder.WithClientCredentialsAuth(
                    DemoOAuthDefaults.CreateClientOptions(baseUri),
                    new HttpClient()
                );
                break;

            case AuthMode.AuthorizationCodePkce:
            {
                Console.WriteLine("Using demo OAuth authorization code flow with PKCE.");
                var pkceOptions = DemoOAuthDefaults.CreateClientOptions(baseUri);
                var redirectUri = DemoOAuthDefaults.DefaultRedirectUri;
                pkceOptions.RedirectUri = redirectUri;

                pkceProviderHttpClient = new HttpClient();
                pkceInteractionHttpClient = new HttpClient(
                    new HttpClientHandler { AllowAutoRedirect = false }
                );

                var registration = await DemoDynamicClientRegistrar.RegisterAsync(
                    baseUri,
                    clientName: "Simple Client PKCE Sample",
                    redirectUri,
                    DemoOAuthDefaults.Scopes,
                    pkceProviderHttpClient,
                    CancellationToken.None
                );

                pkceOptions.ClientId = registration.ClientId;
                pkceOptions.ClientSecret = registration.ClientSecret;
                pkceOptions.RedirectUri = registration.RedirectUris[0];

                Console.WriteLine($"Dynamic client registered with ID {registration.ClientId}.");

                clientBuilder.WithAuthorizationCodeAuth(
                    pkceOptions,
                    CreatePkceInteractionHandler(pkceInteractionHttpClient),
                    pkceProviderHttpClient
                );
                break;
            }

            case AuthMode.None:
                Console.WriteLine("Authentication disabled; requests will be sent without bearer tokens.");
                break;
        }

        using IMcpClient client = clientBuilder.Build();

        try
        {
            // Subscribe to events
            client.OnResponse += response => Console.WriteLine($"Received response: {response.Id}");
            client.OnError += error => Console.WriteLine($"Error: {error.Message}");
            client.OnClose += () => Console.WriteLine("Connection closed");

            // Initialize the client
            Console.WriteLine("Initializing client...");
            await client.Initialize();
            Console.WriteLine("Client initialized");

            await DisplayServerMetadataAsync(client);
            await InspectResourcesAsync(client);
            await InspectPromptsAsync(client);
            await DemonstrateCompletionsAsync(client);

            // List available tools
            var tools = await client.ListTools();
            Console.WriteLine($"\nAvailable tools ({tools.Length}):");
            foreach (var tool in tools)
            {
                Console.WriteLine($"- {tool.Name}: {tool.Description}");
            }

            // Demonstrate Calculator Tools
            await DemonstrateCalculatorTools(client);

            // Demonstrate Warhammer 40k Tools
            await DemonstrateWarhammer40kTools(client);
        }
        catch (HttpRequestException ex)
            when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            Console.WriteLine($"Error: Could not connect to the server at {options.ServerUrl}");
            Console.WriteLine($"Make sure the server is running. You can start it with:");
            Console.WriteLine($"  dotnet run --project ../Mcp.Net.Examples.SimpleServer");
            Console.WriteLine($"\nTechnical details: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            pkceProviderHttpClient?.Dispose();
            pkceInteractionHttpClient?.Dispose();
        }
    }

    public static async Task DemonstrateCalculatorTools(IMcpClient client)
    {
        Console.WriteLine("\n=== Calculator Tools ===");

        try
        {
            // Addition
            Console.WriteLine("\nCalling calculator.add with 5 and 3:");
            var addResult = await client.CallTool("calculator_add", new { a = 5, b = 3 });
            DisplayToolResponse(addResult);

            // Subtraction
            Console.WriteLine("\nCalling calculator_subtract with 10 and 4:");
            var subtractResult = await client.CallTool(
                "calculator_subtract",
                new { a = 10, b = 4 }
            );
            DisplayToolResponse(subtractResult);

            // Multiplication
            Console.WriteLine("\nCalling calculator_multiply with 6 and 7:");
            var multiplyResult = await client.CallTool("calculator_multiply", new { a = 6, b = 7 });
            DisplayToolResponse(multiplyResult);

            // Division (successful)
            Console.WriteLine("\nCalling calculator_divide with 20 and 4:");
            var divideResult = await client.CallTool("calculator_divide", new { a = 20, b = 4 });
            DisplayToolResponse(divideResult);

            // Division (error case - divide by zero)
            Console.WriteLine("\nCalling calculator_divide with 10 and 0 (divide by zero):");
            var divideByZeroResult = await client.CallTool(
                "calculator_divide",
                new { a = 10, b = 0 }
            );
            DisplayToolResponse(divideByZeroResult);

            // Power
            Console.WriteLine("\nCalling calculator_power with 2 and 8:");
            var powerResult = await client.CallTool(
                "calculator_power",
                new { baseNumber = 2, exponent = 8 }
            );
            DisplayToolResponse(powerResult);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error demonstrating calculator tools: {ex.Message}");
        }
    }

    public static async Task DemonstrateWarhammer40kTools(IMcpClient client)
    {
        Console.WriteLine("\n=== Warhammer 40k Tools ===");

        try
        {
            // Inquisitor Name Generator
            Console.WriteLine("\nCalling wh40k_inquisitor_name:");
            var inquisitorResult = await client.CallTool(
                "wh40k_inquisitor_name",
                new { includeTitle = true }
            );
            DisplayToolResponse(inquisitorResult);

            // Dice Rolling
            Console.WriteLine("\nCalling wh40k_roll_dice with 3d6 for hit rolls:");
            var diceResult = await client.CallTool(
                "wh40k_roll_dice",
                new
                {
                    diceCount = 3,
                    diceSides = 6,
                    flavor = "hit",
                }
            );
            DisplayToolResponse(diceResult);

            // Battle Simulation (async tool)
            Console.WriteLine("\nCalling wh40k_battle_simulation (asynchronous tool):");
            var battleResult = await client.CallTool(
                "wh40k_battle_simulation",
                new { imperialForce = "Space Marines", enemyForce = "Orks" }
            );
            DisplayToolResponse(battleResult);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error demonstrating Warhammer 40k tools: {ex.Message}");
        }
    }

    private static Task DisplayServerMetadataAsync(IMcpClient client)
    {
        Console.WriteLine("\n=== Server Metadata ===");

        Console.WriteLine(
            $"Protocol version: {client.NegotiatedProtocolVersion ?? "(unknown)"}"
        );

        if (client.ServerInfo != null)
        {
            Console.WriteLine(
                $"Server info: {client.ServerInfo.Name} v{client.ServerInfo.Version}"
            );
        }

        if (!string.IsNullOrWhiteSpace(client.Instructions))
        {
            Console.WriteLine($"Server instructions: {client.Instructions}");
        }

        if (client.ServerCapabilities != null)
        {
            var capabilitiesJson = JsonSerializer.Serialize(
                client.ServerCapabilities,
                new JsonSerializerOptions { WriteIndented = true }
            );
            Console.WriteLine("Server capabilities:");
            Console.WriteLine(capabilitiesJson);
        }

        return Task.CompletedTask;
    }

    private static async Task InspectResourcesAsync(IMcpClient client)
    {
        Console.WriteLine("\n=== Resources ===");

        try
        {
            var resources = await client.ListResources();
            Console.WriteLine($"Resources available: {resources.Length}");

            if (resources.Length > 0)
            {
                foreach (var resource in resources.Take(Math.Min(3, resources.Length)))
                {
                    Console.WriteLine(
                        $"- {resource.Uri} ({resource.Description ?? "no description"})"
                    );
                }

                var first = resources[0];
                var contents = await client.ReadResource(first.Uri);
                Console.WriteLine(
                    $"Sample resource '{first.Uri}' returned {contents.Length} content item(s)."
                );

                var preview = contents
                    .FirstOrDefault(content => !string.IsNullOrWhiteSpace(content.Text))
                    ?.Text;

                if (!string.IsNullOrWhiteSpace(preview))
                {
                    Console.WriteLine("Preview:");
                    Console.WriteLine(Indent(PreviewText(preview!, 200)));
                }
            }
            else
            {
                Console.WriteLine("No resources published by the server.");
            }
        }
        catch (NotImplementedException)
        {
            Console.WriteLine("Resources API not implemented by this server.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to inspect resources: {ex.Message}");
        }
    }

    private static async Task InspectPromptsAsync(IMcpClient client)
    {
        Console.WriteLine("\n=== Prompts ===");

        try
        {
            var prompts = await client.ListPrompts();
            Console.WriteLine($"Prompts available: {prompts.Length}");

            if (prompts.Length > 0)
            {
                foreach (var prompt in prompts.Take(Math.Min(3, prompts.Length)))
                {
                    Console.WriteLine(
                        $"- {prompt.Name}: {prompt.Description ?? "no description"}"
                    );
                }

                var first = prompts[0];
                var promptMessages = await client.GetPrompt(first.Name);
                Console.WriteLine(
                    $"Prompt '{first.Name}' contains {promptMessages.Length} message(s)."
                );

                if (promptMessages.Length > 0)
                {
                    var previewJson = JsonSerializer.Serialize(
                        promptMessages[0],
                        new JsonSerializerOptions { WriteIndented = true }
                    );
                    Console.WriteLine("First prompt message:");
                    Console.WriteLine(Indent(previewJson));
                }
            }
            else
            {
                Console.WriteLine("No prompts published by the server.");
            }
        }
        catch (NotImplementedException)
        {
            Console.WriteLine("Prompts API not implemented by this server.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to inspect prompts: {ex.Message}");
        }
    }

    private static async Task DemonstrateCompletionsAsync(IMcpClient client)
    {
        Console.WriteLine("\n=== Completions ===");

        if (client.ServerCapabilities?.Completions == null)
        {
            Console.WriteLine("Server does not advertise completion support.");
            return;
        }

        try
        {
            var recipientSuggestions = await client.CompleteAsync(
                new CompletionReference
                {
                    Type = "ref/prompt",
                    Name = "draft-follow-up-email",
                },
                new CompletionArgument
                {
                    Name = "recipient",
                    Value = "eng",
                }
            );

            Console.WriteLine(
                $"Suggested recipients for 'eng': {string.Join(", ", recipientSuggestions.Values)}"
            );

            var contextSuggestions = await client.CompleteAsync(
                new CompletionReference
                {
                    Type = "ref/prompt",
                    Name = "draft-follow-up-email",
                },
                new CompletionArgument
                {
                    Name = "context",
                    Value = string.Empty,
                },
                new CompletionContext
                {
                    Arguments = new Dictionary<string, string>
                    {
                        ["recipient"] = "engineering@mcp.example",
                    },
                }
            );

            Console.WriteLine(
                $"Suggested context entries: {string.Join(", ", contextSuggestions.Values)}"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Completion request failed: {ex.Message}");
        }
    }

    private static string PreviewText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n");
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized.Substring(0, maxLength).TrimEnd() + "…";
    }

    private static string Indent(string value, string indent = "  ")
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var lines = value.Split('\n', StringSplitOptions.None);
        return string.Join(Environment.NewLine, lines.Select(line => indent + line));
    }

    public static void DisplayToolResponse(ToolCallResult result)
    {
        // Check if we have any content
        if (result.Content == null || !result.Content.Any())
        {
            Console.WriteLine("No content returned");
            return;
        }

        // Check if there was an error
        if (result.IsError)
        {
            Console.WriteLine("Tool returned an error:");
        }

        // Process each content item
        foreach (var content in result.Content)
        {
            if (content is TextContent textContent)
            {
                Console.WriteLine(textContent.Text);
            }
            else
            {
                // Try to serialize the content as JSON for display
                try
                {
                    var json = JsonSerializer.Serialize(
                        content,
                        new JsonSerializerOptions { WriteIndented = true }
                    );
                    Console.WriteLine($"Content type: {content.GetType().Name}");
                    Console.WriteLine(json);
                }
                catch
                {
                    Console.WriteLine($"Received content of type: {content?.GetType().Name}");
                }
            }
        }
    }

    private static Func<
        AuthorizationCodeRequest,
        CancellationToken,
        Task<AuthorizationCodeResult>
    > CreatePkceInteractionHandler(HttpClient httpClient)
    {
        return async (request, cancellationToken) =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, request.AuthorizationUri);
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            if (!IsRedirectStatus(response.StatusCode))
            {
                throw new InvalidOperationException(
                    $"Authorization endpoint responded with status {response.StatusCode}."
                );
            }

            var location = response.Headers.Location
                ?? throw new InvalidOperationException(
                    "Authorization server did not provide a redirect location."
                );

            if (!location.IsAbsoluteUri)
            {
                if (request.RedirectUri == null)
                {
                    throw new InvalidOperationException(
                        "Redirect URI is relative and no base redirect URI is available."
                    );
                }

                location = new Uri(request.RedirectUri, location);
            }

            var query = QueryHelpers.ParseQuery(location.Query);
            if (!query.TryGetValue("code", out var codeValues))
            {
                throw new InvalidOperationException("Authorization server did not return an authorization code.");
            }

            var code = codeValues.ToString();
            var returnedState = query.TryGetValue("state", out var stateValues)
                ? stateValues.ToString()
                : string.Empty;

            return new AuthorizationCodeResult(code, returnedState);
        };
    }

    private static bool IsRedirectStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.MovedPermanently
        || statusCode == HttpStatusCode.Found
        || statusCode == HttpStatusCode.SeeOther
        || statusCode == HttpStatusCode.TemporaryRedirect
        || (int)statusCode == 308; // Permanent Redirect
}
