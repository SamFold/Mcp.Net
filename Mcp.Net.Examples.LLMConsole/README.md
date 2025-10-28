# Mcp.Net.Examples.LLMConsole

A console-based example application demonstrating how to integrate Mcp.Net with LLM providers (OpenAI and Anthropic) for tool calling.

## Features

- Interactive console-based chat UI
- Support for OpenAI (GPT-4o) and Anthropic (Claude) models
- Dynamic tool discovery and registration
- Tool selection interface
- Real-time tool execution visualization
- Event-based UI architecture

## Quick Start

### Prerequisites

1. You need an API key for either:
   - OpenAI: [Get one here](https://platform.openai.com/api-keys)
   - Anthropic: [Get one here](https://console.anthropic.com/)

2. Set the API key as an environment variable:
   ```bash
   # For Anthropic (default)
   export ANTHROPIC_API_KEY="your-api-key"
   
   # Or for OpenAI
   export OPENAI_API_KEY="your-api-key"
   ```

### Optional API Keys for External Tools

The LLM project also includes external tools that require additional API keys to function:

#### Google Search API

To use the `googleSearch/search` tool, you'll need:
- Google API Key 
- Custom Search Engine ID

Set these credentials as environment variables:
```bash
export GOOGLE_API_KEY="your-google-api-key"
export GOOGLE_SEARCH_ENGINE_ID="your-search-engine-id"
```

Without these environment variables, the tool will return an informative error message with instructions.

#### Twilio SMS API

To use the `twilioSms/sendSmsToUkNumber` tool, you'll need:
- Twilio Account SID
- Twilio Auth Token
- Twilio Phone Number

Set these credentials as environment variables:
```bash
export TWILIO_ACCOUNT_SID="your-account-sid"
export TWILIO_AUTH_TOKEN="your-auth-token"
export TWILIO_PHONE_NUMBER="your-twilio-phone-number"
```

Without these credentials, the tool will return an informative error message.

### Running the MCP Server

First, run the SimpleServer in a terminal window:

```bash
dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj -- --stdio
```

This launches the server in stdio mode (recommended when pairing with the console sample). For the OAuth demo endpoint instead, run:

```bash
dotnet run --project Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj
```

That variant hosts SSE on `http://localhost:5000/mcp` and requires OAuth (PKCE or client credentials).

### Running the LLM Client

In a new terminal window, run the LLM chat application:

```bash
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj
```

The client will:
1. Connect to the SimpleServer (SSE or stdio)
2. Present a tool selection interface
3. Start a chat session with your chosen LLM

### Command Line Options

```bash
# Use OpenAI instead of Claude (default)
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --provider=openai

# Specify a different model
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --model=gpt-4

# Connect to SSE with client-credentials auth (default)
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --url http://localhost:5000/mcp

# Connect to SSE with PKCE (dynamic registration)
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --url http://localhost:5000/mcp --pkce

# Connect to a stdio server process
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --command "dotnet run --project ../Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj -- --stdio"

# Disable authentication (for unsecured servers)
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --url http://localhost:5000/mcp --no-auth

# Skip the tool selection screen and enable all tools
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --all-tools

# Enable debug logging
dotnet run --project Mcp.Net.Examples.LLMConsole/Mcp.Net.Examples.LLMConsole.csproj --debug
```

## Architecture

This example application demonstrates proper usage of the Mcp.Net.LLM library:

- Event-based UI design separates the chat session logic from UI concerns
- Dependency injection for improved testability and flexibility
- Console-specific UI implementation using the core LLM library
- Proper event subscription and handling
- Asynchronous messaging with proper cancellation support
