using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Interfaces;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Factories;

public sealed class ChatSessionFactory : IChatSessionFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public ChatSessionFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public ChatSession Create(
        IChatClient chatClient,
        IToolExecutor toolExecutor,
        ChatSessionConfiguration configuration,
        IChatTranscriptCompactor? transcriptCompactor = null
    )
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(toolExecutor);
        ArgumentNullException.ThrowIfNull(configuration);

        return new ChatSession(
            chatClient,
            toolExecutor,
            _loggerFactory.CreateLogger<ChatSession>(),
            configuration,
            transcriptCompactor
        );
    }

    public async Task<ChatSession> CreateAsync(
        IChatClient chatClient,
        ChatSessionFactoryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var localTools = NormalizeLocalTools(options.LocalTools);
        var localDescriptors = localTools.Select(tool => tool.Descriptor).ToArray();

        Tool[] remoteDescriptors = Array.Empty<Tool>();
        if (options.McpClient != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            remoteDescriptors = await options.McpClient.ListTools();
            cancellationToken.ThrowIfCancellationRequested();
        }

        ValidateDuplicateToolNames(localDescriptors, remoteDescriptors);

        var configuration = new ChatSessionConfiguration
        {
            SystemPrompt = options.SystemPrompt,
            Tools = localDescriptors.Concat(remoteDescriptors).ToArray(),
            RequestDefaults = options.RequestDefaults,
            MaxToolCallRounds = options.MaxToolCallRounds,
        };

        return Create(
            chatClient,
            CreateToolExecutor(localTools, options.McpClient),
            configuration,
            options.TranscriptCompactor
        );
    }

    private static IReadOnlyList<ILocalTool> NormalizeLocalTools(IReadOnlyList<ILocalTool>? localTools)
    {
        if (localTools == null || localTools.Count == 0)
        {
            return Array.Empty<ILocalTool>();
        }

        var normalized = new ILocalTool[localTools.Count];
        for (var index = 0; index < localTools.Count; index++)
        {
            normalized[index] = localTools[index]
                ?? throw new ArgumentNullException(nameof(localTools), "Local tools cannot contain null entries.");
        }

        return normalized;
    }

    private IToolExecutor CreateToolExecutor(
        IReadOnlyList<ILocalTool> localTools,
        IMcpClient? mcpClient
    )
    {
        var hasLocalTools = localTools.Count > 0;
        var hasMcpClient = mcpClient != null;

        if (!hasLocalTools && !hasMcpClient)
        {
            return new NoOpToolExecutor();
        }

        if (hasLocalTools && !hasMcpClient)
        {
            return new LocalToolExecutor(localTools);
        }

        var mcpExecutor = new McpToolExecutor(
            mcpClient!,
            _loggerFactory.CreateLogger<McpToolExecutor>()
        );

        if (!hasLocalTools)
        {
            return mcpExecutor;
        }

        return new CompositeToolExecutor(new LocalToolExecutor(localTools), mcpExecutor);
    }

    private static void ValidateDuplicateToolNames(
        IReadOnlyList<Tool> localDescriptors,
        IReadOnlyList<Tool> remoteDescriptors
    )
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in localDescriptors)
        {
            if (!seen.Add(tool.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate tool name '{tool.Name}' was configured for this session."
                );
            }
        }

        foreach (var tool in remoteDescriptors)
        {
            if (!seen.Add(tool.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate tool name '{tool.Name}' was configured for this session."
                );
            }
        }
    }
}
