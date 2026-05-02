using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal enum FileEncodingKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Windows1252
}

internal sealed class LogFile : IDisposable
{
    internal sealed record ViewportRow(long StartOffset, long EndOffset, string Text);

    private readonly string _filePath;
    private readonly FileEncodingKind _kind;
    private readonly Encoding _encoding;
    private readonly long _dataOffset;
    private readonly long _fileSize;
    private readonly List<ViewportRow> _viewportRows = new();
    private long _topOffset;
    private long _viewportEndOffset;
    private int _viewportVisibleLines;
    private bool _viewportLoaded;

    private LogFile(string filePath, FileEncodingKind kind, Encoding encoding, long dataOffset, long fileSize)
    {
        _filePath = filePath;
        _kind = kind;
        _encoding = encoding;
        _dataOffset = dataOffset;
        _fileSize = fileSize;
        _topOffset = dataOffset;
        _viewportEndOffset = dataOffset;
    }

    public string FilePath => _filePath;
    public long DataOffset => _dataOffset;
    public string EncodingName => _kind switch
    {
        FileEncodingKind.Utf8 => "UTF-8 BOM",
        FileEncodingKind.Utf16Le => "UTF-16 LE BOM",
        FileEncodingKind.Utf16Be => "UTF-16 BE BOM",
        _ => "Windows-1252"
    };
    public long FileSize => _fileSize;
    public long TopOffset => _topOffset;
    public long ViewportEndOffset => _viewportEndOffset;
    public long ViewportBytes => _viewportEndOffset >= _topOffset ? _viewportEndOffset - _topOffset : 0;
    public bool HasContent => _fileSize > _dataOffset;
    public IReadOnlyList<ViewportRow> ViewportRows => _viewportRows;

