# Mcp.Net Web UI Example

This project demonstrates how to create a web-based interface for the Mcp.Net LLM chat application. It provides a RESTful API and real-time communication through SignalR to enable web clients to interact with the chat system.

## Features

- Web API endpoints for chat session management
- Real-time communication using SignalR
- Multiple concurrent chat sessions
- Tool selection and execution
- Support for both Anthropic and OpenAI LLM providers
- Event-based architecture for UI updates

## Prerequisites

- .NET 8.0 SDK or later
- Running instance of Mcp.Net.Examples.SimpleServer
- API key for Anthropic or OpenAI

## Getting Started

### 1. Set Environment Variables

```bash
# For Anthropic
export ANTHROPIC_API_KEY=your-api-key

# For OpenAI
export OPENAI_API_KEY=your-api-key

# Optional - set provider (defaults to Anthropic)
export LLM_PROVIDER=anthropic # or openai

# Optional - set model (defaults to claude-sonnet-4-5-20250929 for Anthropic, gpt-5 for OpenAI)
export LLM_MODEL=your-model-name
```

### 2. Start the MCP Server

```bash
dotnet run --project ../Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj
```

This launches the server with its demo OAuth 2.1 endpoints enabled. Leave it running while you start the Web UI.

### 3. Start the Web UI Server

```bash
dotnet run
```

The server will start on http://localhost:5231 by default.

> 💡 On first launch the Web UI automatically registers itself with the demo OAuth server and completes the PKCE flow. You’ll see log entries confirming the registration and token acquisition.

## Configuration

Authentication is controlled through the `McpServer` section in `appsettings.json` (or environment variables):

```json
"McpServer": {
  "Url": "http://localhost:5000/",
  "AuthMode": "Pkce",          // or "ClientCredentials" / "None"
  "Pkce": {
    "ClientName": "WebUI PKCE Client",
    "UseDynamicRegistration": true
  },
  "ClientCredentials": {
    "ClientId": "demo-client",
    "ClientSecret": "demo-client-secret"
  }
}
```

- `AuthMode=Pkce` dynamically registers a public client and performs the authorization-code flow, matching the LLMConsole sample.
- Switch to `ClientCredentials` if you prefer the seeded confidential client, or `None` to talk to an anonymous MCP server (e.g., when SimpleServer runs with `--no-auth`).

## API Endpoints

- `POST /api/chat/sessions` - Create a new chat session
- `DELETE /api/chat/sessions/{sessionId}` - End a chat session
- `POST /api/chat/sessions/{sessionId}/messages` - Send a message to a chat session
- `GET /api/tools` - Get all available tools
- `GET /api/tools/enabled` - Get all enabled tools
- `POST /api/tools/enabled` - Enable specific tools by name

## SignalR Hub

The SignalR hub is available at `/chatHub` and provides the following methods:

### Client to Server
- `JoinSession(string sessionId)` - Join a specific chat session
- `LeaveSession(string sessionId)` - Leave a specific chat session
- `SendMessage(string sessionId, string message)` - Send a message to a specific chat session
- `CreateSession()` - Create a new chat session (returns sessionId)

### Server to Client
- `SessionStarted(string sessionId)` - Notifies when a session has started
- `ReceiveMessage(ChatMessageDto message)` - Delivers new messages
- `ToolExecutionUpdated(ToolExecutionDto toolExecution)` - Notifies about tool execution updates
- `ThinkingStateChanged(bool isThinking, string context)` - Notifies when thinking state changes
- `ReceiveError(string errorMessage)` - Delivers error messages

## Architecture Overview

This application uses a clean architecture with separation of concerns:

- **Controllers** - Handle HTTP requests and responses
- **Hubs** - Handle real-time communication via SignalR
- **Services** - Implement business logic and manage resources
- **DTOs** - Transfer data between layers

The core chat logic from the console application is reused through interfaces, with web-specific implementations for user input and UI updates.

## Frontend Development

To create a frontend for this API, you'll need to:

1. Set up a React/Vue/Angular application
2. Install SignalR client library (@microsoft/signalr)
3. Implement connection management
4. Create UI components for chat interface

A basic example of connecting to the SignalR hub:

```javascript
import * as signalR from '@microsoft/signalr';

const connection = new signalR.HubConnectionBuilder()
  .withUrl('http://localhost:5231/chatHub')
  .build();

connection.on('ReceiveMessage', (message) => {
  console.log('Message received:', message);
});

connection.start()
  .then(() => console.log('Connected!'))
  .catch(err => console.error('Connection failed: ', err));
```
