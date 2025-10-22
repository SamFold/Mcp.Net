# MCP Server Implementation

This is a reference implementation of the Model Context Protocol (MCP) server. The server supports both Server-Sent Events (SSE) transport for web-based applications and Standard I/O (stdio) transport for command-line integration.

## Features

- Dual transport support (SSE and stdio)
- JSON-RPC 2.0 message processing
- Tool registration and discovery
- Structured logging with Serilog
- Attribute-based tool registration
- Automatic JSON Schema generation
- Cloud-ready configuration
- Improved connection management

## Running the Server

### Basic Usage

Run with SSE transport (default, for web clients):

```bash
dotnet run
```

Run with stdio transport (for command-line clients):

```bash
dotnet run --stdio
# or
dotnet run -s
```

### Configuration Options

The server supports multiple configuration methods:

```bash
# Set network options
dotnet run --port 8080 --hostname 0.0.0.0

# Enable debug-level logging
dotnet run --debug
# or
dotnet run -d

# Specify a custom log file path
dotnet run --log-path /path/to/logfile.log

# Set a specific URL scheme (http or https)
dotnet run --scheme https

# Combine options
dotnet run --stdio --debug --log-path mcpserver.log --port 8080 --hostname 0.0.0.0
```

### Environment Variables

The server supports configuration via environment variables with priority-based resolution:

```bash
# Standard environment variables
export MCP_SERVER_HOSTNAME=0.0.0.0
export MCP_SERVER_PORT=8080
export MCP_SERVER_SCHEME=http

# Cloud platform compatibility 
# (PORT is standard on many cloud platforms like Google Cloud Run)
export PORT=8080

dotnet run
```

### Configuration Priority

The server follows this priority order when resolving configuration:

1. Command line arguments (highest priority)
2. Environment variables
3. appsettings.json configuration
4. Default values (lowest priority)

### Default Values

If no configuration is provided, these defaults are used:

- Hostname: `localhost` (local development) or `0.0.0.0` (in containers)
- Port: `5000`
- Scheme: `http`
- Log Path: `mcp-server.log`
- Debug Mode: `false`
- Transport: SSE (unless `--stdio` is specified)

### Container Support

Docker support is included for containerized deployment:

```bash
# Build the Docker image
docker build -t mcp-server .

# Run the container
docker run -p 8080:8080 -e PORT=8080 mcp-server
```

### Health Checks and Observability

The SSE server includes built-in health check endpoints:

- `/health` - Overall health status
- `/health/ready` - Readiness check for load balancers
- `/health/live` - Liveness check for container orchestrators

### Future Enhancements

In upcoming releases, we plan to implement:
- Enhanced HTTPS/TLS support
- Advanced metrics and telemetry
- Authentication and authorization mechanisms
- Distributed connection management
- Resource quota and rate limiting

## Transport Modes

### SSE Transport (Web Clients)

When running in SSE mode, the server:

1. Listens on the configured address (default: `http://localhost:5000`)
2. Clients interact with the unified `/mcp` endpoint
   - `GET /mcp` opens the SSE stream for server initiated messages
   - `POST /mcp` submits JSON-RPC messages from the client
3. Responses are streamed back over SSE (GET connection) or returned inline depending on negotiated capabilities
4. Inactive connections are detected and managed

### stdio Transport (Command-line Clients)

When running in stdio mode, the server:

1. Reads JSON-RPC messages from stdin (one per line)
2. Writes responses to stdout (one per line)
3. All console output for debugging is redirected to the log file
4. No UI/debug messages are written to stdout to maintain protocol integrity

## Log File Format

The log file contains structured log entries in the following format:

