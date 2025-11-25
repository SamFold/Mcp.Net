using System;
using System.IO;
using System.Threading.Tasks;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Server.Transport.Stdio;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.SimpleServer;

/// <summary>
/// Stdio transport that logs outbound responses before sending.
/// </summary>
internal sealed class LoggingStdioTransport : StdioTransport
{
    private readonly Action<string> _log;

    public LoggingStdioTransport(
        string id,
        Stream input,
        Stream output,
        ILogger<StdioTransport> logger,
        Action<string> log
    )
        : base(id, input, output, logger)
    {
        _log = log;
    }

    public override async Task SendAsync(JsonRpcResponseMessage message)
    {
        try
        {
            // Serialize once for logging to avoid duplication in the base call.
            string json = SerializeMessage(message);
            _log($"Transport sending response: {json}");
        }
        catch
        {
            // Never fail the send because of logging.
        }

        await base.SendAsync(message);
    }
}
