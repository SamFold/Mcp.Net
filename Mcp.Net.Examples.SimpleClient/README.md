# SimpleClient Sample

The SimpleClient project demonstrates how to connect to an MCP server using the `Mcp.Net.Client`
SDK. It showcases both transports (SSE and stdio), OAuth flows, resource/prompt discovery, and
now the elicitation and completion features introduced in the 2025-06-18 specification.

## Running the Sample

1. Start the sample server in one terminal:

   ```bash
   dotnet run --project ../Mcp.Net.Examples.SimpleServer/Mcp.Net.Examples.SimpleServer.csproj
   ```

2. In another terminal, launch the client:

   ```bash
   # SSE transport with dynamic registration + PKCE
   dotnet run -- --url http://localhost:5000 --auth-mode pkce

   # or stdio transport (starts the server as a child process)
   dotnet run -- --command "dotnet run --project ../Mcp.Net.Examples.SimpleServer -- --stdio"
   ```

The first connection performs capability negotiation, lists prompts/resources, and exercises the
calculator and Warhammer tools. During the Warhammer demo you will be prompted with an elicitation
request, and the completions section demonstrates how prompt arguments receive suggestions.

## Elicitation Walkthrough

When the server calls `elicitation/create`, the client surfaces a console wizard:

```
=== Elicitation Request ===
Customize the inquisitor's profile or leave fields empty to keep the generated values.

Choose action ([A]ccept / [D]ecline / [C]ancel):
```

* `Accept` walks through each schema property, validating types (integers, booleans, enums, etc.).
  Leaving a field blank keeps the server-generated value.
* `Decline` completes the prompt without sending overrides.
* `Cancel` aborts the request (the server will treat it as a user cancellation).

The handler lives in `Elicitation/ConsoleElicitationHandler.cs` and demonstrates:

- Reading the incoming `ElicitationRequestContext` (message, schema, raw JSON).
- Prompting for each schema-defined field.
- Validating numeric ranges, enum selections, and booleans.
- Returning `ElicitationClientResponse.Accept/Decline/Cancel`.

Both SSE and stdio demos register the handler via `McpClientBuilder.WithElicitationHandler(...)`
or `client.SetElicitationHandler(...)`. The client only advertises the `elicitation` capability
when a handler is registered, so call one of these before `Initialize`.

## Completion Demo

After prompts are listed the sample requests suggestions for the
`draft-follow-up-email` prompt:

- Typing `eng` for the `recipient` argument returns team aliases seeded by the server
- Providing the recipient and requesting completions for `context` surfaces helpful phrasing hints

Both SSE and stdio demos use the new `IMcpClient.CompleteAsync` helper. The client only calls
`completion/complete` when the server advertises the capability negotiated during initialization.

## Authentication Modes

* `--auth-mode pkce` (default) uses demo OAuth configuration with dynamic registration.
* `--auth-mode client` performs a client-credentials exchange.
* `--auth-mode none` disables bearer tokens.

See `Authorization` folder for the PKCE helper logic and `Mcp.Net.Examples.Shared` for shared
constants.

## Next Steps

* Swap `ConsoleElicitationHandler` with your UI implementation (WinUI, WPF, web).
* Use the builder helpers in your own clients:

  ```csharp
  var client = new McpClientBuilder()
      .UseSseTransport(serverUrl)
      .WithElicitationHandler(async (ctx, ct) =>
      {
          // Render your UI here
          return ElicitationClientResponse.Decline();
      })
      .Build();
  await client.Initialize();
  ```

* Explore the server project to see how tools request elicitation (`Warhammer40kTools`).
