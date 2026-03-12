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
                    sizeBytes = new FileInfo(path.FullPath).Length,
                    truncated = readResult.Truncated,
                    truncatedByBytes = readResult.TruncatedByBytes,
                    truncatedByLines = readResult.TruncatedByLines,
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
        var buffer = new byte[_policy.MaxReadBytes + 1];
        var totalRead = 0;

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken
            );
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        var truncatedByBytes = totalRead > _policy.MaxReadBytes;
        var decoded = DecodeText(buffer, Math.Min(totalRead, _policy.MaxReadBytes));
        var limited = ApplyLineLimit(decoded);

        return new ReadFileResult(
            limited.Text,
            truncatedByBytes || limited.TruncatedByLines,
            truncatedByBytes,
            limited.TruncatedByLines
        );
    }

    private static string DecodeText(byte[] buffer, int count)
    {
        using var memory = new MemoryStream(buffer, 0, count, writable: false);
        using var reader = new StreamReader(
            memory,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );

        return reader.ReadToEnd();
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
        bool Truncated,
        bool TruncatedByBytes,
        bool TruncatedByLines
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
