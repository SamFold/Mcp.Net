using System;
using System.Net.Http;
using System.Threading.Tasks;
using Mcp.Net.Client;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Examples.Shared;

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

        var clientBuilder = new McpClientBuilder()
            .WithName("SimpleClientExample")
            .WithVersion("1.0.0")
            .WithTitle("Simple Client Example")
            .UseSseTransport(options.ServerUrl);

        if (options.UseAuthentication)
        {
            Console.WriteLine("Using demo OAuth client credentials for authentication.");
            var oauthOptions = DemoOAuthDefaults.CreateClientOptions(baseUri);
            clientBuilder.WithClientCredentialsAuth(oauthOptions, new HttpClient());
        }
        else
        {
            Console.WriteLine("Authentication disabled; requests will be sent without bearer tokens.");
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
            Console.WriteLine("\nCalling calculator.divide with 10 and 0 (divide by zero):");
            var divideByZeroResult = await client.CallTool(
                "calculator.divide",
                new { a = 10, b = 0 }
            );
            DisplayToolResponse(divideByZeroResult);

            // Power
            Console.WriteLine("\nCalling calculator_power with 2 and 8:");
            var powerResult = await client.CallTool(
                "calculator_power",
                new { basenumber = 2, exponent = 8 } // Lower case parameter name to match server expectation
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
                    dicecount = 3,
                    dicesides = 6,
                    flavor = "hit",
                }
            );
            DisplayToolResponse(diceResult);

            // Battle Simulation (async tool)
            Console.WriteLine("\nCalling wh40k_battle_simulation (asynchronous tool):");
            var battleResult = await client.CallTool(
                "wh40k_battle_simulation", 
                new { imperialforce = "Space Marines", enemyforce = "Orks" } // Lower case parameter names for consistency
            );
            DisplayToolResponse(battleResult);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error demonstrating Warhammer 40k tools: {ex.Message}");
        }
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
                    var json = System.Text.Json.JsonSerializer.Serialize(
                        content,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
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
}
