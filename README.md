<div style="display: flex; align-items: center;">
  <div>
    <h1>Mcp.Net - Model Context Protocol for .NET 🚀</h1>
    <p><b>Connect your apps to AI models with a standardized protocol for tools, resources, and prompts</b></p>
    <p>
      <a href="https://www.nuget.org/packages/Mcp.Net.Core/"><img src="https://img.shields.io/nuget/v/Mcp.Net.Core.svg" alt="NuGet"></a>
      <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
    </p>
  </div>
</div>

## ✨ What is Mcp.Net?

Mcp.Net is a .NET implementation of the Model Context Protocol (MCP) - a standardized way for apps to talk to AI models and execute tools. Think of it as the "HTTP of AI tool usage" - a clean, consistent way for your app to give AI models the ability to:

- 🧰 Use tools like search, weather lookup, database access
- 🌐 Access web resources and fetch web content
- 📝 Work with predefined prompts and templates

> **⚠️ Pre-1.0 Notice** 
>
> This is version 0.9.0 - the core is stable but some features are still in development.
> See [Current Status](#current-status) for details.

## 🏃‍♀️ Quick Start

### Try the interactive LLM demo

Experience MCP (with web-search, scraping, twilio, various demo tools) with OpenAI or Anthropic models in just two steps:

```bash
# 1. Start the server with demo tools
dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj

# 2. In a new terminal, run the LLM chat app (requires OpenAI or Anthropic API key)
dotnet run --project Mcp.Net.LLM/Mcp.Net.LLM.csproj
```

See the [LLM demo documentation](Mcp.Net.LLM/README.md) for more details.

### Install the packages

```bash
# For building a server (the thing that provides tools)
dotnet add package Mcp.Net.Server

# For building a client (the thing that talks to AI models)
dotnet add package Mcp.Net.Client
```

### Run the sample server + client with OAuth 2.1

```bash
# Terminal 1 — start the demo server (SSE on http://localhost:5000)
dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj

# Terminal 2 — launch the client (performs dynamic registration + PKCE)
dotnet run --project Mcp.Net.Examples.SimpleClient -- --url http://localhost:5000 --auth-mode pkce
```

> ℹ️ The first SSE GET returns `401 Unauthorized` by design. The client follows the
> `WWW-Authenticate` challenge, registers itself at `/oauth/register`, completes the PKCE
> handshake, and reconnects with a bearer token. Watch the logs to see resources, prompts,
> and tools being exercised end-to-end.

### Create your first MCP server in 2 minutes

```csharp
using Mcp.Net.Core.Attributes;
using Mcp.Net.Server;

// 1. Create a simple stdio server
var server = new McpServer(
    new ServerInfo { Name = "QuickStart Server", Version = "1.0" }
);

// 2. Define tools using simple attributes and POCOs
[McpTool("Calculator", "Math operations")]
public class CalculatorTools
{
    // Simple synchronous tool that returns a plain string
    [McpTool("add", "Add two numbers")]
    public string Add(
        [McpParameter(required: true, description: "First number")] double a,
        [McpParameter(required: true, description: "Second number")] double b)
    {
        return $"The sum of {a} and {b} is {a + b}";
    }
    
    // Async tool with a POCO return type - easiest approach!
    [McpTool("getWeather", "Get weather for a location")]
    public async Task<WeatherResponse> GetWeatherAsync(
        [McpParameter(required: true, description: "Location")] string location)
    {
        // Simulate API call
        await Task.Delay(100);
        
        // Just return a POCO - no need to deal with ToolCallResult!
        return new WeatherResponse
        {
            Location = location,
            Temperature = "72°F",
            Conditions = "Sunny",
            Forecast = new[] { "Clear", "Partly cloudy", "Clear" }
        };
    }
}

// Simple POCO class
public class WeatherResponse
{
    public string Location { get; set; }
    public string Temperature { get; set; }
    public string Conditions { get; set; }
    public string[] Forecast { get; set; }
}

// 3. Register all tools from assembly in one line
server.RegisterToolsFromAssembly(Assembly.GetExecutingAssembly(), serviceProvider);

// 4. Connect to stdio transport and start
await server.ConnectAsync(new StdioTransport());

// Server is now running and ready to process requests!
```

### Manual Tool Registration (Alternative style)

For more control, you can also register tools directly:

```csharp
using System.Text.Json;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Server;

// Create server
var server = new McpServer(
    new ServerInfo { Name = "Manual Server", Version = "1.0" }
);

// Register tool with explicit schema and handler
server.RegisterTool(
    name: "multiply",
    description: "Multiply two numbers",
    inputSchema: JsonDocument.Parse(@"
    {
        ""type"": ""object"",
        ""properties"": {
            ""x"": { ""type"": ""number"" },
            ""y"": { ""type"": ""number"" }
        },
        ""required"": [""x"", ""y""]
    }
    ").RootElement,
    handler: async (args) =>
    {
        var x = args?.GetProperty("x").GetDouble() ?? 0;
        var y = args?.GetProperty("y").GetDouble() ?? 0;
        var result = x * y;
        
        // For full control, you can explicitly use ToolCallResult
        return new ToolCallResult
        {
            Content = new[] { new TextContent { Text = $"{x} * {y} = {result}" } }
        };
    }
);
```

### Connect a client to your server

```csharp
using Mcp.Net.Client;

// Connect to a stdio server (like Claude or a local MCP server)
var client = new StdioMcpClient("MyApp", "1.0");
await client.Initialize();

// List available tools
var tools = await client.ListTools();
Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

// Call the add tool
var result = await client.CallTool("add", new { a = 5, b = 3 });
Console.WriteLine(((TextContent)result.Content.First()).Text); // "The sum is 8"

// Call the weather tool
var weatherResult = await client.CallTool("getWeather", new { location = "San Francisco" });
Console.WriteLine(((TextContent)weatherResult.Content.First()).Text); 
// "The weather in San Francisco is sunny and 72°F"
```

## 📊 Project Structure

- **Mcp.Net.Core**: Models, interfaces, and base protocol components
- **Mcp.Net.Server**: Server-side implementation with transports (SSE and stdio)
- **Mcp.Net.Client**: Client libraries for connecting to MCP servers
- **Mcp.Net.Examples.SimpleServer**: [Simple example server](Mcp.Net.Examples.SimpleServer/README.md) with calculator and themed tools
- **Mcp.Net.Examples.SimpleClient**: [Simple example client](Mcp.Net.Examples.SimpleClient/README.md) that connects to MCP servers
- **Mcp.Net.LLM**: [Interactive LLM demo](Mcp.Net.LLM/README.md) integrating OpenAI/Anthropic models with MCP tools
- **Mcp.Net.Examples.ExternalTools**: Standalone tool library that can be loaded by any MCP server

## 🔌 Key Features

- **Two Transport Options**:
  - ⌨️ **stdio**: Perfect for CLI tools and direct model interaction
  - 🌐 **SSE**: Ideal for web apps and browser integrations
  
- **Tool Management**:
  - ✅ Dynamic tool discovery
  - ✅ JSON Schema validation for parameters
  - ✅ Both synchronous and async tool support
  - ✅ Error handling and result formatting

- **OAuth 2.1 Reference Flow**:
  - ✅ Dynamic client registration (`/oauth/register`) with in-memory persistence
  - ✅ Authorization code + PKCE with enforced `resource` indicators
  - ✅ Refresh-token rotation and audience validation in the demo server/client samples

- **Flexible Hosting**:
  - ✅ Use as standalone server
  - ✅ Embed in ASP.NET Core applications
  - ✅ Run as background service

## 🔧 Server Configuration Options

The MCP server provides multiple ways to configure your server, especially for controlling network settings when using the SSE transport:

### Using the Builder Pattern

```csharp
// Configure the server with the builder pattern
var builder = new McpServerBuilder()
    .WithName("My MCP Server")
    .WithVersion("1.0.0")
    .WithInstructions("This server provides helpful tools")
    // Configure network settings
    .UsePort(8080)           // Default is 5000
    .UseHostname("0.0.0.0")  // Default is localhost
    // Configure transport mode
    .UseSseTransport();      // Uses the port and hostname configured above
```

### Using Command Line Arguments

When running the server from the command line:

```bash
# Run with custom port and hostname
dotnet run --project Mcp.Net.Server --port 8080 --hostname 0.0.0.0

# For cloud environments, binding to 0.0.0.0 is usually required
dotnet run --project Mcp.Net.Server --hostname 0.0.0.0

# Run with stdio transport instead of SSE
dotnet run --project Mcp.Net.Server --stdio
# or use the shorthand
dotnet run --project Mcp.Net.Server -s

# Enable debug-level logging
dotnet run --project Mcp.Net.Server --debug
# or use the shorthand
dotnet run --project Mcp.Net.Server -d

# Specify a custom log file path
dotnet run --project Mcp.Net.Server --log-path /path/to/logfile.log

# Use a specific URL scheme (http or https)
dotnet run --project Mcp.Net.Server --scheme https

# Combine multiple options
dotnet run --project Mcp.Net.Server --stdio --debug --port 8080 --hostname 0.0.0.0
```

The ServerConfiguration and CommandLineOptions classes handle these arguments:

```csharp
// CommandLineOptions.cs parses command-line arguments
public static CommandLineOptions Parse(string[] args)
{
    var options = new CommandLineOptions(args)
    {
        UseStdio = args.Contains("--stdio") || args.Contains("-s"),
        DebugMode = args.Contains("--debug") || args.Contains("-d"),
        LogPath = GetArgumentValue(args, "--log-path") ?? "mcp-server.log",
        Port = GetArgumentValue(args, "--port"),
        Hostname = GetArgumentValue(args, "--hostname"),
        Scheme = GetArgumentValue(args, "--scheme")
    };
    return options;
}
```

### Using Environment Variables

```bash
# Set standard environment variables before running
export MCP_SERVER_PORT=8080
export MCP_SERVER_HOSTNAME=0.0.0.0
export MCP_SERVER_SCHEME=http

# Cloud platform compatibility - many cloud platforms use PORT
export PORT=8080

dotnet run --project Mcp.Net.Server
```

The ServerConfiguration class handles these environment variables with a priority-based approach:

```csharp
// ServerConfiguration.cs handles environment variables:
private void LoadFromEnvironmentVariables()
{
    // Standard MCP hostname variable
    string? envHostname = Environment.GetEnvironmentVariable("MCP_SERVER_HOSTNAME");
    if (!string.IsNullOrEmpty(envHostname))
    {
        Hostname = envHostname;
    }
    
    // Cloud platform compatibility - PORT is standard on platforms like Google Cloud Run
    string? cloudRunPort = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrEmpty(cloudRunPort) && int.TryParse(cloudRunPort, out int parsedCloudPort))
    {
        Port = parsedCloudPort;
    }
    else
    {
        // Fall back to MCP-specific environment variable
        string? envPort = Environment.GetEnvironmentVariable("MCP_SERVER_PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int parsedEnvPort))
        {
            Port = parsedEnvPort;
        }
    }
    
    // HTTPS configuration
    string? envScheme = Environment.GetEnvironmentVariable("MCP_SERVER_SCHEME");
    if (!string.IsNullOrEmpty(envScheme))
    {
        Scheme = envScheme.ToLowerInvariant();
    }
}
```

### Using appsettings.json

The server also reads settings from appsettings.json:

```json
{
  "Server": {
    "Port": 8080,
    "Hostname": "0.0.0.0",
    "Scheme": "http"
  }
}
```

The configuration is loaded with a tiered priority approach:

```csharp
// SseServerBuilder automatically loads from configuration files:
private void ConfigureAppSettings(WebApplicationBuilder builder, string[] args)
{
    // Add configuration from multiple sources with priority:
    // 1. Command line args (highest)
    // 2. Environment variables
    // 3. appsettings.json (lowest)
    builder.Configuration.AddJsonFile("appsettings.json", optional: true);
    builder.Configuration.AddEnvironmentVariables("MCP_");
    builder.Configuration.AddCommandLine(args);
}
```

### Configuration Priority

The server uses this priority order when resolving configuration:

1. Command line arguments (highest priority)
2. Environment variables
3. appsettings.json configuration
4. Default values (lowest priority)

This allows for flexible deployment in various environments, from local development to cloud platforms.

### Health Checks and Observability

The SSE server includes built-in health check endpoints:

- `/health` - Overall health status
- `/health/ready` - Readiness check for load balancers
- `/health/live` - Liveness check for container orchestrators

### Future Planned Features

- HTTPS/TLS support enhancements
- Advanced metrics and telemetry
- Authentication integration
- Resource quota management

## 🛠️ Transport Implementations

### Server-Sent Events (SSE)

Perfect for web applications, the SSE transport:
- Maintains a persistent HTTP connection
- Uses standard event streaming
- Supports browser-based clients
- Enables multiple concurrent connections

### Standard I/O (stdio)

Ideal for CLI tools and AI model integration:
- Communicates via standard input/output
- Works great with Claude, GPT tools
- Simple line-based protocol
- Lightweight and efficient
- Requests now honour newline-delimited framing and expose a configurable `StdioClientTransport.RequestTimeout` (default 60s, set to `Timeout.InfiniteTimeSpan` to disable). Pending requests are cancelled automatically when the transport closes so callers can surface clean shutdown errors.

## 🧩 Advanced Usage

### ASP.NET Core Integration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add MCP server to services
builder.Services.AddMcpServer(b =>
{
    b.WithName("My MCP Server")
     .WithVersion("1.0.0")
     .WithInstructions("Server providing math and weather tools")
     .UsePort(8080)          // Configure port (default: 5000)
     .UseHostname("0.0.0.0") // Configure hostname (default: localhost)
     .UseSseTransport();     // Uses the port and hostname configured above
});

// Configure middleware
var app = builder.Build();
app.UseCors(); // If needed
app.UseMcpServer();

await app.RunAsync();
```

### Custom Content Types

```csharp
// Return both text and an image
return new ToolCallResult
{
    Content = new IContent[] 
    { 
        new TextContent { Text = "Here's the chart you requested:" },
        new ImageContent 
        { 
            MimeType = "image/png",
            Data = Convert.ToBase64String(imageBytes) 
        }
    }
};
```

## 📋 Current Status

This implementation is currently at version 0.9.0:

### Fully Implemented Features
- ✅ Core JSON-RPC message exchange
- ✅ Dual transport support (SSE and stdio)
- ✅ Tool registration and discovery
- ✅ Tool invocation with parameter validation
- ✅ Error handling and propagation
- ✅ Text-based content responses
- ✅ Client connection and initialization flow
- ✅ Configurable server port and hostname
- ✅ Resource catalogue (list/read) with sample markdown content
- ✅ Prompt catalogue (list/get) demonstrated by SimpleServer

### Partially Implemented Features
- ⚠️ Advanced content types (Image, Resource, Embedded)
- ⚠️ XML documentation

## 🧪 Testing & Development

```bash
dotnet build Mcp.Net.sln
dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj
```

The test suite covers happy-path tool invocation and negative-path OAuth scenarios (dynamic
registration failures, PKCE mismatches, resource-indicator validation, and refresh-token
replays). The sample server seeds markdown resources and reusable prompts so integration
runs exercise the entire capability surface.

## 🔒 Authentication & Security Notes

- The demo server includes a lightweight OAuth 2.1 resource server with dynamic registration,
  authorization code + PKCE, refresh tokens, and resource indicators. Use it for local testing;
  production systems should wire in a dedicated identity provider (e.g., Supabase, Auth0,
  Azure AD).
- Clients must include `Authorization: Bearer <token>` plus the negotiated `Mcp-Session-Id`
  and `MCP-Protocol-Version` headers on every POST/GET to `/mcp`.
- SimpleClient defaults to PKCE (`--auth-mode pkce`) but still supports legacy
  client-credentials mode (`--auth-mode client`) when interacting with static client IDs.
- Tool code can read the authenticated principal from `context.Items["AuthenticatedUserId"]`
  (and other claims via `AuthResult`) to enforce user-level authorization before touching
  downstream APIs or databases.

### Connecting to External Identity Providers

The server now accepts production configuration via `appsettings.json` (or environment variables):

```jsonc
{
  "Server": {
    "Authentication": {
      "OAuth": {
        "Authority": "https://your-idp.example",
        "Resource": "https://your-mcp.example/mcp",
        "ResourceMetadataPath": "/.well-known/oauth-protected-resource",
        "AuthorizationServers": [
          "https://your-idp.example/.well-known/oauth-authorization-server"
        ],
        "ValidAudiences": [ "https://your-mcp.example/mcp" ],
        "ValidIssuers": [ "https://your-idp.example" ],
        "SigningKeys": [
          "base64-or-base64url-encoded-HS256-key-if-jwks-not-available"
        ]
      }
    }
  }
}
```

- **Auth0 / Entra ID**: point `Authority` at the OpenID metadata endpoint, add the MCP endpoint as
  an API/audience, and ensure issued tokens include the user’s stable identifier (`sub`). The server
  will fetch signing keys from JWKS automatically.
- **Supabase**: either mark Supabase as the authority (if JWT signing enabled) or run a lightweight
  exchange service that mints MCP-scoped tokens using Supabase session claims. Map `sub` to your
  database row IDs and store the Supabase signing secret in `SigningKeys` when JWKS is not exposed.
- **Clerk**: register an OAuth application that scopes tokens to the MCP resource, then configure
  Clerk’s issuer/JWKS URLs. Tokens arrive with the Clerk user ID in the `sub` claim, which your tools
  can trust after validation.
- HTTP failures now raise `McpClientHttpException`, which captures the status code, response body, and
  originating request so host applications can surface actionable error messages to users.

When writing MCP tools that touch per-user resources, compare the incoming request parameters to the
authenticated subject (or enforce role claims) before executing queries—this prevents a caller from
mutating data for another account even if the tool input is tampered.

## 📚 Learn More

- [Full Documentation](docs/README.md)
- [API Reference](docs/api/README.md)
- [Protocol Specification](MCPProtocol.md)
- [Simple Server Example](Mcp.Net.Examples.SimpleServer/README.md)
- [Simple Client Example](Mcp.Net.Examples.SimpleClient/README.md)

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

Made with ❤️ by [Sam Fold](https://github.com/SamFold)
