using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

public enum LogEncodingKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Windows1252
}

public readonly record struct DetectedEncodingInfo(LogEncodingKind Kind, Encoding Encoding, long DataOffset);

public static class LogEncodingDetector
{
    public static DetectedEncodingInfo DetectEncoding(string path)
    {
        using FileStream fs = VisualRowReader.OpenSourceStream(path);
        return VisualRowReader.DetectEncoding(fs);
    }
}

internal enum RealLineStartKind
{
    TrueBreak,
    SearchLimit
}

internal enum VisualStartKind
{
    RealStart,
    ForcedWrap
}

public sealed class VisualRowReader : IViewportReader
{
    public const int VisibleSegmentChars = 4096;
    internal const int SegmentChars = VisibleSegmentChars;
    internal const int BackSearchSegments = 100;
    internal const int BackSearchLimitChars = SegmentChars * BackSearchSegments;
    private const int ReverseScanBlockBytes = 64 * 1024;
    private const int Utf8SearchLimitProbeBytes = 8;
    private const int Utf16SearchLimitProbeBytes = 8;

    internal sealed record ViewportRow(
        long StartOffset,
        long EndOffset,
        long RealLineStartOffset,
        RealLineStartKind RealLineStartKind,
        VisualStartKind VisualStartKind,
        int SegmentIndex,
        long SegmentStartOffset,
        long SegmentEndOffset,
        string Text);

    private readonly record struct RealLineStartInfo(long StartOffset, RealLineStartKind Kind);

    private readonly record struct VisualPosition(
        long StartOffset,
        long RealLineStartOffset,
        RealLineStartKind RealLineStartKind,
        VisualStartKind VisualStartKind,
        int SegmentIndex);

    private readonly record struct VisualReadResult(ViewportRow Row, VisualPosition? NextPosition);

    private readonly string _filePath;
    private readonly LogEncodingKind _kind;
    private readonly Encoding _encoding;
    private readonly long _dataOffset;
    private readonly long _fileSize;
    private readonly List<ViewportRow> _viewportRows = new();
    private long _topOffset;
    private long _viewportEndOffset;
    private int _viewportVisibleLines;
    private bool _viewportLoaded;

    private VisualRowReader(string filePath, LogEncodingKind kind, Encoding encoding, long dataOffset, long fileSize)
    {
        _filePath = filePath;
        _kind = kind;
        _encoding = encoding;
        _dataOffset = dataOffset;
        _fileSize = fileSize;
        _topOffset = dataOffset;
        _viewportEndOffset = dataOffset;
    }

    public VisualRowReader(string filePath, Encoding encoding, long dataOffset)
        : this(
            Path.GetFullPath(filePath),
            InferKind(encoding, dataOffset),
            encoding,
            dataOffset,
            new FileInfo(Path.GetFullPath(filePath)).Length)
    {
    }

    public string FilePath => _filePath;
    public long DataOffset => _dataOffset;
    public LogEncodingKind Kind => _kind;
    public Encoding Encoding => _encoding;
    public string EncodingName => _kind switch
    {
        LogEncodingKind.Utf8 => "UTF-8 BOM",
        LogEncodingKind.Utf16Le => "UTF-16 LE BOM",
        LogEncodingKind.Utf16Be => "UTF-16 BE BOM",
        _ => "Windows-1252"
    };
    public long FileSize => _fileSize;
    public long TopOffset => _topOffset;
    public long ViewportEndOffset => _viewportEndOffset;
    public long ViewportBytes => _viewportEndOffset >= _topOffset ? _viewportEndOffset - _topOffset : 0;
    public double ScrollPercentage
    {
        get
        {
            if (!HasContent)
            {
                return 0d;
            }

            long contentBytes = Math.Max(1, _fileSize - _dataOffset);
            long topBytes = Math.Clamp(_topOffset - _dataOffset, 0, contentBytes);
            return (topBytes * 100d) / contentBytes;
        }
    }
    public bool HasContent => _fileSize > _dataOffset;
    public IReadOnlyList<string> CurrentRows
    {
        get
        {
            string[] rows = new string[_viewportRows.Count];
            for (int i = 0; i < _viewportRows.Count; i++)
            {
                rows[i] = _viewportRows[i].Text;
            }

            return rows;
        }
    }

    internal IReadOnlyList<ViewportRow> ViewportRows => _viewportRows;

    public IReadOnlyList<string> ReadNext(int count)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        if (!_viewportLoaded || _viewportRows.Count == 0)
        {
            EnsureViewport(count);
        }
        else
        {
            ScrollByLinesForWorker(count, _viewportVisibleLines);
        }

