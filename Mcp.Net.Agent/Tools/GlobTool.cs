using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

public sealed class GlobTool : LocalToolBase<GlobTool.Arguments>
{
    private readonly FileSystemToolPolicy _policy;
    private readonly GlobSearch _search;

    public GlobTool(FileSystemToolPolicy policy)
        : base(CreateDescriptor())
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _search = new GlobSearch(policy);
    }

    protected override Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        Arguments arguments,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments.Pattern))
            {
                return Task.FromResult(
                    invocation.CreateErrorResult(
                        "The 'pattern' argument is required. Provide a single glob pattern relative to the configured base path, for example '**/*.cs' or 'docs/*.md'."
                    )
                );
            }

            if (arguments.Limit is <= 0)
            {
                return Task.FromResult(
                    invocation.CreateErrorResult(
                        "The 'limit' argument must be greater than zero when provided."
                    )
                );
            }

            var basePath = _policy.ResolveOrBase(arguments.Path);

            if (File.Exists(basePath.FullPath))
            {
                return Task.FromResult(
                    invocation.CreateErrorResult($"Path '{basePath.DisplayPath}' is not a directory.")
                );
            }

            if (!Directory.Exists(basePath.FullPath))
            {
                return Task.FromResult(
                    invocation.CreateErrorResult(
                        $"Directory '{basePath.DisplayPath}' does not exist."
                    )
                );
            }

            var pattern = GlobPattern.Parse(arguments.Pattern);
            var limit = Math.Min(arguments.Limit ?? _policy.MaxGlobMatches, _policy.MaxGlobMatches);
            var result = _search.Search(basePath, pattern, limit, cancellationToken);
            var metadata = JsonSerializer.SerializeToElement(
                new
                {
                    path = basePath.DisplayPath,
                    pattern = pattern.OriginalPattern,
                    searchRoot = result.SearchRootDisplayPath,
                    returnedCount = result.Paths.Length,
                    limit,
                    truncated = result.Truncated,
                    directoriesVisited = result.DirectoriesVisited,
                }
            );

            return Task.FromResult(
                invocation.CreateResult(
                    text: new[] { string.Join("\n", result.Paths) },
                    metadata: metadata
                )
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
            return Task.FromResult(invocation.CreateErrorResult(ex.Message));
        }
    }

    public sealed record Arguments(string Pattern, string? Path = null, int? Limit = null);

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "glob_files",
            Description =
                "Finds files in the configured local filesystem scope using a glob pattern. Matches are deterministic, capped by a result limit, and display relative paths when they stay under the configured base path.",
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
                                "Required. Glob pattern relative to the configured base path or the optional path, for example '**/*.cs', 'src/**/*.ts', or '*.md'. This tool matches files, not directories.",
                        },
                        path = new
                        {
                            type = "string",
                            description =
                                "Optional. Directory path resolved from the configured base path to search within. Defaults to '.'.",
                        },
                        limit = new
                        {
                            type = "integer",
                            minimum = 1,
                            description =
                                "Optional. Maximum number of matches to return. Defaults to the policy limit and is always clamped to that bound.",
                        },
                    },
                    required = new[] { "pattern" },
                    additionalProperties = false,
                }
            ),
        };
}