    public static LogFile Open(string path)
    {
        AppLog.Instance.Info("file.open.begin", "begin", new LogField("path", path));
        using FileStream fs = OpenSourceStream(path);
        (FileEncodingKind Kind, Encoding Encoding, long DataOffset) detection;
        try
        {
            detection = DetectEncoding(fs);
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("path", path),
                new LogField("stage", "encoding_detect_failed"),
                new LogField("reason", ex.Message),
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name));
            throw;
        }

        AppLog.Instance.Info(
            "encoding.detected",
            "detected",
            new LogField("path", path),
            new LogField("encoding", DescribeEncoding(detection.Kind)),
            new LogField("dataOffset", detection.DataOffset.ToString()));

        return new LogFile(path, detection.Kind, detection.Encoding, detection.DataOffset, fs.Length);
    }

    public bool EnsureViewport(int visibleLines) => LoadViewportAt(_topOffset, visibleLines);

    public bool ScrollLineDown(int visibleLines)
    {
        if (!EnsureViewport(visibleLines) || _viewportRows.Count == 0)
        {
            return false;
        }

        if (_viewportRows.Count > 1)
        {
            return LoadViewportAt(_viewportRows[1].StartOffset, visibleLines);
        }

        if (_viewportEndOffset > _topOffset && _viewportEndOffset < _fileSize)
        {
            return LoadViewportAt(_viewportEndOffset, visibleLines);
        }

        return false;
    }

    public bool ScrollLineUp(int visibleLines)
    {
        if (!HasContent)
        {
            return false;
        }

        return LoadViewportAt(PreviousLineStart(_topOffset), visibleLines);
    }

    public bool ScrollPageDown(int visibleLines)
    {
        if (!EnsureViewport(visibleLines) || _viewportRows.Count == 0)
        {
            return false;
        }

        if (_viewportEndOffset > _topOffset && _viewportEndOffset < _fileSize)
        {
            return LoadViewportAt(_viewportEndOffset, visibleLines);
        }

        return false;
    }

    public bool ScrollPageUp(int visibleLines)
    {
        if (!HasContent)
        {
            return false;
        }

        int steps = Math.Max(1, visibleLines);
        long nextTop = _topOffset;
        for (int i = 0; i < steps; i++)
        {
            long previous = PreviousLineStart(nextTop);
            if (previous == nextTop)
            {
                break;
            }

            nextTop = previous;
        }

        return LoadViewportAt(nextTop, visibleLines);
    }

    public bool ScrollHome(int visibleLines) => LoadViewportAt(_dataOffset, visibleLines);

    public bool ScrollEnd(int visibleLines)
    {
        if (!HasContent)
        {
            return LoadViewportAt(_dataOffset, visibleLines);
        }

        long nextTop = NormalizeRequestedOffset(_fileSize);
        int steps = Math.Max(0, visibleLines - 1);
        for (int i = 0; i < steps; i++)
        {
            long previous = PreviousLineStart(nextTop);
            if (previous == nextTop)
            {
                break;
            }

            nextTop = previous;
        }

        return LoadViewportAt(nextTop, visibleLines);
    }

    public bool ScrollToApproximateOffset(long requestedOffset, int visibleLines) =>
        LoadViewportAt(NormalizeRequestedOffset(requestedOffset), visibleLines);

    internal LogFile CloneForWorker()
    {
        LogFile clone = new(_filePath, _kind, _encoding, _dataOffset, _fileSize)
        {
            _topOffset = _topOffset,
            _viewportEndOffset = _viewportEndOffset,
            _viewportVisibleLines = _viewportVisibleLines,
            _viewportLoaded = _viewportLoaded
        };
        clone._viewportRows.AddRange(_viewportRows);
        return clone;
    }

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

    private static FileStream OpenSourceStream(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("path", path),
                new LogField("stage", "open_failed"),
                new LogField("reason", ex.Message),
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name));
            throw;
        }
    }

    private static (FileEncodingKind Kind, Encoding Encoding, long DataOffset) DetectEncoding(FileStream fs)
    {
        fs.Position = 0;
        Span<byte> sample = stackalloc byte[3];
        int read = fs.Read(sample);
        fs.Position = 0;

        if (read >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            return (FileEncodingKind.Utf8, Encoding.UTF8, 3);
        }

        if (read >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
        {
            return (FileEncodingKind.Utf16Le, Encoding.Unicode, 2);
        }

        if (read >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
        {
            return (FileEncodingKind.Utf16Be, Encoding.BigEndianUnicode, 2);
        }

        return (FileEncodingKind.Windows1252, Windows1252Encoding.Instance, 0);
    }

    private static string DescribeEncoding(FileEncodingKind kind) => kind switch
    {
        FileEncodingKind.Utf8 => "UTF-8 BOM",
        FileEncodingKind.Utf16Le => "UTF-16 LE BOM",
        FileEncodingKind.Utf16Be => "UTF-16 BE BOM",
        _ => "Windows-1252"
    };

    private int CodeUnitSize => _kind is FileEncodingKind.Utf16Le or FileEncodingKind.Utf16Be ? 2 : 1;

    private static bool IsLineBreakUnit(ushort unit) => unit is 0x000D or 0x000A;

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
                ushort unit = _kind == FileEncodingKind.Utf16Le
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

    private long FindLineStartForOffset(long offset)
    {
        if (!HasContent || offset <= _dataOffset)
        {
            return _dataOffset;
        }

        if (CodeUnitSize == 2)
        {
            bool littleEndian = _kind == FileEncodingKind.Utf16Le;
            byte[] buffer = new byte[64 * 1024];
            long searchEnd = AlignCodeUnitOffset(Math.Min(offset, _fileSize));
            while (searchEnd > _dataOffset)
            {
                long chunkStart = searchEnd > buffer.Length
                    ? Math.Max(_dataOffset, searchEnd - buffer.Length)
                    : _dataOffset;
                chunkStart = AlignCodeUnitOffset(chunkStart);
                long bytesToRead = searchEnd - chunkStart;
                if (bytesToRead == 0)
                {
                    break;
                }

                using FileStream fs = OpenSourceStream(_filePath);
                fs.Position = chunkStart;
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

                for (int i = total; i >= 2; i -= 2)
                {
                    ushort unit = littleEndian
                        ? (ushort)(buffer[i - 2] | (buffer[i - 1] << 8))
                        : (ushort)((buffer[i - 2] << 8) | buffer[i - 1]);
                    if (IsLineBreakUnit(unit))
                    {
                        return chunkStart + i;
                    }

                    if (i == 2)
                    {
                        break;
                    }
                }

                if (chunkStart == _dataOffset)
                {
                    break;
                }

                searchEnd = chunkStart;
            }

            return _dataOffset;
        }

        {
            byte[] buffer = new byte[64 * 1024];
            long searchEnd = Math.Min(offset, _fileSize);
            while (searchEnd > _dataOffset)
            {
                long chunkStart = searchEnd > buffer.Length
                    ? Math.Max(_dataOffset, searchEnd - buffer.Length)
                    : _dataOffset;
                long bytesToRead = searchEnd - chunkStart;
                if (bytesToRead == 0)
                {
                    break;
                }

                using FileStream fs = OpenSourceStream(_filePath);
                fs.Position = chunkStart;
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

                for (int i = total; i > 0; i--)
                {
                    byte value = buffer[i - 1];
                    if (value is 0x0D or 0x0A)
                    {
                        return chunkStart + i;
                    }
                }

                if (chunkStart == _dataOffset)
                {
                    break;
                }

                searchEnd = chunkStart;
            }

            return _dataOffset;
        }
    }

    private long NormalizeRequestedOffset(long requestedOffset)
    {
        if (!HasContent)
        {
            return _dataOffset;
        }

        if (requestedOffset <= _dataOffset)
        {
            return _dataOffset;
        }

        long bounded = Math.Min(requestedOffset, _fileSize);
        if (bounded >= _fileSize)
        {
            bounded = TrimTrailingBreaks();
            if (bounded <= _dataOffset)
            {
                return _dataOffset;
            }
        }

        if (CodeUnitSize == 2)
        {
            bounded = AlignCodeUnitOffset(bounded);
        }

        return FindLineStartForOffset(bounded);
    }

    private long PreviousLineStart(long currentStart)
    {
        if (currentStart <= _dataOffset)
        {
            return _dataOffset;
        }

        if (CodeUnitSize == 2)
        {
            if (currentStart <= _dataOffset + 2)
            {
                return _dataOffset;
            }

            using FileStream fs = OpenSourceStream(_filePath);
            Span<byte> bytes = stackalloc byte[2];
            fs.Position = currentStart - 2;
            fs.ReadExactly(bytes);

            ushort DecodeUnit(ReadOnlySpan<byte> unitBytes) =>
                _kind == FileEncodingKind.Utf16Le
                    ? (ushort)(unitBytes[0] | (unitBytes[1] << 8))
                    : (ushort)((unitBytes[0] << 8) | unitBytes[1]);

            long cursor = currentStart;
            ushort unit = DecodeUnit(bytes);
            if (unit == 0x000A)
            {
                if (currentStart >= _dataOffset + 4)
                {
                    Span<byte> prevBytes = stackalloc byte[2];
                    fs.Position = currentStart - 4;
                    fs.ReadExactly(prevBytes);
                    ushort previousUnit = DecodeUnit(prevBytes);
                    cursor = previousUnit == 0x000D ? currentStart - 4 : currentStart - 2;
                }
                else
                {
                    cursor = currentStart - 2;
                }
            }
            else if (unit == 0x000D)
            {
                cursor = currentStart - 2;
            }
            else
            {
                return _dataOffset;
            }

            if (cursor <= _dataOffset)
            {
                return _dataOffset;
            }

            return FindLineStartForOffset(cursor);
        }

        if (currentStart <= _dataOffset + 1)
        {
            return _dataOffset;
        }

        using (FileStream fs = OpenSourceStream(_filePath))
        {
            fs.Position = currentStart - 1;
            int value = fs.ReadByte();
            if (value < 0)
            {
                return _dataOffset;
            }

            long cursor = currentStart;
            if (value == 0x0A)
            {
                if (currentStart >= _dataOffset + 2)
                {
                    fs.Position = currentStart - 2;
                    int previousValue = fs.ReadByte();
                    if (previousValue < 0)
                    {
                        return _dataOffset;
                    }

                    cursor = previousValue == 0x0D ? currentStart - 2 : currentStart - 1;
                }
                else
                {
                    cursor = currentStart - 1;
                }
            }
            else if (value == 0x0D)
            {
                cursor = currentStart - 1;
            }
            else
            {
                return _dataOffset;
            }

            if (cursor <= _dataOffset)
            {
                return _dataOffset;
            }

            return FindLineStartForOffset(cursor);
        }
    }

    private bool LoadViewportAt(long requestedTopOffset, int visibleLines)
    {
        visibleLines = Math.Max(1, visibleLines);
        long normalizedTopOffset = NormalizeRequestedOffset(requestedTopOffset);
        if (_viewportLoaded &&
            normalizedTopOffset == _topOffset &&
            visibleLines == _viewportVisibleLines)
        {
            return true;
        }

        _topOffset = normalizedTopOffset;
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
        fs.Position = _topOffset;

        if (CodeUnitSize == 2)
        {
            byte[] buffer = new byte[64 * 1024];
            List<ushort> lineUnits = new();
            bool littleEndian = _kind == FileEncodingKind.Utf16Le;
            long currentLineStart = _topOffset;
            long absoluteOffset = _topOffset;
            bool pendingCR = false;
            bool hasCarry = false;
            byte carry = 0;

            void EmitLine(long endOffset)
            {
                _viewportRows.Add(new ViewportRow(currentLineStart, endOffset, DecodeUtf16Line(lineUnits)));
                lineUnits.Clear();
                currentLineStart = endOffset;
                _viewportEndOffset = endOffset;
            }

            while (true)
            {
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                long chunkStart = absoluteOffset;
                int index = 0;

                if (hasCarry)
                {
                    ushort unit = littleEndian
                        ? (ushort)(carry | (buffer[0] << 8))
                        : (ushort)((carry << 8) | buffer[0]);
                    long unitStart = chunkStart - 1;
                    index = 1;
                    hasCarry = false;

                    if (pendingCR)
                    {
                        pendingCR = false;
                        if (unit == 0x000A)
                        {
                            EmitLine(unitStart + 2);
                            if (_viewportRows.Count >= visibleLines)
                            {
                                _viewportLoaded = true;
                                return true;
                            }

                            continue;
                        }

                        EmitLine(unitStart);
                        if (_viewportRows.Count >= visibleLines)
                        {
                            _viewportLoaded = true;
                            return true;
                        }
                    }

                    if (unit == 0x000D)
                    {
                        pendingCR = true;
                    }
                    else if (unit == 0x000A)
                    {
                        EmitLine(unitStart + 2);
                        if (_viewportRows.Count >= visibleLines)
                        {
                            _viewportLoaded = true;
                            return true;
                        }
                    }
                    else
                    {
                        lineUnits.Add(unit);
                    }
                }

                while (index + 1 < read && _viewportRows.Count < visibleLines)
                {
                    ushort unit = littleEndian
                        ? (ushort)(buffer[index] | (buffer[index + 1] << 8))
                        : (ushort)((buffer[index] << 8) | buffer[index + 1]);
                    long unitStart = chunkStart + index;
                    index += 2;

                    if (pendingCR)
                    {
                        pendingCR = false;
                        if (unit == 0x000A)
                        {
                            EmitLine(unitStart + 2);
                            continue;
                        }

                        EmitLine(unitStart);
                        if (_viewportRows.Count >= visibleLines)
                        {
                            break;
                        }
                    }

                    if (unit == 0x000D)
                    {
                        pendingCR = true;
                    }
                    else if (unit == 0x000A)
                    {
                        EmitLine(unitStart + 2);
                    }
                    else
                    {
                        lineUnits.Add(unit);
                    }
                }

                if (index < read)
                {
                    carry = buffer[index];
                    hasCarry = true;
                }

                absoluteOffset += read;
                if (_viewportRows.Count >= visibleLines)
                {
                    break;
                }
            }

            if (pendingCR && _viewportRows.Count < visibleLines)
            {
                EmitLine(absoluteOffset);
            }
            else if (currentLineStart < _fileSize && _viewportRows.Count < visibleLines)
            {
                EmitLine(_fileSize);
            }

            if (_viewportRows.Count == 0)
            {
                _viewportEndOffset = _topOffset;
            }

            _viewportLoaded = true;
            return true;
        }

        {
            byte[] buffer = new byte[64 * 1024];
            List<byte> lineBytes = new();
            long currentLineStart = _topOffset;
            long absoluteOffset = _topOffset;
            bool pendingCR = false;

            void EmitLine(long endOffset)
            {
                _viewportRows.Add(new ViewportRow(currentLineStart, endOffset, DecodeSingleByteLine(lineBytes)));
                lineBytes.Clear();
                currentLineStart = endOffset;
                _viewportEndOffset = endOffset;
            }

            while (true)
            {
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                long chunkStart = absoluteOffset;
                int index = 0;
                while (index < read && _viewportRows.Count < visibleLines)
                {
                    byte b = buffer[index];
                    long byteOffset = chunkStart + index;

                    if (pendingCR)
                    {
                        pendingCR = false;
                        if (b == 0x0A)
                        {
                            index++;
                            EmitLine(byteOffset + 1);
                            continue;
                        }

                        EmitLine(byteOffset);
                        if (_viewportRows.Count >= visibleLines)
                        {
                            break;
                        }

                        continue;
                    }

                    if (b == 0x0D)
                    {
                        pendingCR = true;
                        index++;
                        continue;
                    }

                    if (b == 0x0A)
                    {
                        index++;
                        EmitLine(byteOffset + 1);
                        continue;
                    }

                    lineBytes.Add(b);
                    index++;
                }

                absoluteOffset += read;
                if (_viewportRows.Count >= visibleLines)
                {
                    break;
                }
            }

            if (pendingCR && _viewportRows.Count < visibleLines)
            {
                EmitLine(absoluteOffset);
            }
            else if (currentLineStart < _fileSize && _viewportRows.Count < visibleLines)
            {
                EmitLine(_fileSize);
            }

            if (_viewportRows.Count == 0)
            {
                _viewportEndOffset = _topOffset;
            }

            _viewportLoaded = true;
            return true;
        }
    }
}
