using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

public sealed class EditFileTool : LocalToolBase<EditFileTool.Arguments>
{
    private static readonly JsonSerializerOptions BindingJsonOptions = CreateBindingJsonOptions();

    private readonly FileSystemToolPolicy _policy;

    public EditFileTool(FileSystemToolPolicy policy)
        : base(CreateDescriptor(), BindingJsonOptions)
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
                        "The 'path' argument is required. Provide a single file path relative to the local root."
                );
            }

            if (_policy.RequireExpectedContentHashForEdits && string.IsNullOrWhiteSpace(arguments.ExpectedContentHash))
            {
                return CreateErrorResult(
                    invocation,
                    reason: "missing_content_hash",
                    message:
                        "The 'expectedContentHash' argument is required. Call read_file first and pass through its contentHash metadata."
                );
            }

            if (arguments.Edits is null || arguments.Edits.Count == 0)
            {
                return CreateErrorResult(
                    invocation,
                    reason: "missing_edits",
                    message:
                        "The 'edits' argument must contain at least one edit. Each edit needs oldText and newText."
                );
            }

            if (arguments.Edits.Count > _policy.MaxEditsPerRequest)
            {
                return CreateErrorResult(
                    invocation,
                    reason: "too_many_edits",
                    message:
                        $"The request contains {arguments.Edits.Count} edits, which exceeds the policy limit of {_policy.MaxEditsPerRequest}."
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

            if (!File.Exists(path.FullPath))
            {
                return CreateErrorResult(
                    invocation,
                    path.DisplayPath,
                    reason: "missing_file",
                    message: $"File '{path.DisplayPath}' does not exist."
                );
            }

            if (
                !_policy.AllowMutationThroughReparsePoints
                && File.GetAttributes(path.FullPath).HasFlag(FileAttributes.ReparsePoint)
            )
            {
                return CreateErrorResult(
                    invocation,
                    path.DisplayPath,
                    reason: "reparse_point_blocked",
                    message:
                        $"File '{path.DisplayPath}' is a reparse point. Mutation through reparse points is disabled by policy."
                );
            }

            var snapshot = await TextFileSnapshot.LoadAsync(
                path.FullPath,
                _policy.MaxEditableBytes,
                cancellationToken
            );

            if (
                !string.IsNullOrWhiteSpace(arguments.ExpectedContentHash)
                && !string.Equals(
                    snapshot.ContentHash,
                    arguments.ExpectedContentHash,
                    StringComparison.Ordinal
                )
            )
            {
                return CreateErrorResult(
                    invocation,
                    path.DisplayPath,
                    reason: "content_hash_mismatch",
                    message:
                        $"File '{path.DisplayPath}' changed since it was read. Expected content hash '{arguments.ExpectedContentHash}', but the current file hash is '{snapshot.ContentHash}'."
                );
            }

            var plan = EditPlan.Create(snapshot, arguments.Edits, _policy.MaxDiffPreviewLines);
            var contentHashAfter = TextFileUtilities.ComputeContentHash(
                TextFileUtilities.EncodeText(plan.UpdatedText, snapshot.EncodingInfo)
            );

            if (!arguments.DryRun)
            {
                var currentHash = await TextFileUtilities.ComputeContentHashAsync(
                    path.FullPath,
                    cancellationToken
                );
                if (!string.Equals(currentHash, snapshot.ContentHash, StringComparison.Ordinal))
                {
                    return CreateErrorResult(
                        invocation,
                        path.DisplayPath,
                        reason: "content_hash_mismatch",
                        message:
                            $"File '{path.DisplayPath}' changed while the edit was being prepared. Re-read the file and retry."
                    );
                }

                var bytes = TextFileUtilities.EncodeText(plan.UpdatedText, snapshot.EncodingInfo);
                await AtomicFileCommitter.ReplaceFileAsync(path.FullPath, bytes, cancellationToken);
                contentHashAfter = TextFileUtilities.ComputeContentHash(bytes);
            }

            var metadata = JsonSerializer.SerializeToElement(
                new
                {
                    path = path.DisplayPath,
                    dryRun = arguments.DryRun,
                    appliedEditCount = arguments.Edits.Count,
                    contentHashBefore = snapshot.ContentHash,
                    contentHashAfter,
                    encoding = snapshot.EncodingName,
                    bom = snapshot.HasBom,
                    newlineStyle = snapshot.NewlineStyle,
                    firstChangedLine = plan.FirstChangedLine,
                    usedNormalizedLineEndingMatch = plan.UsedNormalizedLineEndingMatch,
                    diffPreview = plan.DiffPreview,
                    diffTruncated = plan.DiffTruncated,
                }
            );

            return invocation.CreateResult(
                text: [BuildSuccessText(path.DisplayPath, plan, arguments.DryRun)],
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
            return CreateErrorResult(invocation, reason: "edit_failed", message: ex.Message);
        }
    }

    public sealed record Arguments(
        string Path,
        string ExpectedContentHash,
        IReadOnlyList<TextEdit> Edits,
        bool DryRun = false
    );

    public sealed record TextEdit(
        string OldText,
        string NewText,
        EditMatchMode MatchMode = EditMatchMode.NormalizeLineEndings
    );

    public enum EditMatchMode
    {
        Exact,
        NormalizeLineEndings,
    }

    private static JsonSerializerOptions CreateBindingJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

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

    private static string BuildSuccessText(string path, EditPlan plan, bool dryRun)
    {
        var verb = dryRun ? "Previewed" : "Applied";
        if (string.IsNullOrWhiteSpace(plan.DiffPreview))
        {
            return $"{verb} {plan.EditCount} edit(s) to {path}.";
        }

        return $"{verb} {plan.EditCount} edit(s) to {path}.\n\n{plan.DiffPreview}";
    }

    private static Tool CreateDescriptor() =>
        new()
        {
            Name = "edit_file",
            Description =
                "Edits an existing text file inside the bounded local filesystem using optimistic concurrency. Call read_file first, pass its contentHash as expectedContentHash, and provide one or more exact oldText/newText replacements.",
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
                                "Required. Existing file path relative to the local root. This tool edits files in place and does not create new files.",
                        },
                        expectedContentHash = new
                        {
                            type = "string",
                            minLength = 1,
                            description =
                                "Required. The contentHash returned by read_file for optimistic concurrency. The edit fails if the file changed since it was read.",
                        },
                        edits = new
                        {
                            type = "array",
                            minItems = 1,
                            description =
                                "Required. One or more edits planned against the original file snapshot. Each oldText must match exactly once.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    oldText = new
                                    {
                                        type = "string",
                                        minLength = 1,
                                        description =
                                            "Required. Exact text to replace from the original file snapshot.",
                                    },
                                    newText = new
                                    {
                                        type = "string",
                                        description =
                                            "Required. Replacement text. Use an empty string to delete the matched text.",
                                    },
                                    matchMode = new
                                    {
                                        type = "string",
                                        description =
                                            "Optional. Match strategy: 'exact' or 'normalizeLineEndings'. Defaults to 'normalizeLineEndings'.",
                                        @enum = new[] { "exact", "normalizeLineEndings" },
                                    },
                                },
                                required = new[] { "oldText", "newText" },
                                additionalProperties = false,
                            },
                        },
                        dryRun = new
                        {
                            type = "boolean",
                            description =
                                "Optional. When true, validate and preview the edit without writing the file.",
                        },
                    },
                    required = new[] { "path", "expectedContentHash", "edits" },
                    additionalProperties = false,
                }
            ),
        };

    private sealed class EditPlan
    {
        private EditPlan(
            string updatedText,
            string diffPreview,
            bool diffTruncated,
            int firstChangedLine,
            bool usedNormalizedLineEndingMatch,
            int editCount
        )
        {
            UpdatedText = updatedText;
            DiffPreview = diffPreview;
            DiffTruncated = diffTruncated;
            FirstChangedLine = firstChangedLine;
            UsedNormalizedLineEndingMatch = usedNormalizedLineEndingMatch;
            EditCount = editCount;
        }

        public string UpdatedText { get; }

        public string DiffPreview { get; }

        public bool DiffTruncated { get; }

        public int FirstChangedLine { get; }

        public bool UsedNormalizedLineEndingMatch { get; }

        public int EditCount { get; }

        public static EditPlan Create(
            TextFileSnapshot snapshot,
            IReadOnlyList<TextEdit> edits,
            int maxDiffPreviewLines
        )
        {
            var normalizedView = default(NormalizedTextView);
            var plannedEdits = new List<PlannedEdit>(edits.Count);
            for (var index = 0; index < edits.Count; index++)
            {
                plannedEdits.Add(PlanEdit(snapshot, edits[index], ref normalizedView));
            }

            var orderedEdits = plannedEdits.OrderBy(edit => edit.Start).ToArray();
            for (var index = 1; index < orderedEdits.Length; index++)
            {
                if (orderedEdits[index].Start < orderedEdits[index - 1].End)
                {
                    throw new InvalidOperationException(
                        "The requested edits overlap. Split the change into non-overlapping edits planned against the original file snapshot."
                    );
                }
            }

            var updatedText = ApplyEdits(snapshot.Text, orderedEdits);
            var diffPreview = DiffPreviewBuilder.Build(
                snapshot.Text,
                updatedText,
                maxDiffPreviewLines
            );

            return new EditPlan(
                updatedText,
                diffPreview.Preview,
                diffPreview.Truncated,
                TextFileUtilities.GetLineNumber(snapshot.Text, orderedEdits[0].Start),
                orderedEdits.Any(edit => edit.UsedNormalizedLineEndingMatch),
                orderedEdits.Length
            );
        }

        private static PlannedEdit PlanEdit(
            TextFileSnapshot snapshot,
            TextEdit edit,
            ref NormalizedTextView? normalizedView
        )
        {
            ArgumentNullException.ThrowIfNull(edit);

            if (string.IsNullOrEmpty(edit.OldText))
            {
                throw new InvalidOperationException(
                    "Each edit requires a non-empty oldText value."
                );
            }

            if (string.Equals(edit.OldText, edit.NewText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Each edit must change the file. oldText and newText were identical."
                );
            }

            var exactIndex = FindSingleOccurrence(snapshot.Text, edit.OldText);
            if (exactIndex >= 0)
            {
                return new PlannedEdit(
                    exactIndex,
                    exactIndex + edit.OldText.Length,
                    edit.NewText,
                    false
                );
            }

            if (edit.MatchMode == EditMatchMode.Exact)
            {
                throw new InvalidOperationException(
                    "Could not find oldText in the file using exact matching."
                );
            }

            normalizedView ??= NormalizedTextView.Create(snapshot.Text);
            var normalizedOldText = TextFileUtilities.NormalizeLineEndings(edit.OldText);
            var normalizedIndex = FindSingleOccurrence(normalizedView.Text, normalizedOldText);
            if (normalizedIndex < 0)
            {
                throw new InvalidOperationException(
                    "Could not find oldText in the file. Try re-reading the file and provide more surrounding context."
                );
            }

            var start = normalizedView.OriginalIndexMap[normalizedIndex];
            var end = normalizedView.OriginalIndexMap[normalizedIndex + normalizedOldText.Length];
            var replacement = snapshot.PreferredNewlineSequence is { } newline
                ? TextFileUtilities.ConvertLineEndings(edit.NewText, newline)
                : edit.NewText;

            return new PlannedEdit(start, end, replacement, true);
        }

        private static int FindSingleOccurrence(string haystack, string needle)
        {
            var firstIndex = haystack.IndexOf(needle, StringComparison.Ordinal);
            if (firstIndex < 0)
            {
                return -1;
            }

            if (haystack.IndexOf(needle, firstIndex + 1, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "oldText matched multiple locations in the file. Provide more surrounding context so the match is unique."
                );
            }

            return firstIndex;
        }

        private static string ApplyEdits(string originalText, PlannedEdit[] orderedEdits)
        {
            var updatedLength = originalText.Length;
            foreach (var edit in orderedEdits)
            {
                updatedLength = checked(updatedLength + edit.Replacement.Length - (edit.End - edit.Start));
            }

            return string.Create(
                updatedLength,
                (OriginalText: originalText, Edits: orderedEdits),
                static (destination, state) =>
                {
                    var destinationIndex = 0;
                    var originalIndex = 0;

                    foreach (var edit in state.Edits)
                    {
                        state.OriginalText
                            .AsSpan(originalIndex, edit.Start - originalIndex)
                            .CopyTo(destination[destinationIndex..]);
                        destinationIndex += edit.Start - originalIndex;

                        edit.Replacement.AsSpan().CopyTo(destination[destinationIndex..]);
                        destinationIndex += edit.Replacement.Length;
                        originalIndex = edit.End;
                    }

                    state.OriginalText.AsSpan(originalIndex).CopyTo(destination[destinationIndex..]);
                }
            );
        }
    }

    private readonly record struct PlannedEdit(
        int Start,
        int End,
        string Replacement,
        bool UsedNormalizedLineEndingMatch
    );

    private static class DiffPreviewBuilder
    {
        public static DiffPreviewResult Build(string originalText, string updatedText, int maxLines)
        {
            var originalLines = SplitLines(originalText);
            var updatedLines = SplitLines(updatedText);
            var prefix = 0;
            while (
                prefix < originalLines.Length
                && prefix < updatedLines.Length
                && string.Equals(originalLines[prefix], updatedLines[prefix], StringComparison.Ordinal)
            )
            {
                prefix++;
            }

            var suffix = 0;
            while (
                suffix < originalLines.Length - prefix
                && suffix < updatedLines.Length - prefix
                && string.Equals(
                    originalLines[originalLines.Length - 1 - suffix],
                    updatedLines[updatedLines.Length - 1 - suffix],
                    StringComparison.Ordinal
                )
            )
            {
                suffix++;
            }

            var oldStart = prefix;
            var newStart = prefix;
            var oldChangedCount = originalLines.Length - prefix - suffix;
            var newChangedCount = updatedLines.Length - prefix - suffix;
            var previewLines = new List<string>
            {
                $"@@ -{oldStart + 1},{Math.Max(oldChangedCount, 0)} +{newStart + 1},{Math.Max(newChangedCount, 0)} @@",
            };

            foreach (var line in originalLines.Skip(oldStart).Take(oldChangedCount))
            {
                previewLines.Add($"-{line}");
            }

            foreach (var line in updatedLines.Skip(newStart).Take(newChangedCount))
            {
                previewLines.Add($"+{line}");
            }

            var truncated = false;
            if (previewLines.Count > maxLines)
            {
                previewLines = previewLines.Take(maxLines).ToList();
                previewLines.Add("...");
                truncated = true;
            }

            return new DiffPreviewResult(string.Join("\n", previewLines), truncated);
        }

        private static string[] SplitLines(string text) =>
            TextFileUtilities.NormalizeLineEndings(text).Split('\n');
    }

    private readonly record struct DiffPreviewResult(string Preview, bool Truncated);

    private static class AtomicFileCommitter
    {
        public static async Task ReplaceFileAsync(
            string fullPath,
            byte[] bytes,
            CancellationToken cancellationToken
        )
        {
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException(
                    $"File '{fullPath}' does not have a parent directory."
                );
            var tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():n}.tmp"
            );

            try
            {
                using (var handle = File.OpenHandle(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    FileOptions.Asynchronous | FileOptions.RandomAccess,
                    preallocationSize: bytes.Length
                ))
                {
                    await RandomAccess.WriteAsync(handle, bytes, 0, cancellationToken);
                    RandomAccess.FlushToDisk(handle);
                }

                try
                {
                    File.Replace(tempPath, fullPath, destinationBackupFileName: null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(tempPath, fullPath, overwrite: true);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
