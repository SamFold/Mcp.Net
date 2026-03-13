using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

public sealed record GrepToolOptions(string? RipgrepPath = null);

public sealed class GrepTool : LocalToolBase<GrepTool.Arguments>
{
    private readonly FileSystemToolPolicy _policy;
    private readonly RipgrepSearch _search;

    private GrepTool(FileSystemToolPolicy policy, string ripgrepPath)
        : base(CreateDescriptor())
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _search = new RipgrepSearch(policy, ripgrepPath);
    }

    public static bool TryCreate(
        FileSystemToolPolicy policy,
        out GrepTool? tool,
        out string? unavailableReason,
        GrepToolOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (
            !RipgrepCommandResolver.TryResolve(
                options?.RipgrepPath,
                out var ripgrepPath,
                out unavailableReason
            )
        )
        {
            tool = null;
            return false;
        }

        tool = new GrepTool(policy, ripgrepPath!);
        return true;
    }

    protected override async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        Arguments arguments,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments.Pattern))
            {
                return invocation.CreateErrorResult(
                    "The 'pattern' argument is required. Provide a literal string or regular expression to search for within the bounded local filesystem."
                );
            }

            if (arguments.Limit is <= 0)
            {
                return invocation.CreateErrorResult(
                    "The 'limit' argument must be greater than zero when provided."
                );
            }

            if (arguments.ContextLines < 0)
            {
                return invocation.CreateErrorResult(
                    "The 'contextLines' argument must be zero or greater."
                );
            }

            var requestedPath = string.IsNullOrWhiteSpace(arguments.Path) ? "." : arguments.Path;
            var basePath = _policy.Resolve(requestedPath);

            if (!Directory.Exists(basePath.FullPath) && !File.Exists(basePath.FullPath))
            {
                return invocation.CreateErrorResult(
                    $"Path '{basePath.DisplayPath}' does not exist."
                );
            }

            var limit = Math.Min(arguments.Limit ?? _policy.MaxGrepMatches, _policy.MaxGrepMatches);
            var contextLines = Math.Min(arguments.ContextLines, _policy.MaxGrepContextLines);
            var request = new GrepSearchRequest(
                Pattern: arguments.Pattern,
                Glob: string.IsNullOrWhiteSpace(arguments.Glob) ? null : arguments.Glob,
                Literal: arguments.Literal,
                IgnoreCase: arguments.IgnoreCase,
                Word: arguments.Word,
                ContextLines: contextLines,
                Limit: limit
            );
            var result = await _search.SearchAsync(basePath, request, cancellationToken);
            var metadata = JsonSerializer.SerializeToElement(
                new
                {
                    path = basePath.DisplayPath,
                    pattern = arguments.Pattern,
                    glob = request.Glob,
                    literal = request.Literal,
                    ignoreCase = request.IgnoreCase,
                    word = request.Word,
                    contextLines = contextLines,
                    limit,
                    filesSearched = result.FilesSearched,
                    filesMatched = result.FilesMatched,
                    matchCount = result.MatchCount,
                    truncatedByMatches = result.TruncatedByMatches,
                    truncatedByBytes = result.TruncatedByBytes,
                    linesTruncated = result.LinesTruncated,
                    engine = "ripgrep",
                }
            );

            return invocation.CreateResult(
                text: [result.FormattedOutput],
                metadata: metadata
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return invocation.CreateErrorResult(ex.Message);
        }
    }

    public sealed record Arguments(
        string Pattern,
        string? Path = null,
        string? Glob = null,
        bool Literal = true,
        bool IgnoreCase = false,
        bool Word = false,
        int ContextLines = 0,
        int? Limit = null
    );

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "grep_files",
            Description =
                "Searches file contents within the bounded local filesystem using ripgrep when available on the host. Returns deterministic root-relative matches with line numbers and bounded output. Use glob_files to narrow by name and read_file to inspect full file contents.",
            InputSchema = JsonSerializer.SerializeToElement(
                new
                {
                    @type = "object",
                    properties = new
                    {
                        pattern = new
                        {
                            type = "string",
                            minLength = 1,
                            description =
                                "Required. Search pattern to match in file contents. Literal search is the default; set literal=false to use ripgrep regular-expression semantics.",
                        },
                        path = new
                        {
                            type = "string",
                            description =
                                "Optional. File or directory path relative to the local root to search within. Defaults to '.'.",
                        },
                        glob = new
                        {
                            type = "string",
                            description =
                                "Optional. Additional ripgrep glob filter for candidate files, for example '*.cs' or '**/*.ts'.",
                        },
                        literal = new
                        {
                            type = "boolean",
                            description =
                                "Optional. Treat pattern as a fixed string. Defaults to true.",
                        },
                        ignoreCase = new
                        {
                            type = "boolean",
                            description =
                                "Optional. Perform case-insensitive matching. Defaults to false.",
                        },
                        word = new
                        {
                            type = "boolean",
                            description =
                                "Optional. Restrict matches to word boundaries. Defaults to false.",
                        },
                        contextLines = new
                        {
                            type = "integer",
                            minimum = 0,
                            description =
                                "Optional. Number of context lines to show before and after each match. Defaults to 0 and is clamped to the policy limit.",
                        },
                        limit = new
                        {
                            type = "integer",
                            minimum = 1,
                            description =
                                "Optional. Maximum number of matching lines to return. Defaults to the policy limit and is always clamped to that bound.",
                        },
                    },
                    required = new[] { "pattern" },
                    additionalProperties = false,
                }
            ),
        };
}
