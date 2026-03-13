using System.Text;
using System.Text.Json;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

public sealed class ReadFileTool : LocalToolBase<ReadFileTool.Arguments>
{
    private readonly FileSystemToolPolicy _policy;

    public ReadFileTool(FileSystemToolPolicy policy)
        : base(CreateDescriptor())
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    protected override async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        Arguments arguments,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments.Path))
            {
                return invocation.CreateErrorResult(
                    "The 'path' argument is required. Provide a single file path relative to the local root, for example 'README.md'. If you need a path, call list_files first."
                );
            }

            var path = _policy.Resolve(arguments.Path);

            if (Directory.Exists(path.FullPath))
            {
                return invocation.CreateErrorResult(
                    $"Path '{path.DisplayPath}' is a directory."
                );
            }

            if (!File.Exists(path.FullPath))
            {
                return invocation.CreateErrorResult(
                    $"File '{path.DisplayPath}' does not exist."
                );
            }

            var readResult = await ReadFileAsync(path.FullPath, cancellationToken);
            var metadata = JsonSerializer.SerializeToElement(
                new
                {
                    path = path.DisplayPath,
                    sizeBytes = readResult.SizeBytes,
                    truncated = readResult.Truncated,
                    truncatedByBytes = readResult.TruncatedByBytes,
                    truncatedByLines = readResult.TruncatedByLines,
                    contentHash = readResult.ContentHash,
                    encoding = readResult.Encoding,
                    bom = readResult.HasBom,
                    newlineStyle = readResult.NewlineStyle,
                    byteLimit = _policy.MaxReadBytes,
                    lineLimit = _policy.MaxReadLines,
                }
            );

            return invocation.CreateResult(
                text: new[] { readResult.Text },
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
                        or DecoderFallbackException
            )
        {
            return invocation.CreateErrorResult(ex.Message);
        }
    }

    private async Task<ReadFileResult> ReadFileAsync(
        string fullPath,
        CancellationToken cancellationToken
    )
    {
        var inspection = await TextFileUtilities.InspectForReadAsync(
            fullPath,
            _policy.MaxReadBytes,
            cancellationToken
        );
        var limited = ApplyLineLimit(inspection.Text);

        return new ReadFileResult(
            limited.Text,
            inspection.SizeBytes,
            inspection.TruncatedByBytes || limited.TruncatedByLines,
            inspection.TruncatedByBytes,
            limited.TruncatedByLines,
            inspection.ContentHash,
            inspection.EncodingName,
            inspection.HasBom,
            inspection.NewlineStyle
        );
    }

    private LineLimitedText ApplyLineLimit(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        using var reader = new StringReader(normalized);
        var lines = new List<string>();

        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                return new LineLimitedText(string.Join("\n", lines), false);
            }

            if (lines.Count == _policy.MaxReadLines)
            {
                return new LineLimitedText(string.Join("\n", lines), true);
            }

            lines.Add(line);
        }
    }

    private sealed record LineLimitedText(string Text, bool TruncatedByLines);

    private sealed record ReadFileResult(
        string Text,
        long SizeBytes,
        bool Truncated,
        bool TruncatedByBytes,
        bool TruncatedByLines,
        string ContentHash,
        string Encoding,
        bool HasBom,
        string NewlineStyle
    );

    public sealed record Arguments(string Path);

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "read_file",
            Description =
                "Reads a text file from the bounded local filesystem. Requires a single file path relative to the local root. Use list_files first if you need to discover a path.",
            InputSchema = JsonSerializer.SerializeToElement(
                new
                {
                    @type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            minLength = 1,
                            description =
                                "Required. File path relative to the local root, for example 'README.md' or 'docs/vnext/agent.md'. Do not pass a directory path.",
                        },
                    },
                    required = new[] { "path" },
                    additionalProperties = false,
                }
            ),
        };
}
