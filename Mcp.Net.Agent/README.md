# Mcp.Net.Agent

Agent runtime for LLM-driven tool-use loops. Manages conversation state, routes tool calls to local or MCP-backed executors, and emits lifecycle events for consumers.

## Core loop

`ChatSession` coordinates the conversation cycle: accept user input, request an LLM completion, execute any tool calls, feed results back, repeat until the model stops requesting tools.

```csharp
var session = new ChatSession(
    chatClient,         // IChatClient from Mcp.Net.LLM
    toolExecutor,       // IToolExecutor — routes tool calls
    logger,
    new ChatSessionConfiguration
    {
        SystemPrompt = "You are a helpful assistant.",
        Tools = tools,
        RequestDefaults = new ChatRequestOptions { MaxOutputTokens = 8192 }
    }
);

var turn = await session.SendUserMessageAsync("What files are in this directory?");
Console.WriteLine($"{turn.Completion}: {turn.TurnId}");
```

## Tool execution

Tool calls are routed through `IToolExecutor`. Three implementations ship:

| Executor | Purpose |
|----------|---------|
| `McpToolExecutor` | Delegates to an MCP server via `IMcpClient.CallTool()` |
| `LocalToolExecutor` | Executes in-process `ILocalTool` implementations |
| `CompositeToolExecutor` | Routes to local executor if it has the tool, otherwise falls back to another executor |

```csharp
var localExecutor = new LocalToolExecutor(localTools);
var mcpExecutor = new McpToolExecutor(mcpClient, logger);
var executor = new CompositeToolExecutor(localExecutor, mcpExecutor);
```

`ChatSession` doesn't know which backend handles which tool. It validates against its own registered tool set and delegates to the executor.

### Local tools

Implement `ILocalTool` to add in-process tools:

```csharp
public interface ILocalTool
{
    Tool Descriptor { get; }
    Task<ToolInvocationResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default);
}
```

`ToolInvocation` includes result helpers so local tools do not need to call the raw `ToolInvocationResult` constructor directly:

```csharp
public Task<ToolInvocationResult> ExecuteAsync(
    ToolInvocation invocation,
    CancellationToken cancellationToken = default)
{
    return Task.FromResult(invocation.CreateTextResult("sunny"));
}
```

For typed local tools, derive from `LocalToolBase<TArgs>` so argument binding happens once in shared code instead of each tool parsing `invocation.Arguments` manually:

```csharp
public sealed record ReadFileArgs(string Path, int? MaxLines);

public sealed class ReadFileTool() : LocalToolBase<ReadFileArgs>("read_file", "Reads a file")
{
    protected override Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        ReadFileArgs arguments,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(invocation.CreateTextResult($"{arguments.Path}:{arguments.MaxLines}"));
    }
}
```

Merge local tool descriptors with MCP-discovered tools before session creation. The session sees a flat tool list regardless of backend.

## Events

Subscribe to session lifecycle through `IChatSessionEvents`:

| Event | When |
|-------|------|
| `TranscriptChanged` | Entry added or updated (includes streaming assistant updates) |
| `ActivityChanged` | Idle, waiting for provider, or executing tools |
| `ToolCallActivityChanged` | Per-tool lifecycle: queued, running, completed, failed, cancelled |

`SendUserMessageAsync(...)` and `ContinueAsync(...)` both return `ChatTurnSummary`, which gives awaited callers the turn ID plus added and updated transcript entries for that turn.

## Transcript

The conversation is stored as `IReadOnlyList<ChatTranscriptEntry>` with four sealed record types: `UserChatEntry`, `AssistantChatEntry`, `ToolResultChatEntry`, `ErrorChatEntry`. These are defined in `Mcp.Net.LLM` and shared across the provider boundary.

Transcript compaction runs automatically before each provider request via `IChatTranscriptCompactor`. The default implementation (`EntryCountChatTranscriptCompactor`) summarizes old entries when the transcript exceeds a configurable entry count.

## Dependencies

- `Mcp.Net.Core` — tool and content models
- `Mcp.Net.Client` — MCP client interface for remote tool execution
- `Mcp.Net.LLM` — provider abstraction and transcript types

## Status

The core loop, tool executor seam, cancellation flow, resume path, awaited turn summaries, and first built-in local filesystem tools (`list_files`, `read_file`) are in place. The next runtime slice is loop-safety hardening before broader file-discovery or write/shell tools land.