```
2025-03-20 15:02:23.809 [INF] MCP Server starting. UseStdio=true, DebugMode=true, LogPath=mcp-server.log {}
2025-03-20 15:02:23.827 [INF] Starting MCP server with stdio transport {}
2025-03-20 15:02:23.828 [DBG] Registered method: initialize {}
2025-03-20 15:02:23.828 [DBG] Registered method: tools/list {}
2025-03-20 15:02:23.828 [DBG] Registered method: tools/call {}
2025-03-20 15:02:23.828 [DBG] Default MCP methods registered {}
2025-03-20 15:02:23.828 [DBG] McpServer created with server info: example-server 1.0.0 {}
2025-03-20 15:02:23.844 [INF] Registered tool: add - Add two numbers {}
2025-03-20 15:02:23.844 [INF] Registered tool: divide - Divide two numbers {}
2025-03-20 15:02:23.844 [INF] Registered tool: sqrt - Calculate square root {}
2025-03-20 15:02:23.844 [INF] Registered tools from assembly {}
2025-03-20 15:02:23.845 [INF] MCP server connecting to transport {}
2025-03-20 15:02:23.845 [DBG] StdioServerTransport started {}
2025-03-20 15:02:23.845 [INF] Server connected to stdio transport {}
2025-03-20 15:02:23.845 [INF] MCP server running with stdio transport {}
```

When logging in debug mode, the logs include detailed information about message processing, JSON parsing, and other internal operations.

## Session Context

Log entries related to specific client sessions include a SessionId property, which allows for tracking and correlating log entries across a single client connection. In SSE mode, each client gets a unique session ID. In stdio mode, the session is identified as "stdio".

## Troubleshooting

If you encounter issues with the server:

1. Enable debug mode logging (`--debug` or `-d`) to see detailed information
2. Check for JSON parsing errors in the log file
3. For SSE transport, check the network tab in browser DevTools
4. For stdio transport, ensure you're sending well-formed JSON messages, one per line
5. Verify your configuration using the health check endpoints

### Common Issues

- **Invalid JSON message errors**: This is often caused by sending non-JSON strings to the server's stdin stream in stdio mode. Check that all messages are valid JSON-RPC 2.0 format.
- **Transport closed unexpectedly**: Check for errors in the log file.
- **Tool not found errors**: Ensure you're calling a registered tool.
- **Parameter validation errors**: Check the parameters against the tool's schema.
- **Connection refused errors**: Verify port/hostname configuration and network access.
- **"Connection has active SSE stream" warning**: Each client should only have one SSE connection.
- **Cloud platform deployment issues**: Ensure `PORT` environment variable is set and the server is binding to `0.0.0.0`.

## Implementation Details

### Logging System

The logging system is implemented using Serilog with the following features:

1. **Multiple sinks**: 
   - File sink (always enabled)
   - Console sink (only enabled in SSE mode)

2. **Log Levels**:
   - Debug: Detailed diagnostic information
   - Information: Normal operational events
   - Warning: Unusual but non-error events
   - Error: Error conditions and exceptions

3. **Contextual properties**:
   - SessionId: Used to group logs by client session
   - Request ID: For correlating related log entries
   - Tool name: For tracking tool invocations

4. **Special handling**:
   - No console output in stdio mode to maintain protocol integrity
   - Detailed error logging with exception information
   - Trimmed message content for invalid JSON messages

### Serilog Configuration

The Serilog configuration is kept simple but effective:

```csharp
var loggerConfig = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .MinimumLevel.Is(options.DebugMode ? LogEventLevel.Debug : LogEventLevel.Information);

// Always write to file
loggerConfig = loggerConfig.WriteTo.File(
    options.LogFilePath,
    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

// Only add console logging if not in stdio mode
if (!options.UseStdio)
{
    loggerConfig = loggerConfig.WriteTo.Console(
        outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}");
}
```

## Further Development

When extending the server or adding new tools:

1. Use the logging infrastructure for all console output
2. Always use `Logger.Debug()` for detailed diagnostic information
3. In stdio mode, never write directly to `Console.WriteLine`/`Console.Error.WriteLine`
4. Add appropriate logging to capture important lifecycle events

### Future Enhancements

Potential future enhancements to the logging system:

1. **Log rotation**: Automatically rotate log files by size or time
2. **Structured logging**: More use of structured properties vs. string templates
3. **Performance metrics**: Add timing information for processing requests
4. **Log filtering**: Add ability to filter logs by component or session

## Example Client Usage

See the TestClient project for examples of how to connect to and use the MCP server.
