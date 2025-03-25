# SimpleClient Example for Mcp.Net

This project demonstrates how to create a client that connects to an MCP server using the Mcp.Net library. The SimpleClient includes examples for connecting via SSE and stdio, and demonstrates how to invoke various tools.

## Overview

The SimpleClient example shows how to:

1. Create and initialize an MCP client
2. Connect to a server via SSE or stdio transport
3. List available tools on the server
4. Call tools and handle responses
5. Process different types of tool responses
6. Handle errors from tool invocations

## Getting Started

### Prerequisites

- .NET 9.0 or later
- An MCP server to connect to (see the [SimpleServer example](../Mcp.Net.Examples.SimpleServer))

### Running the Client

Run the client with default settings (SSE transport to localhost:5000):

```bash
dotnet run
```

Connect to a specific server URL:

```bash
dotnet run -- --url http://localhost:5001
```

Connect using stdio transport to a local server process:

```bash
dotnet run -- --command "dotnet run --project ../Mcp.Net.Examples.SimpleServer -- --stdio"
```

## Examples

The SimpleClient demonstrates how to:

### Connect to a Server

```csharp
// SSE connection
using IMcpClient client = new SseMcpClient(
    "http://localhost:5000",
    "SimpleClientExample",
    "1.0.0"
);

// Stdio connection
using IMcpClient client = new StdioMcpClient(
    "dotnet run --project ../Mcp.Net.Examples.SimpleServer -- --stdio",
    "SimpleClientExample",
    "1.0.0"
);
```

### Initialize the Client

```csharp
await client.Initialize();
```

### List Available Tools

```csharp
var tools = await client.ListTools();
foreach (var tool in tools)
{
    Console.WriteLine($"- {tool.Name}: {tool.Description}");
}
```

### Call a Tool

```csharp
// Simple tool call
var result = await client.CallTool("calculator.add", new { a = 5, b = 3 });

// Tool call with error handling
try {
    var result = await client.CallTool("calculator.divide", new { a = 10, b = 0 });
    if (result.IsError) {
        Console.WriteLine($"Tool returned an error: {result.ErrorMessage}");
    }
} catch (Exception ex) {
    Console.WriteLine($"Error calling tool: {ex.Message}");
}
```

## Key Components

- **Program.cs**: Main entry point that parses command-line arguments and starts the appropriate client
- **SseClientExample.cs**: Example of using SSE transport to connect to an MCP server
- **StdioClientExample.cs**: Example of using stdio transport to connect to an MCP server

## Environment Variables

- `MCP_PORT`: Default port to use when connecting (default: 5000)
- `MCP_LOG_LEVEL`: Set the log level for client operations (default: Debug)
- `DOTNET_ENVIRONMENT`: Set to "Development" for more verbose logging

## Working with Tool Responses

The SimpleClient demonstrates how to handle different types of tool responses:

### Text Content

```csharp
if (content is TextContent textContent)
{
    Console.WriteLine(textContent.Text);
}
```

### Complex Objects

```csharp
var json = System.Text.Json.JsonSerializer.Serialize(
    content,
    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
);
Console.WriteLine(json);
```

### Error Handling

```csharp
if (result.IsError)
{
    Console.WriteLine("Tool returned an error:");
    // Handle error
}
```

## Related Resources

- [Mcp.Net.Examples.SimpleServer](../Mcp.Net.Examples.SimpleServer): Server example to connect to
- [MCP Protocol Documentation](../MCPProtocol.md): Details about the MCP protocol