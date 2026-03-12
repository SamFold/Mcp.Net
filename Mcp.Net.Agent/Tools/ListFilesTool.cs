using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

public sealed class ListFilesTool : LocalToolBase<ListFilesTool.Arguments>
{
    private readonly FileSystemToolPolicy _policy;

    public ListFilesTool(FileSystemToolPolicy policy)
        : base(CreateDescriptor())
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    protected override Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        Arguments arguments,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var requestedPath = string.IsNullOrWhiteSpace(arguments.Path) ? "." : arguments.Path;
            var path = _policy.Resolve(requestedPath);

            if (File.Exists(path.FullPath))
            {
                return Task.FromResult(
                    invocation.CreateErrorResult(
                        $"Path '{path.DisplayPath}' is not a directory."
                    )
                );
            }

            if (!Directory.Exists(path.FullPath))
            {
                return Task.FromResult(
                    invocation.CreateErrorResult(
                        $"Directory '{path.DisplayPath}' does not exist."
                    )
                );
            }

            var entries = new DirectoryInfo(path.FullPath)
                .EnumerateFileSystemInfos()
                .Select(info =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resolvedEntry = _policy.Resolve(info.FullName);
                    return new ListedEntry(
                        resolvedEntry.DisplayPath,
                        info is DirectoryInfo
                    );
                })
                .OrderBy(entry => entry.DisplayPath, StringComparer.Ordinal)
                .ToList();

            var visibleEntries = entries.Take(_policy.MaxDirectoryEntries).ToArray();
            var truncated = entries.Count > _policy.MaxDirectoryEntries;
            var text = string.Join(
                "\n",
                visibleEntries.Select(entry =>
                    entry.IsDirectory ? $"{entry.DisplayPath}/" : entry.DisplayPath
                )
            );
            var metadata = JsonSerializer.SerializeToElement(
                new
                {
                    path = path.DisplayPath,
                    truncated,
                    entryLimit = _policy.MaxDirectoryEntries,
                    totalEntryCount = entries.Count,
                }
            );

            return Task.FromResult(
                invocation.CreateResult(
                    text: new[] { text },
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

    private sealed record ListedEntry(string DisplayPath, bool IsDirectory);

    public sealed record Arguments(string? Path = null);

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "list_files",
            Description =
                "Lists files and directories from the bounded local filesystem. The optional path is relative to the local root and defaults to the current directory '.'.",
            InputSchema = JsonSerializer.SerializeToElement(
                new
                {
                    @type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description =
                                "Optional. Directory path relative to the local root. Defaults to '.'. Use this to inspect folders before calling read_file.",
                        },
                    },
                    required = Array.Empty<string>(),
                    additionalProperties = false,
                }
            ),
        };
}
