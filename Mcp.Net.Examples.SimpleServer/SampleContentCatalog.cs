using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.Server;
using Mcp.Net.Server.Completions;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.SimpleServer;

/// <summary>
/// Seeds the demo server with sample resources and prompts so integration runs exercise the full MCP surface.
/// </summary>
internal static class SampleContentCatalog
{
    private static readonly Resource GettingStartedResource = new()
    {
        Uri = "mcp://docs/simple-server/getting-started",
        Name = "Simple Server: Getting Started",
        Description = "Overview of the sample MCP server and how to interact with it.",
        MimeType = "text/markdown",
    };

    private const string GettingStartedMarkdown =
        """
        # Welcome to the Simple MCP Server

        This server demonstrates the 2025-06-18 Model Context Protocol features:

        - Unified `/mcp` endpoint serving both JSON-RPC POSTs and SSE streams
        - OAuth 2.1 bearer authentication with dynamic client registration + PKCE
        - Rich tool catalogue drawn from the calculator and Warhammer 40k samples
        - Sample prompts and resources for integration/regression testing

        ## Try it out

        1. `dotnet run --project Mcp.Net.Examples.SimpleServer`
        2. `dotnet run --project Mcp.Net.Examples.SimpleClient -- --auth-mode pkce`
        3. Inspect the logs to see dynamic registration, prompt usage, and resource reads.
        """;

    private static readonly Resource OAuthResource = new()
    {
        Uri = "mcp://docs/simple-server/oauth-flow",
        Name = "OAuth Flow Walkthrough",
        Description = "Describes the demo OAuth server, the seeded client, and the PKCE workflow.",
        MimeType = "text/markdown",
    };

    private const string OAuthMarkdown =
        """
        # Demo OAuth 2.1 Flow

        The demo server runs a lightweight OAuth issuer that supports both client credentials and authorization code + PKCE:

        - `demo-client` / `demo-client-secret` remain available for confidential flows and backwards compatibility.
        - Public clients can POST to `/oauth/register` to obtain an ephemeral client identifier.
        - The authorization endpoint enforces the S256 code challenge and the `resource` indicator (`http://localhost:5000/mcp`).
        - Tokens are JWTs signed with an in-memory key and scoped to the MCP resource.

        The sample SimpleClient registers itself on startup, acquires a token, then replays requests with the negotiated session headers.
        """;

    private static readonly Prompt SummarizeResourcePrompt = new()
    {
        Name = "summarize-resource",
        Title = "Summarize a Resource",
        Description = "Produces a concise summary of a selected MCP resource.",
    };

    private static readonly Prompt DraftEmailPrompt = new()
    {
        Name = "draft-follow-up-email",
        Title = "Draft a Follow-up Email",
        Description = "Creates a polite follow-up email referencing the outcome of a tool invocation.",
        Arguments = new[]
        {
            new PromptArgument
            {
                Name = "recipient",
                Description = "Who should receive the email?",
                Required = true,
            },
            new PromptArgument
            {
                Name = "context",
                Description = "Key details or results the email should mention.",
                Required = false,
            },
        },
    };

    private static readonly string[] DraftEmailRecipientSuggestions =
    {
        "engineering@mcp.example",
        "product@mcp.example",
        "security@mcp.example",
        "support@mcp.example",
    };

    private static readonly string[] DraftEmailContextSuggestions =
    {
        "Summarise the calculator tool results and next steps.",
        "Include findings from the Warhammer inquisitor elicitation.",
        "Highlight outstanding OAuth configuration items.",
    };

    public static void Register(McpServer server, ILogger? logger = null)
    {
        if (server == null)
        {
            throw new ArgumentNullException(nameof(server));
        }

        server.RegisterResource(
            GettingStartedResource,
            _ => Task.FromResult(CloneContents(new[]
            {
                new ResourceContent
                {
                    Uri = GettingStartedResource.Uri,
                    MimeType = GettingStartedResource.MimeType,
                    Text = GettingStartedMarkdown,
                },
            })),
            overwrite: true
        );

        server.RegisterResource(
            OAuthResource,
            _ => Task.FromResult(CloneContents(new[]
            {
                new ResourceContent
                {
                    Uri = OAuthResource.Uri,
                    MimeType = OAuthResource.MimeType,
                    Text = OAuthMarkdown,
                },
            })),
            overwrite: true
        );

        server.RegisterPrompt(
            SummarizeResourcePrompt,
            _ => Task.FromResult(CreateSummarizePromptMessages()),
            overwrite: true
        );

        server.RegisterPrompt(
            DraftEmailPrompt,
            _ => Task.FromResult(CreateEmailPromptMessages()),
            overwrite: true
        );

        server.RegisterPromptCompletion(
            DraftEmailPrompt.Name,
            (context, _) => Task.FromResult(BuildDraftEmailCompletions(context)),
            overwrite: true
        );

        logger?.LogInformation(
            "Seeded {ResourceCount} demo resources and {PromptCount} prompts.",
            2,
            2
        );
    }

    private static CompletionValues BuildDraftEmailCompletions(CompletionRequestContext context)
    {
        var argumentName = context.Parameters.Argument?.Name ?? string.Empty;
        var currentValue = context.Parameters.Argument?.Value ?? string.Empty;

        return argumentName switch
        {
            "recipient" => BuildCompletionValues(DraftEmailRecipientSuggestions, currentValue),
            "context" => BuildCompletionValues(DraftEmailContextSuggestions, currentValue),
            _ => new CompletionValues
            {
                Values = Array.Empty<string>(),
                Total = 0,
                HasMore = false,
            },
        };
    }

    private static CompletionValues BuildCompletionValues(IEnumerable<string> options, string prefix)
    {
        var normalizedPrefix = prefix?.Trim() ?? string.Empty;
        var matches = options
            .Where(option =>
                string.IsNullOrEmpty(normalizedPrefix)
                || option.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
            )
            .Take(100)
            .ToArray();

        return new CompletionValues
        {
            Values = matches,
            Total = matches.Length,
            HasMore = false,
        };
    }

    private static object[] CreateSummarizePromptMessages()
    {
        return new object[]
        {
            new
            {
                role = "system",
                content = new ContentBase[]
                {
                    new TextContent
                    {
                        Text = "You are a helpful assistant that produces concise resource summaries highlighting the most actionable information."
                    },
                },
            },
            new
            {
                role = "user",
                content = new ContentBase[]
                {
                    new TextContent
                    {
                        Text = "Summarize the selected resource, emphasising next steps for a developer integrating with the server."
                    },
                },
            },
        };
    }

    private static object[] CreateEmailPromptMessages()
    {
        return new object[]
        {
            new
            {
                role = "system",
                content = new ContentBase[]
                {
                    new TextContent
                    {
                        Text = "Draft professional, friendly emails that clearly communicate progress and outstanding actions."
                    },
                },
            },
            new
            {
                role = "user",
                content = new ContentBase[]
                {
                    new TextContent
                    {
                        Text = "Compose a follow-up email to {{recipient}} summarising the latest MCP tool results and proposed next steps. Include the provided context if available: {{context}}."
                    },
                },
            },
        };
    }

    private static ResourceContent[] CloneContents(ResourceContent[] source)
    {
        var result = new ResourceContent[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var item = source[i];
            result[i] = new ResourceContent
            {
                Uri = item.Uri,
                MimeType = item.MimeType,
                Text = item.Text,
                Blob = item.Blob,
            };
        }

        return result;
    }
}
