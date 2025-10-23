# SimpleServer Example for Mcp.Net

This project demonstrates how to create a basic MCP server using the Mcp.Net library. The SimpleServer includes example tools for a calculator and Warhammer 40k themed functionality.

## Overview

The SimpleServer example shows how to:

1. Set up and configure an MCP server
2. Create and register tools
3. Handle client connections via SSE or stdio
4. Process tool invocations
5. Return different types of responses (simple values and complex objects)
6. Implement asynchronous tools

## Getting Started

### Prerequisites

- .NET 9.0 or later
- A client to connect to the server (see the [SimpleClient example](../Mcp.Net.Examples.SimpleClient))

## Running the Server

You can run the SimpleServer either directly on your local machine or using Docker.

### Locally

Run the server with default settings (SSE transport on port 5000):

```bash
dotnet run
```

Or from the solution root directory:

```bash
dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj
```

Run with a specific port:

```bash
dotnet run -- --port 5001
```

Run with stdio transport (for direct process-to-process communication):

```bash
dotnet run -- --stdio
```

Disable authentication (all requests allowed without tokens):

```bash
dotnet run -- --no-auth
```

Use other command-line options:

```bash
# Set server name
dotnet run -- --name "My Custom MCP Server"

# Set hostname (default: localhost)
dotnet run -- --hostname 0.0.0.0

# Load external tool assemblies
dotnet run -- --tool-assembly /path/to/custom/tools.dll

# Set log level
dotnet run -- --log-level Debug

# Combine multiple options
dotnet run -- --port 8080 --hostname 0.0.0.0 --name "Production MCP Server" --tool-assembly tools1.dll --tool-assembly tools2.dll
```

### Using with LLM Demo

The SimpleServer is designed to work seamlessly with the LLM demo project:

1. Start this server in one terminal:
   ```bash
   dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj
   ```

2. In another terminal, run the LLM demo:
   ```bash
   dotnet run --project Mcp.Net.LLM/Mcp.Net.LLM.csproj
   ```

The LLM demo will automatically connect to this server and make its tools available to OpenAI or Anthropic models.

### Using Docker

Build the Docker image:

```bash
# From the Mcp.Net root directory
docker build -t mcp-simple-server -f Mcp.Net.Examples.SimpleServer/Dockerfile .
```

Run the containerized server:

```bash
# Run on port 8080
docker run -p 8080:8080 -e PORT=8080 mcp-simple-server
```

Connect to the containerized server with SimpleClient:

```bash
# In a separate terminal
dotnet run --project Mcp.Net.Examples.SimpleClient -- --url http://localhost:8080
```

## Included Tools

The SimpleServer includes the following example tools:

### Calculator Tools

- `calculator.add`: Add two numbers
- `calculator.subtract`: Subtract one number from another
- `calculator.multiply`: Multiply two numbers
- `calculator.divide`: Divide one number by another (with error handling)
- `calculator.power`: Raise a number to a power

### Warhammer 40k Tools

- `wh40k.inquisitor_name`: Generate a Warhammer 40k Inquisitor name
- `wh40k.roll_dice`: Roll dice with Warhammer 40k flavor
- `wh40k.battle_simulation`: Simulate a battle (asynchronous tool)

### Seeded Resources & Prompts

- Resources:
  - `mcp://docs/simple-server/getting-started`
  - `mcp://docs/simple-server/oauth-flow`
- Prompts:
  - `summarize-resource`
  - `draft-follow-up-email`

These are registered at startup so clients can test MCP resource and prompt capabilities without additional configuration.

## Key Components

- **Program.cs**: Main entry point that sets up and starts the server
- **CalculatorTools.cs**: Simple calculator tools example
- **Warhammer40kTools.cs**: Themed tools demonstrating different MCP capabilities
- **SampleContentCatalog.cs**: Seeds demo resources and prompts for integration testing

## Environment Variables

- `PORT` or `MCP_PORT`: Set the server port (default: 5000)
- `HOSTNAME` or `MCP_HOSTNAME`: Set the hostname to bind to (default: localhost)
- `SERVER_NAME` or `MCP_SERVER_NAME`: Set the server name
- `LOG_LEVEL` or `MCP_LOG_LEVEL`: Set the log level (default: Debug)
- `MCP_DEBUG_TOOLS`: Enable tool registration debugging (default: true)

## Creating Your Own Tools

To create your own tools:

1. Create a class with static methods
2. Decorate methods with `[McpTool]` attribute
3. Decorate parameters with `[McpParameter]` attribute
4. Return values or objects as the tool result

Example:

```csharp
[McpTool("my.tool", "My tool description")]
public static MyResult MyTool(
    [McpParameter(required: true, description: "Parameter description")] string param1)
{
    // Tool implementation
    return new MyResult { ... };
}
```

For asynchronous tools, return a `Task<T>`:

```csharp
[McpTool("my.async_tool", "My async tool description")]
public static async Task<MyResult> MyAsyncTool(
    [McpParameter(required: true, description: "Parameter description")] string param1)
{
    // Async implementation
    await Task.Delay(1000);
    return new MyResult { ... };
}
```

## Configuration Options

The server can be configured with:

- Name and version
- Transport type (SSE or stdio)
- Port number (for SSE transport)
- Logging levels
- Custom instructions

See `Program.cs` for examples of different configuration options.

## OAuth 2.1 Demo Overview

- `/oauth/register`: Supports dynamic client registration (public clients). The SimpleClient uses this endpoint automatically in PKCE mode.
- `/oauth/authorize`: Issues authorization codes and enforces S256 PKCE challenges and the `resource` indicator.
- `/oauth/token`: Handles authorization-code exchange and refresh tokens, validating audience and resource values.
- Access tokens are JWTs signed with an in-memory test key and scoped to the MCP resource (`http://localhost:5000/mcp` by default).
- Run with `--no-auth` to disable authentication when experimenting.

## Related Resources

- [Mcp.Net.Examples.SimpleClient](../Mcp.Net.Examples.SimpleClient): Client example for connecting to this server
- [Mcp.Net.LLM](../Mcp.Net.LLM): Interactive LLM demo that uses this server's tools
- [MCP Protocol Documentation](../MCPProtocol.md): Details about the MCP protocol
