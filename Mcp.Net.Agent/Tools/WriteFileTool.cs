using System.Text.Json;
using System.Text;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

public sealed class WriteFileTool : LocalToolBase<WriteFileTool.Arguments>
{
    private readonly FileSystemToolPolicy _policy;

    public WriteFileTool(FileSystemToolPolicy policy)
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
                return CreateErrorResult(
                    invocation,
                    reason: "missing_path",
                    message:
                        "The 'path' argument is required. Provide a single file path resolved from the configured base path."
                );
            }

            if (arguments.Content is null)
            {
                return CreateErrorResult(
                    invocation,
                    reason: "missing_content",
                    message:
                        "The 'content' argument is required. Provide the full text content to write."
                );
            }

            if (arguments.Content.AsSpan().IndexOf('\0') >= 0)
            {
                return CreateErrorResult(
                    invocation,
                    reason: "content_contains_null",
                    message:
                        "write_file only supports text content and does not accept null characters."
                );
            }

            var path = _policy.Resolve(arguments.Path);
            if (Directory.Exists(path.FullPath))
            {
                return CreateErrorResult(
                    invocation,
                    path.DisplayPath,
                    reason: "path_is_directory",
                    message: $"Path '{path.DisplayPath}' is a directory."
                );
            }

            var fileExists = File.Exists(path.FullPath);
            if (fileExists && !arguments.Overwrite)
            {
                return CreateErrorResult(
                    invocation,
                    path.DisplayPath,
                    reason: "already_exists",
                    message:
                        $"File '{path.DisplayPath}' already exists. Re-run with overwrite=true to replace it, or use edit_file for surgical updates."
                );
            }

            var parentDirectory = Path.GetDirectoryName(path.FullPath)
                ?? throw new InvalidOperationException(
                    $"File '{path.DisplayPath}' does not have a parent directory."
                );
            var createdDirectories = false;
            if (!Directory.Exists(parentDirectory))
            {
                if (!_policy.AllowCreateDirectories)
                {
                    return CreateErrorResult(
                        invocation,
                        path.DisplayPath,
                        reason: "missing_parent_directory",
                        message:
                            $"The parent directory for '{path.DisplayPath}' does not exist and policy does not allow creating directories automatically."
                    );
                }

                Directory.CreateDirectory(parentDirectory);
                createdDirectories = true;
            }

            if (
                !_policy.AllowMutationThroughReparsePoints
                && TryGetBlockedMutationPath(path.FullPath, fileExists, parentDirectory, out var blockedPath)
            )
            {
                var blockedDisplayPath = _policy.GetDisplayPath(blockedPath!);
                return CreateErrorResult(
                    invocation,
                    path.DisplayPath,
                    reason: "reparse_point_blocked",
                    message:
                        $"Path '{blockedDisplayPath}' is a reparse point. Mutation through reparse points is disabled by policy."
                );
            }

            var encodingInfo = TextFileUtilities.GetUtf8EncodingInfo();
            if (fileExists)
            {
                var snapshot = await TextFileSnapshot.LoadAsync(
                    path.FullPath,
                    _policy.MaxWritableBytes,
                    cancellationToken
                );
                encodingInfo = snapshot.EncodingInfo;
            }

            var bytes = TextFileUtilities.EncodeText(arguments.Content, encodingInfo);
            if (bytes.Length > _policy.MaxWritableBytes)
            {
                return CreateErrorResult(
                    invocation,
                    path.DisplayPath,
                    reason: "content_too_large",
                    message:
                        $"Writing '{path.DisplayPath}' would produce {bytes.Length} bytes, which exceeds the writable limit of {_policy.MaxWritableBytes} bytes."
                );
            }

            await AtomicFileCommitter.WriteFileAsync(
                path.FullPath,
                bytes,
                overwriteExisting: fileExists,
                cancellationToken
            );

            var newlineStyle = TextFileUtilities.GetNewlineStyleName(arguments.Content);
            var metadata = JsonSerializer.SerializeToElement(
                new
                {
                    path = path.DisplayPath,
                    created = !fileExists,
                    overwroteExisting = fileExists,
                    sizeBytes = bytes.LongLength,
                    encoding = encodingInfo.Name,
                    bom = encodingInfo.HasBom,
                    newlineStyle,
                    createdDirectories,
                }
            );

            return invocation.CreateResult(
                text:
                [
                    BuildSuccessText(
                        path.DisplayPath,
                        created: !fileExists,
                        sizeBytes: bytes.LongLength,
                        encodingName: encodingInfo.Name,
                        newlineStyle
                    ),
                ],
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
            return CreateErrorResult(invocation, reason: "write_failed", message: ex.Message);
        }
    }

    public sealed record Arguments(string Path, string Content, bool Overwrite = false);

    private static ToolInvocationResult CreateErrorResult(
        ToolInvocation invocation,
        string reason,
        string message
    ) => CreateErrorResult(invocation, path: null, reason, message);

    private static ToolInvocationResult CreateErrorResult(
        ToolInvocation invocation,
        string? path,
        string reason,
        string message
    )
    {
        var metadata = JsonSerializer.SerializeToElement(
            new
            {
                path,
                reason,
            }
        );
        return invocation.CreateResult(text: [message], metadata: metadata, isError: true);
    }

    private static string BuildSuccessText(
        string path,
        bool created,
        long sizeBytes,
        string encodingName,
        string newlineStyle
    )
    {
        var verb = created ? "Created" : "Overwrote";
        return $"{verb} {path} ({sizeBytes} bytes, {encodingName}, {newlineStyle}).";
    }

    private static bool TryGetBlockedMutationPath(
        string fullPath,
        bool targetExists,
        string parentDirectory,
        out string? blockedPath
    )
    {
        blockedPath = null;

        if (targetExists)
        {
            if (File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
            {
                blockedPath = fullPath;
                return true;
            }

            return false;
        }

        if (
            Directory.Exists(parentDirectory)
            && File.GetAttributes(parentDirectory).HasFlag(FileAttributes.ReparsePoint)
        )
        {
            blockedPath = parentDirectory;
            return true;
        }

        return false;
    }

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "write_file",
            Description =
                "Writes a text file inside the configured local filesystem scope. Creates a new file by default, automatically creates parent directories when policy allows it, and requires overwrite=true to replace an existing file.",
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
                                "Required. File path resolved from the configured base path. Use this to create a new text file or replace an existing one when overwrite=true.",
                        },
                        content = new
                        {
                            type = "string",
                            description =
                                "Required. Full text content to write to the file.",
                        },
                        overwrite = new
                        {
                            type = "boolean",
                            description =
                                "Optional. Set to true to replace an existing file. Defaults to false, which means the call fails if the file already exists.",
                        },
                    },
                    required = new[] { "path", "content" },
                    additionalProperties = false,
                }
            ),
        };
}