        return CurrentRows;
    }

    public IReadOnlyList<string> ReadPrevious(int count)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        if (!_viewportLoaded || _viewportRows.Count == 0)
        {
            EnsureViewport(count);
        }
        else if (count == 1)
        {
            ScrollByLinesForWorker(-count, _viewportVisibleLines);
        }
        else
        {
            ScrollUpByVisualRows(count, _viewportVisibleLines);
        }

        return CurrentRows;
    }

    public IReadOnlyList<string> ReadFromPercentage(double percentage, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        double clamped = Math.Clamp(percentage, 0d, 100d);
        if (!HasContent)
        {
            _viewportVisibleLines = Math.Max(1, count);
            _viewportRows.Clear();
            _topOffset = _dataOffset;
            _viewportEndOffset = _dataOffset;
            _viewportLoaded = true;
            return Array.Empty<string>();
        }

        if (clamped >= 100d)
        {
            ScrollEnd(count);
            return CurrentRows;
        }

        long contentBytes = Math.Max(1, _fileSize - _dataOffset);
        long requestedOffset = _dataOffset + (long)((clamped / 100d) * contentBytes);
        ScrollToApproximateOffset(requestedOffset, count);
        if (_viewportRows.Count < count && _viewportEndOffset >= _fileSize)
        {
            ScrollEnd(count);
        }

        return CurrentRows;
    }

    private bool EnsureViewport(int visibleLines)
    {
        if (_viewportLoaded && _viewportRows.Count > 0)
        {
            return LoadViewportAt(ToVisualPosition(_viewportRows[0]), visibleLines);
        }

        return LoadViewportAt(DefaultTopPosition(), visibleLines);
    }

    private bool ScrollLineDown(int visibleLines)
    {
        if (!EnsureViewport(visibleLines) || _viewportRows.Count == 0)
        {
            return false;
        }

        if (_viewportRows.Count > 1)
        {
            return LoadViewportAt(ToVisualPosition(_viewportRows[1]), visibleLines);
        }

        if (_viewportEndOffset >= _fileSize)
        {
            return false;
        }

        return LoadViewportAt(_viewportEndOffset, visibleLines);
    }

    private bool ScrollLineUp(int visibleLines)
    {
        if (!HasContent || !EnsureViewport(visibleLines) || _viewportRows.Count == 0)
        {
            return false;
        }

        if (!TryGetPreviousVisualPosition(ToVisualPosition(_viewportRows[0]), out VisualPosition previous))
        {
            return false;
        }

        return LoadViewportAt(previous, visibleLines);
    }

    private bool ScrollUpByVisualRows(int rowsToMove, int visibleLines)
    {
        if (rowsToMove <= 0)
        {
            return EnsureViewport(visibleLines);
        }

        if (!HasContent || !EnsureViewport(visibleLines) || _viewportRows.Count == 0)
        {
            return false;
        }

        using FileStream fs = OpenSourceStream(_filePath);
        VisualPosition current = NormalizePreviousNavigationStart(ToVisualPosition(_viewportRows[0]));
        int remainingRows = rowsToMove;

        if (current.VisualStartKind == VisualStartKind.ForcedWrap && current.SegmentIndex > 0)
        {
            int stepsWithinLine = Math.Min(remainingRows, current.SegmentIndex);
            current = GetVisualPositionForSegment(fs, current.RealLineStartOffset, current.RealLineStartKind, current.SegmentIndex - stepsWithinLine);
            remainingRows -= stepsWithinLine;
            if (remainingRows == 0)
            {
                return LoadViewportAt(current, visibleLines);
            }
        }

        List<RealLineStartInfo> previousLines = CollectPreviousRealLineStarts(fs, current.RealLineStartOffset, remainingRows);
        if (previousLines.Count == 0)
        {
            return false;
        }

        foreach (RealLineStartInfo realLineStart in previousLines)
        {
            VisualPosition lastSegment = LocateLastVisualPositionOfRealLine(fs, realLineStart);
            int segmentCount = lastSegment.SegmentIndex + 1;
            if (remainingRows <= segmentCount)
            {
                int targetSegmentIndex = segmentCount - remainingRows;
                VisualPosition targetTop = GetVisualPositionForSegment(fs, realLineStart.StartOffset, realLineStart.Kind, targetSegmentIndex);
                return LoadViewportAt(targetTop, visibleLines);
            }

            remainingRows -= segmentCount;
        }

        RealLineStartInfo earliest = previousLines[^1];
        return LoadViewportAt(new VisualPosition(
            earliest.StartOffset,
            earliest.StartOffset,
            earliest.Kind,
            VisualStartKind.RealStart,
            0), visibleLines);
    }

    private bool ScrollPageDown(int visibleLines)
    {
        if (!EnsureViewport(visibleLines) || _viewportRows.Count == 0)
        {
            return false;
        }

        if (_viewportEndOffset >= _fileSize)
        {
            return false;
        }

        return LoadViewportAt(_viewportEndOffset, visibleLines);
    }

    private bool ScrollPageUp(int visibleLines)
    {
        return ScrollUpByVisualRows(Math.Max(1, visibleLines), visibleLines);
    }

    private bool ScrollHome(int visibleLines) => LoadViewportAt(DefaultTopPosition(), visibleLines);

    private bool ScrollEnd(int visibleLines)
    {
        if (!HasContent)
        {
            return LoadViewportAt(DefaultTopPosition(), visibleLines);
        }

        VisualPosition nextTop = LocateVisualPositionForOffset(TrimTrailingBreaks());
        int steps = Math.Max(0, visibleLines - 1);
        for (int i = 0; i < steps; i++)
        {
            if (!TryGetPreviousVisualPosition(nextTop, out VisualPosition previous))
            {
                break;
            }

            nextTop = previous;
        }

        return LoadViewportAt(nextTop, visibleLines);
    }

    private bool ScrollToApproximateOffset(long requestedOffset, int visibleLines) =>
        LoadViewportAt(LocateVisualPositionForOffset(requestedOffset), visibleLines);

    public VisualRowReader CloneForWorker()
    {
        VisualRowReader clone = new(_filePath, _kind, _encoding, _dataOffset, _fileSize)
        {
            _topOffset = _topOffset,
            _viewportEndOffset = _viewportEndOffset,
            _viewportVisibleLines = _viewportVisibleLines,
            _viewportLoaded = _viewportLoaded
        };
        clone._viewportRows.AddRange(_viewportRows);
        return clone;
    }

    IViewportReader IViewportReader.CloneForWorker() => CloneForWorker();

    internal bool ScrollByLinesForWorker(int deltaLines, int visibleLines)
    {
        if (deltaLines == 0)
        {
            return EnsureViewport(visibleLines);
        }

        int steps = Math.Abs(deltaLines);
        for (int i = 0; i < steps; i++)
        {
            bool moved = deltaLines >= 0 ? ScrollLineDown(visibleLines) : ScrollLineUp(visibleLines);
            if (!moved)
            {
                break;
            }
        }

        return true;
    }

    public void Dispose()
    {
    }

    internal static FileStream OpenSourceStream(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
    }

    internal static DetectedEncodingInfo DetectEncoding(FileStream fs)
    {
        fs.Position = 0;
        Span<byte> sample = stackalloc byte[3];
        int read = fs.Read(sample);
        fs.Position = 0;

        if (read >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            return new(LogEncodingKind.Utf8, Encoding.UTF8, 3);
        }

        if (read >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
        {
            return new(LogEncodingKind.Utf16Le, Encoding.Unicode, 2);
        }

        if (read >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
        {
            return new(LogEncodingKind.Utf16Be, Encoding.BigEndianUnicode, 2);
        }

        return new(LogEncodingKind.Windows1252, Windows1252Encoding.Instance, 0);
    }

    internal static LogEncodingKind InferKind(Encoding encoding, long dataOffset)
    {
        if (dataOffset == 3 && string.Equals(encoding.WebName, Encoding.UTF8.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return LogEncodingKind.Utf8;
        }

        if (dataOffset == 2 && string.Equals(encoding.WebName, Encoding.Unicode.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return LogEncodingKind.Utf16Le;
        }

        if (dataOffset == 2 && string.Equals(encoding.WebName, Encoding.BigEndianUnicode.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return LogEncodingKind.Utf16Be;
        }

        return LogEncodingKind.Windows1252;
    }

    private static string DescribeEncoding(LogEncodingKind kind) => kind switch
    {
        LogEncodingKind.Utf8 => "UTF-8 BOM",
        LogEncodingKind.Utf16Le => "UTF-16 LE BOM",
        LogEncodingKind.Utf16Be => "UTF-16 BE BOM",
        _ => "Windows-1252"
    };

    private int CodeUnitSize => _kind is LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be ? 2 : 1;

    private static bool IsLineBreakUnit(ushort unit) => unit is 0x000D or 0x000A;

    private VisualPosition DefaultTopPosition() =>
        new(_dataOffset, _dataOffset, RealLineStartKind.TrueBreak, VisualStartKind.RealStart, 0);

    private static VisualPosition ToVisualPosition(ViewportRow row) =>
        new(row.StartOffset, row.RealLineStartOffset, row.RealLineStartKind, row.VisualStartKind, row.SegmentIndex);

    private long AlignCodeUnitOffset(long offset)
    {
        if (CodeUnitSize == 1 || offset <= _dataOffset)
        {
            return offset <= _dataOffset ? _dataOffset : offset;
        }

        long delta = offset - _dataOffset;
        return _dataOffset + (delta - (delta % 2));
    }

    private byte[] ReadExact(long offset, int bytesToRead)
    {
        using FileStream fs = OpenSourceStream(_filePath);
        fs.Position = offset;
        byte[] buffer = new byte[bytesToRead];
        int total = 0;
        while (total < bytesToRead)
        {
            int read = fs.Read(buffer, total, bytesToRead - total);
            if (read <= 0)
            {
                throw new IOException("Unexpected EOF while reading.");
            }

            total += read;
        }

        return buffer;
    }

    private byte[] ReadWindow(long startOffset, long endOffset)
    {
        long bytesToRead = Math.Max(0, endOffset - startOffset);
        byte[] buffer = new byte[bytesToRead];
        using FileStream fs = OpenSourceStream(_filePath);
        fs.Position = startOffset;
        int total = 0;
        while (total < bytesToRead)
        {
            int read = fs.Read(buffer, total, (int)bytesToRead - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        if (total == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, total);
        return buffer;
    }

    private static byte[] ReadWindow(FileStream fs, long startOffset, long endOffset)
    {
        long bytesToRead = Math.Max(0, endOffset - startOffset);
        byte[] buffer = new byte[bytesToRead];
        fs.Position = startOffset;
        int total = 0;
        while (total < bytesToRead)
        {
            int read = fs.Read(buffer, total, (int)bytesToRead - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        if (total == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, total);
        return buffer;
    }

    private string DecodeSingleByteLine(List<byte> bytes)
    {
        if (bytes.Count == 0)
        {
            return string.Empty;
        }

        byte[] raw = bytes.ToArray();
        return _encoding.GetString(raw, 0, raw.Length);
    }

    private static string DecodeUtf16Line(List<ushort> units)
    {
        if (units.Count == 0)
        {
            return string.Empty;
        }

        char[] chars = new char[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            chars[i] = (char)units[i];
        }

        return new string(chars);
    }

    private long TrimTrailingBreaks()
    {
        if (!HasContent)
        {
            return _dataOffset;
        }

        long cursor = _fileSize;
        if (CodeUnitSize == 2)
        {
            cursor = AlignCodeUnitOffset(cursor);
            while (cursor >= _dataOffset + 2)
            {
                byte[] bytes = ReadExact(cursor - 2, 2);
                ushort unit = _kind == LogEncodingKind.Utf16Le
                    ? (ushort)(bytes[0] | (bytes[1] << 8))
                    : (ushort)((bytes[0] << 8) | bytes[1]);
                if (!IsLineBreakUnit(unit))
                {
                    break;
                }

                cursor -= 2;
            }

            return cursor;
        }

        while (cursor > _dataOffset)
        {
            byte[] bytes = ReadExact(cursor - 1, 1);
            byte value = bytes[0];
            if (value is not (0x0D or 0x0A))
            {
                break;
            }

            cursor--;
        }

        return cursor;
    }

    private RealLineStartInfo FindRealLineStartContaining(long offset)
    {
        if (!HasContent || offset <= _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        return _kind switch
        {
            LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be => FindUtf16RealLineStartContaining(offset),
            LogEncodingKind.Utf8 => FindUtf8RealLineStartContaining(offset),
            _ => FindSingleByteRealLineStartContaining(offset)
        };
    }

    private RealLineStartInfo FindSingleByteRealLineStartContaining(long offset)
    {
        long bounded = Math.Min(Math.Max(offset, _dataOffset), _fileSize);
        long searchStart = Math.Max(_dataOffset, bounded - BackSearchLimitChars);
        byte[] buffer = ReadWindow(searchStart, bounded);
        for (int i = buffer.Length; i > 0; i--)
        {
            if (buffer[i - 1] is 0x0D or 0x0A)
            {
                return new(searchStart + i, RealLineStartKind.TrueBreak);
            }
        }

        if (searchStart == _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        return new(searchStart, RealLineStartKind.SearchLimit);
    }

    private RealLineStartInfo FindUtf16RealLineStartContaining(long offset)
    {
        long bounded = AlignCodeUnitOffset(Math.Min(Math.Max(offset, _dataOffset), _fileSize));
        long searchStart = AlignCodeUnitOffset(Math.Max(_dataOffset, bounded - (BackSearchLimitChars * 2L)));
        byte[] buffer = ReadWindow(searchStart, bounded);
        bool littleEndian = _kind == LogEncodingKind.Utf16Le;

        for (int i = buffer.Length; i >= 2; i -= 2)
        {
            ushort unit = littleEndian
                ? (ushort)(buffer[i - 2] | (buffer[i - 1] << 8))
                : (ushort)((buffer[i - 2] << 8) | buffer[i - 1]);
            if (IsLineBreakUnit(unit))
            {
                return new(searchStart + i, RealLineStartKind.TrueBreak);
            }
        }

        if (searchStart == _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        int idx = AdvanceToValidUtf16Start(buffer, littleEndian);

        long provisionalStart = AlignCodeUnitOffset(searchStart + idx);
        if (provisionalStart <= _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        return new(provisionalStart, RealLineStartKind.SearchLimit);
    }

    private RealLineStartInfo FindUtf8RealLineStartContaining(long offset)
    {
        long bounded = Math.Min(Math.Max(offset, _dataOffset), _fileSize);
        long searchStart = Math.Max(_dataOffset, bounded - (BackSearchLimitChars * 4L));
        byte[] buffer = ReadWindow(searchStart, bounded);

        for (int i = buffer.Length; i > 0; i--)
        {
            if (buffer[i - 1] is 0x0D or 0x0A)
            {
                return new(searchStart + i, RealLineStartKind.TrueBreak);
            }
        }

        if (searchStart == _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        int idx = AdvanceToValidUtf8Start(buffer);

        long provisionalStart = searchStart + idx;
        if (provisionalStart <= _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        return new(provisionalStart, RealLineStartKind.SearchLimit);
    }

    private RealLineStartInfo FindPreviousRealLineStart(long currentRealLineStart)
    {
        if (currentRealLineStart <= _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        return _kind switch
        {
            LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be => FindPreviousUtf16RealLineStart(currentRealLineStart),
            _ => FindPreviousSingleByteRealLineStart(currentRealLineStart)
        };
    }

    private RealLineStartInfo FindPreviousSingleByteRealLineStart(long currentRealLineStart)
    {
        using (FileStream fs = OpenSourceStream(_filePath))
        {
            return FindPreviousSingleByteRealLineStart(fs, currentRealLineStart);
        }
    }

    private RealLineStartInfo FindPreviousSingleByteRealLineStart(FileStream fs, long currentRealLineStart)
    {
        long cursor;
        fs.Position = currentRealLineStart - 1;
        int value = fs.ReadByte();
        if (value < 0)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        if (value == 0x0A)
        {
            long breakStart = currentRealLineStart - 1;
            if (currentRealLineStart >= _dataOffset + 2)
            {
                fs.Position = currentRealLineStart - 2;
                int previousValue = fs.ReadByte();
                if (previousValue == 0x0D)
                {
                    breakStart = currentRealLineStart - 2;
                }
            }

            cursor = breakStart - 1;
        }
        else if (value == 0x0D)
        {
            cursor = currentRealLineStart - 2;
        }
        else
        {
            cursor = currentRealLineStart - 1;
        }

        if (cursor < _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        long bounded = Math.Min(Math.Max(cursor, _dataOffset), _fileSize);
        long searchStart = _kind == LogEncodingKind.Utf8
            ? Math.Max(_dataOffset, bounded - (BackSearchLimitChars * 4L))
            : Math.Max(_dataOffset, bounded - BackSearchLimitChars);

        if (TryFindPreviousSingleByteBreakByBlocks(fs, searchStart, bounded, out long lineStart))
        {
            return new(lineStart, RealLineStartKind.TrueBreak);
        }

        if (searchStart == _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        if (_kind != LogEncodingKind.Utf8)
        {
            return new(searchStart, RealLineStartKind.SearchLimit);
        }

        long prefixEnd = Math.Min(bounded, searchStart + Utf8SearchLimitProbeBytes);
        byte[] prefix = ReadWindow(fs, searchStart, prefixEnd);
        int idx = AdvanceToValidUtf8Start(prefix);
        long provisionalStart = searchStart + idx;
        if (provisionalStart <= _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        return new(provisionalStart, RealLineStartKind.SearchLimit);
    }

    private RealLineStartInfo FindPreviousUtf16RealLineStart(long currentRealLineStart)
    {
        if (currentRealLineStart <= _dataOffset + 1)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        using (FileStream fs = OpenSourceStream(_filePath))
        {
            return FindPreviousUtf16RealLineStart(fs, currentRealLineStart);
        }
    }

    private RealLineStartInfo FindPreviousUtf16RealLineStart(FileStream fs, long currentRealLineStart)
    {
        if (currentRealLineStart <= _dataOffset + 1)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        bool littleEndian = _kind == LogEncodingKind.Utf16Le;
        long cursor;
        fs.Position = currentRealLineStart - 2;
        Span<byte> bytes = stackalloc byte[2];
        fs.ReadExactly(bytes);
        ushort unit = littleEndian
            ? (ushort)(bytes[0] | (bytes[1] << 8))
            : (ushort)((bytes[0] << 8) | bytes[1]);

        if (unit == 0x000A)
        {
            long breakStart = currentRealLineStart - 2;
            if (currentRealLineStart >= _dataOffset + 4)
            {
                fs.Position = currentRealLineStart - 4;
                Span<byte> prevBytes = stackalloc byte[2];
                fs.ReadExactly(prevBytes);
                ushort previousUnit = littleEndian
                    ? (ushort)(prevBytes[0] | (prevBytes[1] << 8))
                    : (ushort)((prevBytes[0] << 8) | prevBytes[1]);
                if (previousUnit == 0x000D)
                {
                    breakStart = currentRealLineStart - 4;
                }
            }

            cursor = breakStart - 2;
        }
        else if (unit == 0x000D)
        {
            cursor = currentRealLineStart - 4;
        }
        else
        {
            cursor = currentRealLineStart - 2;
        }

        if (cursor < _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        long bounded = AlignCodeUnitOffset(Math.Min(Math.Max(cursor, _dataOffset), _fileSize));
        long searchStart = AlignCodeUnitOffset(Math.Max(_dataOffset, bounded - (BackSearchLimitChars * 2L)));

        if (TryFindPreviousUtf16BreakByBlocks(fs, searchStart, bounded, littleEndian, out long lineStart))
        {
            return new(lineStart, RealLineStartKind.TrueBreak);
        }

        if (searchStart == _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        long prefixEnd = AlignCodeUnitOffset(Math.Min(bounded, searchStart + Utf16SearchLimitProbeBytes));
        byte[] prefix = ReadWindow(fs, searchStart, prefixEnd);
        int idx = AdvanceToValidUtf16Start(prefix, littleEndian);

        long provisionalStart = AlignCodeUnitOffset(searchStart + idx);
        if (provisionalStart <= _dataOffset)
        {
            return new(_dataOffset, RealLineStartKind.TrueBreak);
        }

        return new(provisionalStart, RealLineStartKind.SearchLimit);
    }

    private List<RealLineStartInfo> CollectPreviousRealLineStarts(FileStream fs, long currentRealLineStart, int maxRealLines)
    {
        List<RealLineStartInfo> starts = new(Math.Max(0, maxRealLines));
        long scanFrom = currentRealLineStart;
        for (int i = 0; i < maxRealLines; i++)
        {
            RealLineStartInfo previous = _kind switch
            {
                LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be => FindPreviousUtf16RealLineStart(fs, scanFrom),
                _ => FindPreviousSingleByteRealLineStart(fs, scanFrom)
            };

            if (previous.StartOffset >= scanFrom)
            {
                break;
            }

            starts.Add(previous);
            scanFrom = previous.StartOffset;
        }

        return starts;
    }

    private static bool TryFindPreviousSingleByteBreakByBlocks(FileStream fs, long searchStart, long scanEndExclusive, out long lineStart)
    {
        long blockEndExclusive = scanEndExclusive;
        while (blockEndExclusive > searchStart)
        {
            long blockStart = Math.Max(searchStart, blockEndExclusive - ReverseScanBlockBytes);
            byte[] buffer = ReadWindow(fs, blockStart, blockEndExclusive);
            for (int i = buffer.Length; i > 0; i--)
            {
                if (buffer[i - 1] is 0x0D or 0x0A)
                {
                    lineStart = blockStart + i;
                    return true;
                }
            }

            blockEndExclusive = blockStart;
        }

        lineStart = 0;
        return false;
    }

    private static bool TryFindPreviousUtf16BreakByBlocks(FileStream fs, long searchStart, long scanEndExclusive, bool littleEndian, out long lineStart)
    {
        long blockEndExclusive = scanEndExclusive;
        while (blockEndExclusive > searchStart)
        {
            long bytesToRead = Math.Min(blockEndExclusive - searchStart, ReverseScanBlockBytes);
            bytesToRead -= bytesToRead % 2;
            if (bytesToRead <= 0)
            {
                break;
            }

            long blockStart = blockEndExclusive - bytesToRead;
            byte[] buffer = ReadWindow(fs, blockStart, blockEndExclusive);
            for (int i = buffer.Length; i >= 2; i -= 2)
            {
                ushort unit = littleEndian
                    ? (ushort)(buffer[i - 2] | (buffer[i - 1] << 8))
                    : (ushort)((buffer[i - 2] << 8) | buffer[i - 1]);
                if (IsLineBreakUnit(unit))
                {
                    lineStart = blockStart + i;
                    return true;
                }
            }

            blockEndExclusive = blockStart;
        }

        lineStart = 0;
        return false;
    }

    private VisualPosition LocateVisualPositionForOffset(long requestedOffset) =>
        LocateVisualPositionForOffset(requestedOffset, null);

    private VisualPosition LocateVisualPositionForOffset(long requestedOffset, RealLineStartInfo? forcedRealStart)
    {
        if (!HasContent || requestedOffset <= _dataOffset)
        {
            return DefaultTopPosition();
        }

        long bounded = Math.Min(requestedOffset, _fileSize);
        if (CodeUnitSize == 2)
        {
            bounded = AlignCodeUnitOffset(bounded);
        }

        if (bounded >= _fileSize)
        {
            long trimmed = TrimTrailingBreaks();
            if (trimmed <= _dataOffset)
            {
                return DefaultTopPosition();
            }

            bounded = trimmed;
        }

        RealLineStartInfo realLineStart = forcedRealStart ?? FindRealLineStartContaining(bounded);
        VisualPosition current = new(realLineStart.StartOffset, realLineStart.StartOffset, realLineStart.Kind, VisualStartKind.RealStart, 0);

        using FileStream fs = OpenSourceStream(_filePath);
        fs.Position = current.StartOffset;
        while (true)
        {
            VisualReadResult read = ReadVisualRow(fs, current);
            if (bounded < read.Row.EndOffset || read.NextPosition is null)
            {
                return current;
            }

            current = read.NextPosition.Value;
            if (current.StartOffset >= _fileSize)
            {
                return current;
            }
        }
    }

    private bool TryGetPreviousVisualPosition(VisualPosition current, out VisualPosition previous)
    {
        if (current.StartOffset <= _dataOffset)
        {
            previous = DefaultTopPosition();
            return false;
        }

        if (current.VisualStartKind == VisualStartKind.ForcedWrap && current.SegmentIndex > 0)
        {
            previous = GetVisualPositionForSegment(current.RealLineStartOffset, current.RealLineStartKind, current.SegmentIndex - 1);
            return true;
        }

        current = NormalizePreviousNavigationStart(current);

        RealLineStartInfo previousRealLine = FindPreviousRealLineStart(current.RealLineStartOffset);
        if (previousRealLine.StartOffset == current.RealLineStartOffset)
        {
            previous = DefaultTopPosition();
            return false;
        }

        previous = LocateLastVisualPositionOfRealLine(previousRealLine);
        return true;
    }

    private VisualPosition LocateLastVisualPositionOfRealLine(RealLineStartInfo realLineStart)
    {
        using FileStream fs = OpenSourceStream(_filePath);
        return LocateLastVisualPositionOfRealLine(fs, realLineStart);
    }

    private VisualPosition LocateLastVisualPositionOfRealLine(FileStream fs, RealLineStartInfo realLineStart)
    {
        VisualPosition current = new(realLineStart.StartOffset, realLineStart.StartOffset, realLineStart.Kind, VisualStartKind.RealStart, 0);
        fs.Position = current.StartOffset;
        while (true)
        {
            VisualReadResult read = ReadVisualRow(fs, current);
            if (read.NextPosition is null ||
                read.NextPosition.Value.RealLineStartOffset != realLineStart.StartOffset ||
                read.NextPosition.Value.VisualStartKind != VisualStartKind.ForcedWrap)
            {
                return current;
            }

            current = read.NextPosition.Value;
        }
    }

    private VisualPosition GetVisualPositionForSegment(long realLineStartOffset, RealLineStartKind kind, int targetSegmentIndex)
    {
        VisualPosition current = new(realLineStartOffset, realLineStartOffset, kind, VisualStartKind.RealStart, 0);
        if (targetSegmentIndex <= 0)
        {
            return current;
        }

        using FileStream fs = OpenSourceStream(_filePath);
        return GetVisualPositionForSegment(fs, realLineStartOffset, kind, targetSegmentIndex);
    }

    private VisualPosition GetVisualPositionForSegment(FileStream fs, long realLineStartOffset, RealLineStartKind kind, int targetSegmentIndex)
    {
        VisualPosition current = new(realLineStartOffset, realLineStartOffset, kind, VisualStartKind.RealStart, 0);
        if (targetSegmentIndex <= 0)
        {
            return current;
        }

        fs.Position = current.StartOffset;
        while (current.SegmentIndex < targetSegmentIndex)
        {
            VisualReadResult read = ReadVisualRow(fs, current);
            if (read.NextPosition is null ||
                read.NextPosition.Value.RealLineStartOffset != realLineStartOffset ||
                read.NextPosition.Value.VisualStartKind != VisualStartKind.ForcedWrap)
            {
                return current;
            }

            current = read.NextPosition.Value;
        }

        return current;
    }

    private VisualPosition NormalizePreviousNavigationStart(VisualPosition current)
    {
        while (current.VisualStartKind == VisualStartKind.RealStart && current.RealLineStartKind == RealLineStartKind.SearchLimit)
        {
            RealLineStartInfo correctedRealStart = FindPreviousRealLineStart(current.RealLineStartOffset);
            if (correctedRealStart.StartOffset >= current.RealLineStartOffset)
            {
                break;
            }

            VisualPosition correctedCurrent = LocateVisualPositionForOffset(current.StartOffset, correctedRealStart);
            if (correctedCurrent.StartOffset == current.StartOffset &&
                correctedCurrent.RealLineStartOffset == current.RealLineStartOffset &&
                correctedCurrent.RealLineStartKind == current.RealLineStartKind &&
                correctedCurrent.VisualStartKind == current.VisualStartKind &&
                correctedCurrent.SegmentIndex == current.SegmentIndex)
            {
                break;
            }

            current = correctedCurrent;
        }

        return current;
    }

    private bool LoadViewportAt(long requestedTopOffset, int visibleLines) =>
        LoadViewportAt(LocateVisualPositionForOffset(requestedTopOffset), visibleLines);

    private bool LoadViewportAt(VisualPosition topPosition, int visibleLines)
    {
        visibleLines = Math.Max(1, visibleLines);
        if (_viewportLoaded &&
            _viewportRows.Count > 0 &&
            _topOffset == topPosition.StartOffset &&
            _viewportVisibleLines == visibleLines &&
            ToVisualPosition(_viewportRows[0]) == topPosition)
        {
            return true;
        }

        _topOffset = topPosition.StartOffset;
        _viewportVisibleLines = visibleLines;
        _viewportRows.Clear();
        _viewportEndOffset = _topOffset;
        _viewportLoaded = false;

        if (!HasContent || _topOffset >= _fileSize)
        {
            _viewportLoaded = true;
            return true;
        }

        using FileStream fs = OpenSourceStream(_filePath);
        fs.Position = topPosition.StartOffset;
        VisualPosition current = topPosition;

        while (_viewportRows.Count < visibleLines && current.StartOffset < _fileSize)
        {
            VisualReadResult read = ReadVisualRow(fs, current);
            _viewportRows.Add(read.Row);
            _viewportEndOffset = read.Row.EndOffset;

            if (read.NextPosition is null)
            {
                break;
            }

            current = read.NextPosition.Value;
        }

        if (_viewportRows.Count == 0)
        {
            _viewportEndOffset = _topOffset;
        }

        _viewportLoaded = true;
        return true;
    }

    private VisualReadResult ReadVisualRow(FileStream fs, VisualPosition position) =>
        _kind switch
        {
            LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be => ReadUtf16VisualRow(fs, position),
            LogEncodingKind.Utf8 => ReadUtf8VisualRow(fs, position),
            _ => ReadSingleByteVisualRow(fs, position)
        };

    private VisualReadResult ReadSingleByteVisualRow(FileStream fs, VisualPosition position)
    {
        List<byte> lineBytes = new(Math.Min(SegmentChars, 256));
        while (fs.Position < _fileSize)
        {
            long byteStart = fs.Position;
            int read = fs.ReadByte();
            if (read < 0)
            {
                break;
            }

            byte value = (byte)read;
            if (value == 0x0D || value == 0x0A)
            {
                long endOffset = fs.Position;
                if (value == 0x0D && TryConsumeSingleByteLf(fs, ref endOffset))
                {
                }

                VisualPosition? next = endOffset < _fileSize
                    ? new(endOffset, endOffset, RealLineStartKind.TrueBreak, VisualStartKind.RealStart, 0)
                    : null;
                return CreateSingleByteResult(position, lineBytes, endOffset, next);
            }

            lineBytes.Add(value);
            if (lineBytes.Count == SegmentChars)
            {
                long endOffset = fs.Position;
                VisualPosition? next = endOffset < _fileSize
                    ? new(endOffset, position.RealLineStartOffset, position.RealLineStartKind, VisualStartKind.ForcedWrap, position.SegmentIndex + 1)
                    : null;
                if (TryConsumeSingleByteBreakAfterWrap(fs, ref endOffset))
                {
                    next = endOffset < _fileSize
                        ? new(endOffset, endOffset, RealLineStartKind.TrueBreak, VisualStartKind.RealStart, 0)
                        : null;
                }

                return CreateSingleByteResult(position, lineBytes, endOffset, next);
            }

            if (byteStart == fs.Position)
            {
                break;
            }
        }

        return CreateSingleByteResult(position, lineBytes, _fileSize, null);
    }

    private VisualReadResult CreateSingleByteResult(VisualPosition position, List<byte> bytes, long endOffset, VisualPosition? nextPosition)
    {
        ViewportRow row = new(
            StartOffset: position.StartOffset,
            EndOffset: endOffset,
            RealLineStartOffset: position.RealLineStartOffset,
            RealLineStartKind: position.RealLineStartKind,
            VisualStartKind: position.VisualStartKind,
            SegmentIndex: position.SegmentIndex,
            SegmentStartOffset: position.StartOffset,
            SegmentEndOffset: endOffset,
            Text: DecodeSingleByteLine(bytes));
        return new(row, nextPosition);
    }

    private bool TryConsumeSingleByteLf(FileStream fs, ref long endOffset)
    {
        if (fs.Position >= _fileSize)
        {
            return false;
        }

        int next = fs.ReadByte();
        if (next == 0x0A)
        {
            endOffset = fs.Position;
            return true;
        }

        if (next >= 0)
        {
            fs.Position--;
        }

        return false;
    }

    private bool TryConsumeSingleByteBreakAfterWrap(FileStream fs, ref long endOffset)
    {
        if (fs.Position >= _fileSize)
        {
            return false;
        }

        int next = fs.ReadByte();
        if (next < 0)
        {
            return false;
        }

        if (next == 0x0D)
        {
            endOffset = fs.Position;
            TryConsumeSingleByteLf(fs, ref endOffset);
            return true;
        }

        if (next == 0x0A)
        {
            endOffset = fs.Position;
            return true;
        }

        fs.Position--;
        return false;
    }

    private VisualReadResult ReadUtf16VisualRow(FileStream fs, VisualPosition position)
    {
        List<ushort> lineUnits = new(Math.Min(SegmentChars, 256));
        bool littleEndian = _kind == LogEncodingKind.Utf16Le;
        byte[] unitBytes = new byte[2];
        while (fs.Position + 1 < _fileSize)
        {
            fs.ReadExactly(unitBytes);
            ushort unit = littleEndian
                ? (ushort)(unitBytes[0] | (unitBytes[1] << 8))
                : (ushort)((unitBytes[0] << 8) | unitBytes[1]);

            if (unit == 0x000D || unit == 0x000A)
            {
                long endOffset = fs.Position;
                if (unit == 0x000D && TryConsumeUtf16Lf(fs, ref endOffset, littleEndian))
                {
                }

                VisualPosition? next = endOffset < _fileSize
                    ? new(endOffset, endOffset, RealLineStartKind.TrueBreak, VisualStartKind.RealStart, 0)
                    : null;
                return CreateUtf16Result(position, lineUnits, endOffset, next);
            }

            lineUnits.Add(unit);
            if (lineUnits.Count == SegmentChars)
            {
                long endOffset = fs.Position;
                VisualPosition? next = endOffset < _fileSize
                    ? new(endOffset, position.RealLineStartOffset, position.RealLineStartKind, VisualStartKind.ForcedWrap, position.SegmentIndex + 1)
                    : null;
                if (TryConsumeUtf16BreakAfterWrap(fs, ref endOffset, littleEndian))
                {
                    next = endOffset < _fileSize
                        ? new(endOffset, endOffset, RealLineStartKind.TrueBreak, VisualStartKind.RealStart, 0)
                        : null;
                }

                return CreateUtf16Result(position, lineUnits, endOffset, next);
            }
        }

        return CreateUtf16Result(position, lineUnits, _fileSize, null);
    }

    private VisualReadResult CreateUtf16Result(VisualPosition position, List<ushort> units, long endOffset, VisualPosition? nextPosition)
    {
        ViewportRow row = new(
            StartOffset: position.StartOffset,
            EndOffset: endOffset,
            RealLineStartOffset: position.RealLineStartOffset,
            RealLineStartKind: position.RealLineStartKind,
            VisualStartKind: position.VisualStartKind,
            SegmentIndex: position.SegmentIndex,
            SegmentStartOffset: position.StartOffset,
            SegmentEndOffset: endOffset,
            Text: DecodeUtf16Line(units));
        return new(row, nextPosition);
    }

    private bool TryConsumeUtf16Lf(FileStream fs, ref long endOffset, bool littleEndian)
    {
        if (fs.Position + 1 >= _fileSize)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[2];
        fs.ReadExactly(bytes);
        ushort nextUnit = littleEndian
            ? (ushort)(bytes[0] | (bytes[1] << 8))
            : (ushort)((bytes[0] << 8) | bytes[1]);
        if (nextUnit == 0x000A)
        {
            endOffset = fs.Position;
            return true;
        }

        fs.Position -= 2;
        return false;
    }

    private bool TryConsumeUtf16BreakAfterWrap(FileStream fs, ref long endOffset, bool littleEndian)
    {
        if (fs.Position + 1 >= _fileSize)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[2];
        fs.ReadExactly(bytes);
        ushort unit = littleEndian
            ? (ushort)(bytes[0] | (bytes[1] << 8))
            : (ushort)((bytes[0] << 8) | bytes[1]);

        if (unit == 0x000D)
        {
            endOffset = fs.Position;
            TryConsumeUtf16Lf(fs, ref endOffset, littleEndian);
            return true;
        }

        if (unit == 0x000A)
        {
            endOffset = fs.Position;
            return true;
        }

        fs.Position -= 2;
        return false;
    }

    private VisualReadResult ReadUtf8VisualRow(FileStream fs, VisualPosition position)
    {
        StringBuilder text = new(Math.Min(SegmentChars, 256));
        int renderedChars = 0;
        byte[] runeBytes = new byte[4];
        while (fs.Position < _fileSize)
        {
            long runeStart = fs.Position;
            int firstRead = fs.ReadByte();
            if (firstRead < 0)
            {
                break;
            }

            byte firstByte = (byte)firstRead;
            if (firstByte == 0x0D || firstByte == 0x0A)
            {
                long endOffset = fs.Position;
                if (firstByte == 0x0D && TryConsumeSingleByteLf(fs, ref endOffset))
                {
                }

                VisualPosition? next = endOffset < _fileSize
                    ? new(endOffset, endOffset, RealLineStartKind.TrueBreak, VisualStartKind.RealStart, 0)
                    : null;
                return CreateUtf8Result(position, text.ToString(), endOffset, next);
            }

            int sequenceLength = Utf8SequenceLength(firstByte);
            runeBytes[0] = firstByte;
            int bytesRead = 1;
            while (bytesRead < sequenceLength && fs.Position < _fileSize)
            {
                int next = fs.ReadByte();
                if (next < 0)
                {
                    break;
                }

                runeBytes[bytesRead] = (byte)next;
                bytesRead++;
            }

            string runeText;
            int runeCharCount;
            OperationStatus status = Rune.DecodeFromUtf8(runeBytes.AsSpan(0, bytesRead), out Rune rune, out int consumed);
            if (status == OperationStatus.Done)
            {
                runeText = rune.ToString();
                runeCharCount = runeText.Length;
                if (consumed != bytesRead)
                {
                    fs.Position = runeStart + consumed;
                }
            }
            else
            {
                runeText = "\uFFFD";
                runeCharCount = 1;
                fs.Position = runeStart + 1;
            }

            if (renderedChars > 0 && renderedChars + runeCharCount > SegmentChars)
            {
                fs.Position = runeStart;
                long endOffset = runeStart;
                VisualPosition next = new(endOffset, position.RealLineStartOffset, position.RealLineStartKind, VisualStartKind.ForcedWrap, position.SegmentIndex + 1);
                return CreateUtf8Result(position, text.ToString(), endOffset, next);
            }

            text.Append(runeText);
            renderedChars += runeCharCount;

            if (renderedChars >= SegmentChars)
            {
                long endOffset = fs.Position;
                VisualPosition? next = endOffset < _fileSize
                    ? new(endOffset, position.RealLineStartOffset, position.RealLineStartKind, VisualStartKind.ForcedWrap, position.SegmentIndex + 1)
                    : null;
                if (TryConsumeSingleByteBreakAfterWrap(fs, ref endOffset))
                {
                    next = endOffset < _fileSize
                        ? new(endOffset, endOffset, RealLineStartKind.TrueBreak, VisualStartKind.RealStart, 0)
                        : null;
                }

                return CreateUtf8Result(position, text.ToString(), endOffset, next);
            }
        }

        return CreateUtf8Result(position, text.ToString(), _fileSize, null);
    }

    private VisualReadResult CreateUtf8Result(VisualPosition position, string text, long endOffset, VisualPosition? nextPosition)
    {
        ViewportRow row = new(
            StartOffset: position.StartOffset,
            EndOffset: endOffset,
            RealLineStartOffset: position.RealLineStartOffset,
            RealLineStartKind: position.RealLineStartKind,
            VisualStartKind: position.VisualStartKind,
            SegmentIndex: position.SegmentIndex,
            SegmentStartOffset: position.StartOffset,
            SegmentEndOffset: endOffset,
            Text: text);
        return new(row, nextPosition);
    }

    private static int Utf8SequenceLength(byte firstByte)
    {
        if ((firstByte & 0b1000_0000) == 0)
        {
            return 1;
        }

        if ((firstByte & 0b1110_0000) == 0b1100_0000)
        {
            return 2;
        }

        if ((firstByte & 0b1111_0000) == 0b1110_0000)
        {
            return 3;
        }

        if ((firstByte & 0b1111_1000) == 0b1111_0000)
        {
            return 4;
        }

        return 1;
    }

    private static int DecodeUtf8CharLength(ReadOnlySpan<byte> source, out int charCount)
    {
        OperationStatus status = Rune.DecodeFromUtf8(source, out Rune rune, out int bytesConsumed);
        if (status == OperationStatus.Done)
        {
            charCount = rune.ToString().Length;
            return Math.Max(1, bytesConsumed);
        }

        charCount = 1;
        return 1;
    }

    private static int AdvanceToValidUtf16Start(ReadOnlySpan<byte> buffer, bool littleEndian)
    {
        int idx = 0;
        while (idx + 1 < buffer.Length)
        {
            ushort unit0 = littleEndian
                ? (ushort)(buffer[idx] | (buffer[idx + 1] << 8))
                : (ushort)((buffer[idx] << 8) | buffer[idx + 1]);

            if (char.IsLowSurrogate((char)unit0))
            {
                idx += 2;
                continue;
            }

            if (char.IsHighSurrogate((char)unit0))
            {
                if (idx + 3 < buffer.Length)
                {
                    ushort unit1 = littleEndian
                        ? (ushort)(buffer[idx + 2] | (buffer[idx + 3] << 8))
                        : (ushort)((buffer[idx + 2] << 8) | buffer[idx + 3]);
                    if (char.IsLowSurrogate((char)unit1))
                    {
                        return idx;
                    }
                }

                idx += 2;
                continue;
            }

            return idx;
        }

        return buffer.Length;
    }

    private static int AdvanceToValidUtf8Start(ReadOnlySpan<byte> buffer)
    {
        int idx = 0;
        while (idx < buffer.Length)
        {
            while (idx < buffer.Length && (buffer[idx] & 0b1100_0000) == 0b1000_0000)
            {
                idx++;
            }

            if (idx >= buffer.Length)
            {
                break;
            }

            OperationStatus status = Rune.DecodeFromUtf8(buffer[idx..], out _, out int consumed);
            if (status == OperationStatus.Done && consumed > 0)
            {
                return idx;
            }

            idx++;
        }

        return buffer.Length;
    }
}
