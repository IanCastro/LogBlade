using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

public sealed class LogRecordSource : ILogRecordSource
{
    private readonly LogContentSource _contentSource;
    private readonly LogEncodingKind _kind;
    private readonly Encoding _encoding;
    private readonly long _dataOffset;
    private readonly DisplayParserRule? _displayParserRule;
    private readonly List<LogViewportRecord> _currentRecords = new();
    private long _fileSize;
    private bool _observedZeroFileSize;
    private long _topOffset;
    private long _viewportEndOffset;
    private int _recordWindowCount;
    private int _anchorCharacterIndex;
    private bool _viewportLoaded;

    private LogRecordSource(
        LogContentSource contentSource,
        LogEncodingKind kind,
        Encoding encoding,
        long dataOffset,
        long fileSize,
        DisplayParserRule? displayParserRule)
    {
        _contentSource = contentSource;
        _kind = kind;
        _encoding = encoding;
        _dataOffset = dataOffset;
        _fileSize = fileSize;
        _displayParserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        _topOffset = dataOffset;
        _viewportEndOffset = dataOffset;
    }

    public LogRecordSource(string filePath, Encoding encoding, long dataOffset, DisplayParserRule? displayParserRule = null)
        : this(LogContentSource.FromFile(filePath), encoding, dataOffset, displayParserRule)
    {
    }

    public LogRecordSource(LogContentSource contentSource, Encoding encoding, long dataOffset, DisplayParserRule? displayParserRule = null)
        : this(
            contentSource,
            LogFileUtilities.InferKind(encoding, dataOffset),
            encoding,
            dataOffset,
            contentSource.Length,
            displayParserRule)
    {
    }

    public string SourceName => _contentSource.DisplayName;
    public string EncodingName => LogFileUtilities.DescribeEncoding(_kind);
    public long DataOffset => _dataOffset;
    public long FileSize => _observedZeroFileSize ? 0 : _fileSize;
    public long ConfirmedFileSize => _fileSize;
    public long TopOffset => _observedZeroFileSize ? _dataOffset : _topOffset;
    public long ViewportBytes => _observedZeroFileSize ? 0 : Math.Max(0, _viewportEndOffset - _topOffset);
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
    public bool HasContent => !_observedZeroFileSize && _fileSize > _dataOffset;
    public bool IsAtEnd => !_observedZeroFileSize && _viewportLoaded && _viewportEndOffset >= _fileSize;
    public int AnchorCharacterIndex => _observedZeroFileSize ? 0 : _anchorCharacterIndex;
    public IReadOnlyList<string> ColumnHeaders => Array.Empty<string>();
    public IReadOnlyList<LogViewportRecord> CurrentRecords =>
        _observedZeroFileSize ? Array.Empty<LogViewportRecord>() : _currentRecords;
    public IReadOnlyList<string> CurrentDisplayTexts
    {
        get
        {
            if (_observedZeroFileSize)
            {
                return Array.Empty<string>();
            }

            string[] rows = new string[_currentRecords.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = _currentRecords[i].DisplayText;
            }

            return rows;
        }
    }

    internal LogEncodingKind Kind => _kind;
    internal Encoding SourceEncoding => _encoding;
    internal LogContentSource ContentSource => _contentSource;
    public bool HasExactDisplaySourceMapping => _displayParserRule is null;

    public bool TryGetSourceOffsetForDisplayCharacter(
        LogViewportRecord record,
        int characterIndex,
        out long sourceOffset)
    {
        sourceOffset = record.Key.StartOffset;
        if (_displayParserRule is not null)
        {
            return false;
        }

        int bounded = Math.Clamp(characterIndex, 0, record.DisplayText.Length);
        sourceOffset = Math.Clamp(
            record.Key.StartOffset + _encoding.GetByteCount(record.DisplayText.AsSpan(0, bounded)),
            record.Key.StartOffset,
            record.Key.EndOffset);
        return true;
    }

