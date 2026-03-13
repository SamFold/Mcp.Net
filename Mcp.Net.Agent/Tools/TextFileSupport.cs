using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mcp.Net.Agent.Tools;

internal sealed record ReadFileInspection(
    string Text,
    long SizeBytes,
    bool TruncatedByBytes,
    string ContentHash,
    string EncodingName,
    bool HasBom,
    string NewlineStyle
);

internal sealed class TextFileSnapshot
{
    private TextFileSnapshot(
        byte[] bytes,
        string text,
        string contentHash,
        TextEncodingInfo encodingInfo,
        string newlineStyle
    )
    {
        Bytes = bytes;
        Text = text;
        ContentHash = contentHash;
        EncodingInfo = encodingInfo;
        NewlineStyle = newlineStyle;
    }

    public byte[] Bytes { get; }

    public string Text { get; }

    public string ContentHash { get; }

    public TextEncodingInfo EncodingInfo { get; }

    public string NewlineStyle { get; }

    public long SizeBytes => Bytes.LongLength;

    public string EncodingName => EncodingInfo.Name;

    public bool HasBom => EncodingInfo.HasBom;

    public string? PreferredNewlineSequence =>
        NewlineStyle switch
        {
            "crlf" => "\r\n",
            "lf" => "\n",
            "cr" => "\r",
            _ => null,
        };

    public static async Task<TextFileSnapshot> LoadAsync(
        string fullPath,
        int maxBytes,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        using SafeFileHandle handle = File.OpenHandle(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.RandomAccess
        );

        var length = RandomAccess.GetLength(handle);
        if (length > maxBytes)
        {
            throw new InvalidOperationException(
                $"File '{Path.GetFileName(fullPath)}' is {length} bytes, which exceeds the editable limit of {maxBytes} bytes."
            );
        }

        var bytes = GC.AllocateUninitializedArray<byte>((int)length);
        var offset = 0L;
        while (offset < length)
        {
            var read = await RandomAccess.ReadAsync(
                handle,
                bytes.AsMemory((int)offset, (int)(length - offset)),
                offset,
                cancellationToken
            );

            if (read == 0)
            {
                throw new IOException($"Unexpected end of file while reading '{fullPath}'.");
            }

            offset += read;
        }

        var encodingInfo = TextFileUtilities.DetectEncoding(bytes);
        var text = TextFileUtilities.DecodeStrict(bytes, encodingInfo);
        if (text.AsSpan().IndexOf('\0') >= 0)
        {
            throw new InvalidOperationException(
                $"File '{Path.GetFileName(fullPath)}' is not a supported text file."
            );
        }

        return new TextFileSnapshot(
            bytes,
            text,
            TextFileUtilities.ComputeContentHash(bytes),
            encodingInfo,
            TextFileUtilities.GetNewlineStyleName(text)
        );
    }
}

internal sealed class NormalizedTextView
{
    private NormalizedTextView(string text, int[] originalIndexMap)
    {
        Text = text;
        OriginalIndexMap = originalIndexMap;
    }

    public string Text { get; }

    public int[] OriginalIndexMap { get; }

    public static NormalizedTextView Create(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalizedBuffer = GC.AllocateUninitializedArray<char>(text.Length);
        var indexMap = new int[text.Length + 1];
        var normalizedLength = 0;
        var originalIndex = 0;

        indexMap[0] = 0;

        while (originalIndex < text.Length)
        {
            var current = text[originalIndex];
            if (current == '\r')
            {
                normalizedBuffer[normalizedLength++] = '\n';
                originalIndex += originalIndex + 1 < text.Length && text[originalIndex + 1] == '\n'
                    ? 2
                    : 1;
                indexMap[normalizedLength] = originalIndex;
                continue;
            }

            normalizedBuffer[normalizedLength++] = current;
            originalIndex++;
            indexMap[normalizedLength] = originalIndex;
        }

        Array.Resize(ref indexMap, normalizedLength + 1);
        return new NormalizedTextView(new string(normalizedBuffer, 0, normalizedLength), indexMap);
    }
}

internal readonly record struct TextEncodingInfo(
    string Name,
    Encoding Encoding,
    bool HasBom,
    int BomLength,
    byte[] BomBytes
);

