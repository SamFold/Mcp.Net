# MCP Server Extension Methods

This directory contains extension methods for integrating the MCP server with ASP.NET Core applications.

## Extension Method Organization

The extension methods are organized into the following categories:

- **Core Server Extensions**: Basic server setup and configuration
- **Authentication Extensions**: Authentication services and configuration
- **Logging Extensions**: Logging configuration and setup
- **Tool Registration Extensions**: Tool discovery and registration
- **CORS Extensions**: Cross-Origin Resource Sharing services
- **Transport Extensions**: Communication transports (SSE, STDIO)

## Usage Guidelines

### Core Server Extensions

```csharp
// Basic server usage
services.AddMcpServer(builder => 
{
    builder.WithName("My MCP Server")
           .WithVersion("1.0");
});

// Use the server in the middleware pipeline
app.UseMcpServer();
```

### Authentication Extensions

```csharp
// Configure OAuth authentication via the server builder
services.AddMcpServer(mcpBuilder =>
{
    mcpBuilder.WithAuthentication(auth =>
    {
        auth.WithOAuth(options =>
        {
            options.Authority = "https://auth.example.com";
            options.Resource = "https://mcp.example.com/mcp";
        });
    });
});

// Disable authentication (development only)
services.AddMcpServer(mcpBuilder => mcpBuilder.WithAuthentication(auth => auth.WithNoAuth()));
```

### Logging Extensions

```csharp
// Configure logging with options
services.AddMcpLogging(options => 
{
    options.MinimumLogLevel = LogLevel.Information;
    options.UseConsoleLogging = true;
    options.LogFilePath = "logs/mcp-server.log";
});
```

### Tool Registration Extensions

```csharp
// Configure tool registration
services.AddMcpTools(options => 
{
    options.IncludeEntryAssembly = true;
    options.EnableDetailedLogging = true;
});

// Add specific assemblies
services.AddMcpTools(options => 
{
    options.Assemblies.Add(typeof(MyTool).Assembly);
});
```

### Transport Extensions

```csharp
// Configure SSE transport
services.AddMcpSseTransport(options => 
{
    options.Port = 5000;
    options.Hostname = "localhost";
});

// Configure STDIO transport
services.AddMcpStdioTransport(options => 
{
    options.UsePrettyConsoleOutput = true;
});
```

## Codebase Organization

- `/Extensions/CoreServerExtensions.cs`: Core server registration methods
- `/Extensions/AuthenticationExtensions.cs`: Authentication configuration methods
- `/Extensions/LoggingExtensions.cs`: Logging configuration methods
- `/Extensions/ToolRegistrationExtensions.cs`: Tool registration methods
- `/Extensions/CorsExtensions.cs`: CORS configuration methods
- `/Extensions/Transport/TransportExtensions.cs`: Common transport functionality
- `/Extensions/Transport/SseTransportExtensions.cs`: SSE transport methods
- `/Extensions/Transport/StdioTransportExtensions.cs`: STDIO transport methods

## Extension Namespaces

All extension methods are organized by functionality in the `Mcp.Net.Server.Extensions` namespace:

```csharp
// Core server extensions
using Mcp.Net.Server.Extensions;

// Transport-specific extensions
using Mcp.Net.Server.Extensions.Transport;
```

Make sure to import the appropriate namespace to access the extension methods you need.