    public bool TryGetDisplayCharacterForSourceOffset(
        LogViewportRecord record,
        long sourceOffset,
        out int characterIndex)
    {
        characterIndex = 0;
        if (_displayParserRule is not null)
        {
            return false;
        }

        long bounded = Math.Clamp(sourceOffset, record.Key.StartOffset, record.Key.EndOffset);
        characterIndex = Math.Clamp(
            ReadCharacterCount(record.Key.StartOffset, bounded),
            0,
            record.DisplayText.Length);
        return true;
    }

    public IReadOnlyList<LogViewportRecord> ReadNextRecords(int count)
    {
        count = Math.Max(1, count);
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        if (!_viewportLoaded)
        {
            return ReadFromPercentage(0d, count);
        }

        int windowCount = Math.Max(1, _recordWindowCount);
        int remaining = count;
        while (remaining > 0 && _currentRecords.Count > 0)
        {
            if (remaining < _currentRecords.Count)
            {
                _currentRecords.RemoveRange(0, remaining);
                _anchorCharacterIndex = 0;
                FillWindowFromEnd(windowCount);
                return CurrentRecords;
            }

            long nextOffset = _viewportEndOffset;
            if (nextOffset >= _fileSize)
            {
                return CurrentRecords;
            }

            remaining -= _currentRecords.Count;
            LoadFromOffset(nextOffset, windowCount, nextOffset);
        }

        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadPreviousRecords(int count)
    {
        count = Math.Max(1, count);
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        if (!_viewportLoaded || _topOffset <= _dataOffset)
        {
            return ReadFromPercentage(0d, Math.Max(count, _recordWindowCount));
        }

        int windowCount = Math.Max(1, _recordWindowCount);
        List<LogViewportRecord> previous = ReadRecordsBefore(_topOffset, count);
        if (previous.Count == 0)
        {
            return CurrentRecords;
        }

        _currentRecords.InsertRange(0, previous);
        if (_currentRecords.Count > windowCount)
        {
            _currentRecords.RemoveRange(windowCount, _currentRecords.Count - windowCount);
        }

        _anchorCharacterIndex = 0;
        UpdateViewportBounds();
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadFromPercentage(double percentage, int count)
    {
        count = Math.Max(1, count);
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        if (!HasContent)
        {
            SetEmptyViewport(count);
            return CurrentRecords;
        }

        double clamped = Math.Clamp(percentage, 0d, 100d);
        if (clamped >= 100d)
        {
            List<LogViewportRecord> tail = ReadRecordsBefore(_fileSize, count);
            long startOffset = tail.Count > 0 ? tail[0].Key.StartOffset : _dataOffset;
            LoadFromOffset(startOffset, count, startOffset);
            return CurrentRecords;
        }

        long requestedOffset = _dataOffset + (long)(((clamped / 100d) * (_fileSize - _dataOffset)));
        ReadFromOffset(requestedOffset, count);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadFromOffset(long offset, int count)
    {
        count = Math.Max(1, count);
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        if (!HasContent)
        {
            SetEmptyViewport(count);
            return CurrentRecords;
        }

        long bounded = Math.Clamp(offset, _dataOffset, _fileSize);
        long startOffset = LogFileUtilities.FindLineStartContaining(
            _contentSource,
            _kind,
            _dataOffset,
            _fileSize,
            bounded);
        LoadFromOffset(startOffset, count, bounded);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> RefreshTail(int count)
    {
        count = Math.Max(1, count);
        bool wasAtEnd = IsAtConfirmedEnd;
        TryRefreshFileSize();
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        return wasAtEnd ? ReadFromPercentage(100d, count) : CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReloadAfterFileChange(int count)
    {
        count = Math.Max(1, count);
        long previousTopOffset = _topOffset;
        bool wasAtEnd = IsAtConfirmedEnd;
        TryRefreshFileSize();
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        if (!HasContent)
        {
            SetEmptyViewport(count);
            return CurrentRecords;
        }

        return wasAtEnd
            ? ReadFromPercentage(100d, count)
            : ReadFromOffset(Math.Min(previousTopOffset, _fileSize), count);
    }

    public void MarkObservedZeroFileSize() => _observedZeroFileSize = true;

    public void ClearObservedZeroFileSize() => _observedZeroFileSize = false;

    public bool RefreshFileSize()
    {
        return RefreshFileSize(out _, out _, out _);
    }

    public bool RefreshFileSize(out long previousSize, out long currentSize)
    {
        return RefreshFileSize(out previousSize, out currentSize, out _);
    }

    public bool RefreshFileSize(
        out long previousSize,
        out long currentSize,
        out bool wasAtEndBeforeRefresh)
    {
        previousSize = _fileSize;
        bool wasVisuallyZero = _observedZeroFileSize;
        wasAtEndBeforeRefresh = IsAtConfirmedEnd;
        try
        {
            currentSize = _contentSource.Length;
        }
        catch (IOException)
        {
            currentSize = _observedZeroFileSize ? 0 : previousSize;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            currentSize = _observedZeroFileSize ? 0 : previousSize;
            return false;
        }

        if (currentSize == 0)
        {
            bool changed = !_observedZeroFileSize;
            _observedZeroFileSize = true;
            return changed;
        }

        _observedZeroFileSize = false;
        if (currentSize < previousSize)
        {
            _fileSize = currentSize;
            return true;
        }

        if (currentSize == previousSize)
        {
            return wasVisuallyZero;
        }

        _fileSize = currentSize;
        return true;
    }

    public IEnumerable<LogViewportRecord> EnumerateRecords(LogRecordKey? start, LogRecordKey? end)
    {
        if (_observedZeroFileSize || _fileSize <= _dataOffset)
        {
            yield break;
        }

        long startOffset = start?.StartOffset ?? _dataOffset;
        foreach (LogViewportRecord record in EnumerateFrom(startOffset, _fileSize))
        {
            if (start.HasValue && record.Key.CompareTo(start.Value) < 0)
            {
                continue;
            }

            if (end.HasValue && record.Key.CompareTo(end.Value) > 0)
            {
                yield break;
            }

            yield return record;
        }
    }

    public LogRecordSource CloneForWorker()
    {
        var clone = new LogRecordSource(
            _contentSource,
            _kind,
            _encoding,
            _dataOffset,
            _fileSize,
            _displayParserRule)
        {
            _observedZeroFileSize = _observedZeroFileSize,
            _topOffset = _topOffset,
            _viewportEndOffset = _viewportEndOffset,
            _recordWindowCount = _recordWindowCount,
            _anchorCharacterIndex = _anchorCharacterIndex,
            _viewportLoaded = _viewportLoaded
        };
        clone._currentRecords.AddRange(_currentRecords);
        return clone;
    }

    ILogRecordSource ILogRecordSource.CloneForWorker() => CloneForWorker();

    public void Dispose()
    {
        _currentRecords.Clear();
    }

    private void LoadFromOffset(long startOffset, int count, long requestedOffset)
    {
        _currentRecords.Clear();
        _recordWindowCount = Math.Max(1, count);
        _anchorCharacterIndex = 0;
        _topOffset = Math.Clamp(startOffset, _dataOffset, _fileSize);
        _viewportEndOffset = _topOffset;
        _viewportLoaded = true;

        foreach (LogViewportRecord record in EnumerateFrom(_topOffset, _fileSize))
        {
            _currentRecords.Add(record);
            _viewportEndOffset = Math.Max(_viewportEndOffset, record.NextOffset);
            if (_currentRecords.Count == 1 &&
                _displayParserRule is null &&
                requestedOffset > record.Key.StartOffset &&
                requestedOffset < record.Key.EndOffset)
            {
                _anchorCharacterIndex = ReadCharacterCount(record.Key.StartOffset, requestedOffset);
            }

            if (_currentRecords.Count >= _recordWindowCount)
            {
                break;
            }
        }

        if (_currentRecords.Count > 0)
        {
            _topOffset = _currentRecords[0].Key.StartOffset;
        }
    }

    private void FillWindowFromEnd(int windowCount)
    {
        long nextOffset = _currentRecords.Count > 0
            ? _currentRecords[^1].NextOffset
            : _viewportEndOffset;
        if (_currentRecords.Count < windowCount && nextOffset < _fileSize)
        {
            foreach (LogViewportRecord record in EnumerateFrom(nextOffset, _fileSize))
            {
                _currentRecords.Add(record);
                if (_currentRecords.Count >= windowCount)
                {
                    break;
                }
            }
        }

        UpdateViewportBounds();
    }

    private void UpdateViewportBounds()
    {
        if (_currentRecords.Count == 0)
        {
            _topOffset = _dataOffset;
            _viewportEndOffset = _dataOffset;
            return;
        }

        _topOffset = _currentRecords[0].Key.StartOffset;
        _viewportEndOffset = _currentRecords[^1].NextOffset;
    }

    private IEnumerable<LogViewportRecord> EnumerateFrom(long startOffset, long endOffset)
    {
        DisplayParserRecordSequence sequence = new(_displayParserRule);
        foreach (DisplayParserRecord record in sequence.Enumerate(
            SearchRealLineScanner.Enumerate(
                _contentSource,
                _encoding,
                _kind,
                startOffset,
                endOffset,
                CancellationToken.None,
                firstLineNumber: 1)))
        {
            yield return new LogViewportRecord(
                new LogRecordKey(record.StartOffset, record.EndOffset),
                record.NextOffset,
                record.Text,
                record.Text);
        }
    }

    private List<LogViewportRecord> ReadRecordsBefore(long beforeOffset, int count)
    {
        count = Math.Max(1, count);
        long scanStart = beforeOffset;
        int physicalLines = 0;
        int targetPhysicalLines = count + 1;
        List<LogViewportRecord> candidates = new();

        while (scanStart > _dataOffset)
        {
            while (physicalLines < targetPhysicalLines && scanStart > _dataOffset)
            {
                long previous = LogFileUtilities.FindPreviousLineStart(
                    _contentSource,
                    _kind,
                    _dataOffset,
                    scanStart);
                if (previous >= scanStart)
                {
                    scanStart = _dataOffset;
                    break;
                }

                scanStart = previous;
                physicalLines++;
            }

            candidates.Clear();
            foreach (LogViewportRecord record in EnumerateFrom(scanStart, beforeOffset))
            {
                if (record.Key.StartOffset < beforeOffset)
                {
                    candidates.Add(record);
                }
            }

            if (candidates.Count > count || scanStart <= _dataOffset)
            {
                break;
            }

            targetPhysicalLines *= 2;
        }

        int take = Math.Min(count, candidates.Count);
        return take == 0
            ? new List<LogViewportRecord>()
            : candidates.GetRange(candidates.Count - take, take);
    }

    private int ReadCharacterCount(long startOffset, long endOffset)
    {
        long remaining = Math.Max(0, endOffset - startOffset);
        if (remaining == 0)
        {
            return 0;
        }

        const int BufferSize = 64 * 1024;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(BufferSize);
        char[] chars = ArrayPool<char>.Shared.Rent(_encoding.GetMaxCharCount(BufferSize));
        try
        {
            using Stream fs = LogFileUtilities.OpenSourceStream(_contentSource);
            fs.Position = startOffset;
            Decoder decoder = _encoding.GetDecoder();
            long characterCount = 0;
            while (remaining > 0)
            {
                int readCount = (int)Math.Min(remaining, BufferSize);
                fs.ReadExactly(bytes.AsSpan(0, readCount));
                int byteIndex = 0;
                while (byteIndex < readCount)
                {
                    decoder.Convert(
                        bytes,
                        byteIndex,
                        readCount - byteIndex,
                        chars,
                        0,
                        chars.Length,
                        flush: false,
                        out int bytesUsed,
                        out int charsUsed,
                        out _);
                    byteIndex += bytesUsed;
                    characterCount += charsUsed;
                }

                remaining -= readCount;
            }

            return (int)Math.Min(characterCount, int.MaxValue);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    private void TryRefreshFileSize()
    {
        try
        {
            RefreshFileSize();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private bool IsAtConfirmedEnd => _viewportLoaded && _viewportEndOffset >= _fileSize;

    private void SetEmptyViewport(int count)
    {
        _recordWindowCount = Math.Max(1, count);
        _anchorCharacterIndex = 0;
        _topOffset = _dataOffset;
        _viewportEndOffset = _dataOffset;
        _currentRecords.Clear();
        _viewportLoaded = true;
    }
}