internal static class TextFileUtilities
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];
    private static readonly byte[] Utf32LeBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BeBom = [0x00, 0x00, 0xFE, 0xFF];

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding Utf16Le = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding Utf16Be = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UTF32Encoding Utf32Le = new(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
    private static readonly UTF32Encoding Utf32Be = new(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);

    public static async Task<ReadFileInspection> InspectForReadAsync(
        string fullPath,
        int previewByteLimit,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        var captureLimit = Math.Max(previewByteLimit + 1, Utf32LeBom.Length);
        var captureBuffer = new byte[captureLimit];
        var captured = 0;
        long totalRead = 0;

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                hasher.AppendData(buffer, 0, read);

                if (captured < captureBuffer.Length)
                {
                    var toCopy = Math.Min(captureBuffer.Length - captured, read);
                    buffer.AsSpan(0, toCopy).CopyTo(captureBuffer.AsSpan(captured));
                    captured += toCopy;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var displayByteCount = (int)Math.Min(totalRead, previewByteLimit);
        var encodingInfo = DetectEncoding(captureBuffer.AsSpan(0, captured));
        var decoded = DecodeForDisplay(captureBuffer.AsSpan(0, Math.Min(captured, displayByteCount)), encodingInfo);

        return new ReadFileInspection(
            decoded,
            totalRead,
            totalRead > previewByteLimit,
            $"sha256:{Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant()}",
            encodingInfo.Name,
            encodingInfo.HasBom,
            GetNewlineStyleName(decoded)
        );
    }

    public static async Task<string> ComputeContentHashAsync(
        string fullPath,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static string ComputeContentHash(ReadOnlySpan<byte> bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";

    public static TextEncodingInfo DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Utf32LeBom))
        {
            return new TextEncodingInfo("utf-32-le", Utf32Le, true, Utf32LeBom.Length, Utf32LeBom);
        }

        if (bytes.StartsWith(Utf32BeBom))
        {
            return new TextEncodingInfo("utf-32-be", Utf32Be, true, Utf32BeBom.Length, Utf32BeBom);
        }

        if (bytes.StartsWith(Utf8Bom))
        {
            return new TextEncodingInfo("utf-8", Utf8, true, Utf8Bom.Length, Utf8Bom);
        }

        if (bytes.StartsWith(Utf16LeBom))
        {
            return new TextEncodingInfo("utf-16-le", Utf16Le, true, Utf16LeBom.Length, Utf16LeBom);
        }

        if (bytes.StartsWith(Utf16BeBom))
        {
            return new TextEncodingInfo("utf-16-be", Utf16Be, true, Utf16BeBom.Length, Utf16BeBom);
        }

        return new TextEncodingInfo("utf-8", Utf8, false, 0, []);
    }

    public static string DecodeStrict(ReadOnlySpan<byte> bytes, TextEncodingInfo encodingInfo)
    {
        var contentBytes = bytes[encodingInfo.BomLength..];
        return encodingInfo.Encoding.GetString(contentBytes);
    }

    public static string DecodeForDisplay(ReadOnlySpan<byte> bytes, TextEncodingInfo encodingInfo)
    {
        if (bytes.Length <= encodingInfo.BomLength)
        {
            return string.Empty;
        }

        var contentBytes = bytes[encodingInfo.BomLength..];
        var decoder = encodingInfo.Encoding.GetDecoder();
        var charBuffer = ArrayPool<char>.Shared.Rent(encodingInfo.Encoding.GetMaxCharCount(contentBytes.Length));
        try
        {
            decoder.Convert(contentBytes, charBuffer, flush: false, out _, out var charsUsed, out _);
            return new string(charBuffer, 0, charsUsed);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    public static byte[] EncodeText(string text, TextEncodingInfo encodingInfo)
    {
        ArgumentNullException.ThrowIfNull(text);

        var textBytes = encodingInfo.Encoding.GetBytes(text);
        if (!encodingInfo.HasBom)
        {
            return textBytes;
        }

        var output = GC.AllocateUninitializedArray<byte>(encodingInfo.BomBytes.Length + textBytes.Length);
        encodingInfo.BomBytes.AsSpan().CopyTo(output);
        textBytes.AsSpan().CopyTo(output.AsSpan(encodingInfo.BomBytes.Length));
        return output;
    }

    public static string NormalizeLineEndings(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    public static string ConvertLineEndings(string text, string newlineSequence)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(newlineSequence);

        return NormalizeLineEndings(text).Replace("\n", newlineSequence, StringComparison.Ordinal);
    }

    public static string GetNewlineStyleName(ReadOnlySpan<char> text)
    {
        var sawLf = false;
        var sawCrLf = false;
        var sawCr = false;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    sawCrLf = true;
                    index++;
                }
                else
                {
                    sawCr = true;
                }
            }
            else if (text[index] == '\n')
            {
                sawLf = true;
            }

            if ((sawLf ? 1 : 0) + (sawCrLf ? 1 : 0) + (sawCr ? 1 : 0) > 1)
            {
                return "mixed";
            }
        }

        if (sawCrLf)
        {
            return "crlf";
        }

        if (sawLf)
        {
            return "lf";
        }

        if (sawCr)
        {
            return "cr";
        }

        return "none";
    }

    public static int GetLineNumber(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lineNumber = 1;
        for (var position = 0; position < index && position < text.Length; position++)
        {
            if (text[position] == '\r')
            {
                if (position + 1 < index && text[position + 1] == '\n')
                {
                    position++;
                }

                lineNumber++;
            }
            else if (text[position] == '\n')
            {
                lineNumber++;
            }
        }

        return lineNumber;
    }
}
