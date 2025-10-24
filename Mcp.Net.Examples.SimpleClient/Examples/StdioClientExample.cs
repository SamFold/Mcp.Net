using System;
using System.Threading.Tasks;
using Mcp.Net.Client;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Examples.SimpleClient.Elicitation;

namespace Mcp.Net.Examples.SimpleClient.Examples;

public class StdioClientExample
{
    public static async Task Run(ClientOptions options)
    {
        if (string.IsNullOrEmpty(options.ServerCommand))
        {
            Console.WriteLine("Error: Server command is required. Use --command parameter.");
            return;
        }

        Console.WriteLine("Running Example: Stdio Client");
        Console.WriteLine($"Starting server with command: {options.ServerCommand}");

        IMcpClient? client = null;

        try
        {
            // Create a Stdio client
            client = new StdioMcpClient(options.ServerCommand, "SimpleClientExample", "1.0.0");
            client.SetElicitationHandler(ConsoleElicitationHandler.HandleAsync);

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
            await SseClientExample.DemonstrateCalculatorTools(client);

            // Demonstrate Warhammer 40k Tools
            await SseClientExample.DemonstrateWarhammer40kTools(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            // Clean up
            if (client != null)
            {
                Console.WriteLine("Disposing client...");
                client.Dispose();
            }
        }
    }
}
