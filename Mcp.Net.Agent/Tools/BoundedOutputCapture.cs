using System.Text;

namespace Mcp.Net.Agent.Tools;

internal sealed class BoundedOutputCapture
{
    private readonly object _gate = new();
    private readonly List<CapturedLine> _headLines = [];
    private readonly Queue<CapturedLine> _tailLines = new();
    private readonly int _maxOutputBytes;
    private readonly int _maxOutputLines;
    private readonly int _headLineLimit;
    private readonly int _tailLineLimit;
    private readonly int _headByteLimit;
    private readonly int _tailByteLimit;

    private int _headBytes;
    private int _tailBytes;
    private bool _headFrozen;

    public BoundedOutputCapture(int maxOutputBytes, int maxOutputLines)
    {
        if (maxOutputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));
        }

        if (maxOutputLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputLines));
        }

        _maxOutputBytes = maxOutputBytes;
        _maxOutputLines = maxOutputLines;
        _headLineLimit = Math.Max(1, maxOutputLines / 2);
        _tailLineLimit = Math.Max(0, maxOutputLines - _headLineLimit);
        _headByteLimit = Math.Max(1, maxOutputBytes / 2);
        _tailByteLimit = Math.Max(1, maxOutputBytes - _headByteLimit);
    }

    public int TotalLines { get; private set; }

    public long TotalBytes { get; private set; }

    public bool TruncatedByLines => TotalLines > _maxOutputLines;

    public bool TruncatedByBytes => TotalBytes > _maxOutputBytes;

    public void Append(string line)
    {
        var text = line ?? string.Empty;
        var byteCount = Encoding.UTF8.GetByteCount(text) + 1;

        lock (_gate)
        {
            TotalLines++;
            TotalBytes += byteCount;

            if (!_headFrozen)
            {
                if (
                    _headLines.Count == 0
                    || (
                        _headLines.Count < _headLineLimit
                        && _headBytes + byteCount <= _headByteLimit
                    )
                )
                {
                    _headLines.Add(new CapturedLine(text, byteCount));
                    _headBytes += byteCount;
                    return;
                }

                _headFrozen = true;
            }

            AppendTail(text, byteCount);
        }
    }

    public BoundedOutputCaptureResult Build()
    {
        lock (_gate)
        {
            var headLines = _headLines.Select(line => line.Text).ToList();
            var tailLines = _tailLines.Select(line => line.Text).ToList();
            var truncated = TruncatedByLines || TruncatedByBytes;

            if (!truncated)
            {
                var allLines = new List<string>(headLines.Count + tailLines.Count);
                allLines.AddRange(headLines);
                allLines.AddRange(tailLines);
                return new BoundedOutputCaptureResult(
                    string.Join('\n', allLines),
                    truncated,
                    TruncatedByLines,
                    TruncatedByBytes,
                    TotalLines
                );
            }

            var omittedLineCount = Math.Max(0, TotalLines - headLines.Count - tailLines.Count);
            var marker = omittedLineCount > 0
                ? $"[... {omittedLineCount} lines truncated ...]"
                : "[... output truncated ...]";

            var renderedLines = new List<string>(headLines.Count + tailLines.Count + 1);
            renderedLines.AddRange(headLines);
            renderedLines.Add(marker);
            renderedLines.AddRange(tailLines);

            return new BoundedOutputCaptureResult(
                string.Join('\n', renderedLines),
                truncated,
                TruncatedByLines,
                TruncatedByBytes,
                TotalLines
            );
        }
    }

    private void AppendTail(string text, int byteCount)
    {
        if (_tailLineLimit == 0)
        {
            return;
        }

        _tailLines.Enqueue(new CapturedLine(text, byteCount));
        _tailBytes += byteCount;

        while (_tailLines.Count > 1 && (_tailLines.Count > _tailLineLimit || _tailBytes > _tailByteLimit))
        {
            var removedLine = _tailLines.Dequeue();
            _tailBytes -= removedLine.ByteCount;
        }
    }

    private readonly record struct CapturedLine(string Text, int ByteCount);
}

internal sealed record BoundedOutputCaptureResult(
    string CombinedOutput,
    bool Truncated,
    bool TruncatedByLines,
    bool TruncatedByBytes,
    int TotalLines
);
