using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class FilteredLogRecordSource : ILogRecordSource
{
    private readonly LogContentSource _contentSource;
    private LogEncodingKind _kind;
    private Encoding _encoding;
    private long _dataOffset;
    private long _fileSize;
    private readonly DisplayParserRule? _displayParserRule;
    private FilteredLineDescriptor[] _descriptors;
    private string[] _columnHeaders;
    private readonly List<LogViewportRecord> _currentRecords = new();
    private bool _observedZeroFileSize;
    private int _captureGroupCount;
    private string[] _captureGroupHeaders;
    private long _topRecordOrdinal;
    private long _viewportBytes;
    private int _recordWindowCount;
    private long _totalLineCount;
    private long _parserRescanOffset;
    private long _parserRescanLineNumber;
    private bool _viewportLoaded;
    private bool _disposed;

    internal FilteredLogRecordSource(
        LogContentSource contentSource,
        LogEncodingKind kind,
        Encoding encoding,
        long dataOffset,
        long fileSize,
        IReadOnlyList<FilteredLineDescriptor> descriptors,
        long totalLineCount = 0,
        DisplayParserRule? displayParserRule = null,
        long parserRescanOffset = -1,
        long parserRescanLineNumber = 0,
        IReadOnlyList<string>? columnHeaders = null)
    {
        _contentSource = contentSource;
        _kind = kind;
        _encoding = encoding;
        _dataOffset = dataOffset;
        _fileSize = fileSize;
        _displayParserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        _parserRescanOffset = parserRescanOffset >= dataOffset ? parserRescanOffset : fileSize;
        _parserRescanLineNumber = parserRescanLineNumber > 0 ? parserRescanLineNumber : totalLineCount + 1;
        _descriptors = new FilteredLineDescriptor[descriptors.Count];
        _captureGroupHeaders = CopyExpectedCaptureGroupHeaders(columnHeaders);
        _captureGroupCount = _captureGroupHeaders.Length;

        for (int i = 0; i < descriptors.Count; i++)
        {
            _descriptors[i] = descriptors[i];
            FilteredCaptureGroups? captureGroups = descriptors[i].CaptureGroups;
            int captureGroupCount = captureGroups?.Values.Length ?? 0;
            if (_captureGroupHeaders.Length == 0 && captureGroupCount > 0)
            {
                _captureGroupHeaders = CopyCaptureGroupHeaders(captureGroups!.Value.Headers, captureGroupCount);
            }

            _captureGroupCount = Math.Max(_captureGroupCount, captureGroupCount);
            _totalLineCount = Math.Max(_totalLineCount, descriptors[i].LineNumber);
        }

        _totalLineCount = Math.Max(_totalLineCount, totalLineCount);
        _columnHeaders = new string[_captureGroupCount + 2];
        _columnHeaders[0] = "#";
        _columnHeaders[1] = "Text";
        for (int i = 0; i < _captureGroupCount; i++)
        {
            _columnHeaders[i + 2] = i < _captureGroupHeaders.Length && !string.IsNullOrEmpty(_captureGroupHeaders[i])
                ? _captureGroupHeaders[i]
                : i.ToString();
        }
    }

    public string SourceName => _contentSource.DisplayName;
    public string EncodingName => LogFileUtilities.DescribeEncoding(_kind);
    public long DataOffset => _dataOffset;
    public long FileSize => _observedZeroFileSize ? 0 : _fileSize;
    public long ConfirmedFileSize => _fileSize;
    public long TopOffset => HasContent && _descriptors.Length > 0
        ? _descriptors[(int)Math.Clamp(_topRecordOrdinal, 0, _descriptors.Length - 1)].StartOffset
        : _dataOffset;
    public long ViewportBytes => _observedZeroFileSize ? 0 : _viewportBytes;
    public double ScrollPercentage
    {
        get
        {
            if (!HasContent)
            {
                return 0d;
            }

            long maxTop = Math.Max(0, _descriptors.LongLength - Math.Max(1, _recordWindowCount));
            return maxTop == 0 ? 0d : (_topRecordOrdinal * 100d) / maxTop;
        }
    }
    public bool HasContent => !_observedZeroFileSize && _descriptors.Length > 0;
    public bool IsAtEnd => !HasContent || IsAtConfirmedEnd;
    public bool IsAtConfirmedEnd
    {
        get
        {
            if (_descriptors.Length == 0)
            {
                return true;
            }

            long maxTop = Math.Max(0, _descriptors.LongLength - Math.Max(1, _recordWindowCount));
            return _viewportLoaded && _topRecordOrdinal >= maxTop;
        }
    }
    public int AnchorCharacterIndex => 0;
    public IReadOnlyList<string> ColumnHeaders => (string[])_columnHeaders.Clone();
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
    public IReadOnlyList<IReadOnlyList<string>> CurrentCells
    {
        get
        {
            if (_observedZeroFileSize)
            {
                return Array.Empty<IReadOnlyList<string>>();
            }

            IReadOnlyList<string>[] rows = new IReadOnlyList<string>[_currentRecords.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = _currentRecords[i].Cells ?? Array.Empty<string>();
            }

            return rows;
        }
    }
    public long TopRecordOrdinal => _topRecordOrdinal;
    public long MatchedLineCount => _observedZeroFileSize ? 0 : _descriptors.LongLength;
    public long MaxLineNumber => _totalLineCount;

    internal LogEncodingKind Kind => _kind;
    internal Encoding SourceEncoding => _encoding;
    internal LogContentSource ContentSource => _contentSource;
    internal long TotalLineCount => _totalLineCount;
    internal long ParserRescanOffset => _parserRescanOffset;
    internal long ParserRescanLineNumber => _parserRescanLineNumber;

    public IReadOnlyList<LogViewportRecord> ReadNextRecords(int count)
    {
        ThrowIfDisposed();
        count = Math.Max(1, count);
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        if (!_viewportLoaded)
        {
            LoadViewportAtRecord(0, count);
            return CurrentRecords;
        }

        long maxTop = Math.Max(0, _descriptors.LongLength - Math.Max(1, _recordWindowCount));
        LoadViewportAtRecord(
            Math.Min(maxTop, _topRecordOrdinal + count),
            _recordWindowCount,
            reuseExisting: true);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadPreviousRecords(int count)
    {
        ThrowIfDisposed();
        count = Math.Max(1, count);
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        if (!_viewportLoaded)
        {
            LoadViewportAtRecord(0, count);
            return CurrentRecords;
        }

        LoadViewportAtRecord(
            Math.Max(0, _topRecordOrdinal - count),
            _recordWindowCount,
            reuseExisting: true);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadFromPercentage(double percentage, int count)
    {
        ThrowIfDisposed();
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
        long maxTop = Math.Max(0, _descriptors.LongLength - count);
        long nextTop = clamped >= 100d
            ? maxTop
            : (long)((clamped / 100d) * maxTop);
        LoadViewportAtRecord(nextTop, count);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadFromRecordOrdinal(long ordinal, int count)
    {
        ThrowIfDisposed();
        count = Math.Max(1, count);
        if (_observedZeroFileSize)
        {
            return CurrentRecords;
        }

        LoadViewportAtRecord(ordinal, count);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReloadAfterFileChange(int count)
    {
        ThrowIfDisposed();
        count = Math.Max(1, count);
        long previousTop = _topRecordOrdinal;
        bool wasAtEnd = IsAtConfirmedEnd;
        try
        {
            long currentSize = _contentSource.Length;
            if (currentSize == 0 && _fileSize > _dataOffset)
            {
                MarkObservedZeroFileSize();
                return CurrentRecords;
            }

            _observedZeroFileSize = false;
            _fileSize = currentSize;
        }
        catch (IOException)
        {
            return CurrentRecords;
        }
        catch (UnauthorizedAccessException)
        {
            return CurrentRecords;
        }

        return wasAtEnd
            ? ReadFromPercentage(100d, count)
            : ReadFromRecordOrdinal(previousTop, count);
    }

    public void MarkObservedZeroFileSize()
    {
        ThrowIfDisposed();
        _observedZeroFileSize = true;
    }

    public void ClearObservedZeroFileSize()
    {
        ThrowIfDisposed();
        _observedZeroFileSize = false;
    }

    public bool TryGetRecordStartOffset(long ordinal, out long startOffset)
    {
        ThrowIfDisposed();
        startOffset = 0;
        if (ordinal < 0 || ordinal >= _descriptors.LongLength)
        {
            return false;
        }

        FilteredLineDescriptor descriptor = _descriptors[(int)ordinal];
        using Stream fs = LogFileUtilities.OpenSourceStream(_contentSource);
        FilteredLineUtilities.ValidateLineRange(fs, _encoding, descriptor.StartOffset, descriptor.EndOffset);
        startOffset = descriptor.StartOffset;
        return true;
    }

    public bool TryGetRecordOrdinal(LogRecordKey key, out long ordinal)
    {
        ThrowIfDisposed();
        int low = 0;
        int high = _descriptors.Length - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            int comparison = CompareDescriptorToKey(_descriptors[mid], key);
            if (comparison < 0)
            {
                low = mid + 1;
            }
            else if (comparison > 0)
            {
                high = mid - 1;
            }
            else
            {
                ordinal = mid;
                return true;
            }
        }

        ordinal = 0;
        return false;
    }

    public IEnumerable<LogViewportRecord> EnumerateRecords(LogRecordKey? start, LogRecordKey? end)
    {
        ThrowIfDisposed();
        using Stream stream = LogFileUtilities.OpenSourceStream(_contentSource);
        byte[] scanBuffer = new byte[SearchRealLineScanner.RequiredBufferBytes];
        DisplayParserRecordSequence? sequence = DisplayParserEvaluator.GetFilterCount(_displayParserRule) == 0
            ? new DisplayParserRecordSequence(_displayParserRule)
            : null;
        DisplayParserFilterPipelineSequence? filterSequence = sequence is null && _displayParserRule is not null
            ? new DisplayParserFilterPipelineSequence(_displayParserRule)
            : null;
        string? cachedLogicalText = null;
        long cachedStart = -1;
        long cachedEnd = -1;
        for (int i = 0; i < _descriptors.Length; i++)
        {
            FilteredLineDescriptor descriptor = _descriptors[i];
            LogRecordKey key = ToRecordKey(descriptor);
            if (start.HasValue && key.CompareTo(start.Value) < 0)
            {
                continue;
            }

            if (end.HasValue && key.CompareTo(end.Value) > 0)
            {
                yield break;
            }

            string logicalText = cachedLogicalText is not null &&
                cachedStart == descriptor.StartOffset &&
                cachedEnd == descriptor.EndOffset
                ? cachedLogicalText
                : ReadDescriptorText(stream, descriptor, sequence, filterSequence, scanBuffer);
            cachedLogicalText = logicalText;
            cachedStart = descriptor.StartOffset;
            cachedEnd = descriptor.EndOffset;
            yield return CreateRecord(descriptor, logicalText);
        }
    }

    public FilteredLogRecordSource CloneForWorker()
    {
        ThrowIfDisposed();
        var clone = new FilteredLogRecordSource(
            _contentSource,
            _kind,
            _encoding,
            _dataOffset,
            _fileSize,
            _descriptors,
            _totalLineCount,
            _displayParserRule,
            _parserRescanOffset,
            _parserRescanLineNumber,
            _columnHeaders)
        {
            _observedZeroFileSize = _observedZeroFileSize,
            _topRecordOrdinal = _topRecordOrdinal,
            _viewportBytes = _viewportBytes,
            _recordWindowCount = _recordWindowCount,
            _viewportLoaded = _viewportLoaded
        };
        clone._currentRecords.AddRange(_currentRecords);
        return clone;
    }

    ILogRecordSource ILogRecordSource.CloneForWorker() => CloneForWorker();

    internal FilteredLineDescriptor[] CopyDescriptorsBefore(long offset)
    {
        ThrowIfDisposed();
        int count = 0;
        while (count < _descriptors.Length && _descriptors[count].StartOffset < offset)
        {
            count++;
        }

        FilteredLineDescriptor[] copy = new FilteredLineDescriptor[count];
        Array.Copy(_descriptors, copy, count);
        return copy;
    }

    internal FilteredLineDescriptor[] CopyDescriptors()
    {
        ThrowIfDisposed();
        return (FilteredLineDescriptor[])_descriptors.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _currentRecords.Clear();
        _descriptors = Array.Empty<FilteredLineDescriptor>();
        _columnHeaders = Array.Empty<string>();
        _captureGroupHeaders = Array.Empty<string>();
        _encoding = Encoding.UTF8;
        _disposed = true;
    }

    private void LoadViewportAtRecord(long topOrdinal, int count, bool reuseExisting = false)
    {
        count = Math.Max(1, count);
        long previousTop = _topRecordOrdinal;
        LogViewportRecord[] previousRecords = reuseExisting
            ? _currentRecords.ToArray()
            : Array.Empty<LogViewportRecord>();
        _recordWindowCount = count;
        _currentRecords.Clear();
        _viewportBytes = 0;
        _viewportLoaded = true;
        if (!HasContent)
        {
            _topRecordOrdinal = 0;
            return;
        }

        long maxTop = Math.Max(0, _descriptors.LongLength - count);
        _topRecordOrdinal = Math.Clamp(topOrdinal, 0, maxTop);
        int firstIndex = (int)_topRecordOrdinal;
        int endIndex = Math.Min(_descriptors.Length, firstIndex + count);
        long firstStart = _descriptors[firstIndex].StartOffset;
        long lastEnd = firstStart;
        string? cachedLogicalText = null;
        long cachedStart = -1;
        long cachedEnd = -1;
        for (int i = firstIndex; i < endIndex; i++)
        {
            long previousIndex = i - previousTop;
            if (previousIndex >= 0 && previousIndex < previousRecords.LongLength)
            {
                LogViewportRecord existing = previousRecords[(int)previousIndex];
                _currentRecords.Add(existing);
                cachedLogicalText = existing.LogicalText;
                cachedStart = existing.Key.StartOffset;
                cachedEnd = existing.Key.EndOffset;
                lastEnd = existing.Key.EndOffset;
                continue;
            }

            FilteredLineDescriptor descriptor = _descriptors[i];
            string logicalText = cachedLogicalText is not null &&
                cachedStart == descriptor.StartOffset &&
                cachedEnd == descriptor.EndOffset
                ? cachedLogicalText
                : ReadDescriptorText(descriptor);
            cachedLogicalText = logicalText;
            cachedStart = descriptor.StartOffset;
            cachedEnd = descriptor.EndOffset;
            _currentRecords.Add(CreateRecord(descriptor, logicalText));
            lastEnd = descriptor.EndOffset;
        }

        _viewportBytes = Math.Max(0, lastEnd - firstStart);
    }

    private LogViewportRecord CreateRecord(FilteredLineDescriptor descriptor, string logicalText)
    {
        string displayText = FilteredLineUtilities.GetExplicitRowText(logicalText, descriptor.ExplicitRowIndex);
        return new LogViewportRecord(
            ToRecordKey(descriptor),
            descriptor.EndOffset,
            displayText,
            logicalText,
            CreateCells(displayText, descriptor));
    }

    private string[] CreateCells(string displayText, FilteredLineDescriptor descriptor)
    {
        string[] cells = new string[_captureGroupCount + 2];
        Array.Fill(cells, string.Empty);
        cells[0] = descriptor.LineNumber > 0 ? descriptor.LineNumber.ToString() : string.Empty;
        cells[1] = displayText;
        string[]? groups = descriptor.CaptureGroups?.Values;
        int groupsToCopy = Math.Min(_captureGroupCount, groups?.Length ?? 0);
        for (int i = 0; i < groupsToCopy; i++)
        {
            cells[i + 2] = groups![i];
        }

        return cells;
    }

    private string ReadDescriptorText(FilteredLineDescriptor descriptor) =>
        DisplayParserRecordEvaluator.ReadRecordText(
            _contentSource,
            _encoding,
            _kind,
            descriptor.StartOffset,
            descriptor.EndOffset,
            descriptor.LineNumber,
            _displayParserRule,
            descriptor.ParserOutputLevel);

    private string ReadDescriptorText(
        Stream stream,
        FilteredLineDescriptor descriptor,
        DisplayParserRecordSequence? sequence,
        DisplayParserFilterPipelineSequence? filterSequence,
        byte[] scanBuffer)
    {
        if (descriptor.ParserOutputLevel >= 0 && filterSequence is not null)
        {
            return DisplayParserRecordEvaluator.ReadPipelineRecordText(
                stream,
                _encoding,
                _kind,
                descriptor.StartOffset,
                descriptor.EndOffset,
                descriptor.LineNumber,
                filterSequence,
                descriptor.ParserOutputLevel,
                scanBuffer);
        }

        return DisplayParserRecordEvaluator.ReadRecordText(
            stream,
            _encoding,
            _kind,
            descriptor.StartOffset,
            descriptor.EndOffset,
            descriptor.LineNumber,
            sequence ?? new DisplayParserRecordSequence(_displayParserRule),
            scanBuffer);
    }

    private static LogRecordKey ToRecordKey(FilteredLineDescriptor descriptor) =>
        new(descriptor.StartOffset, descriptor.EndOffset, descriptor.ExplicitRowIndex);

    private static int CompareDescriptorToKey(FilteredLineDescriptor descriptor, LogRecordKey key) =>
        ToRecordKey(descriptor).CompareTo(key);

    private static string[] CopyCaptureGroupHeaders(IReadOnlyList<string> headers, int count)
    {
        string[] copy = new string[count];
        int copied = Math.Min(headers.Count, count);
        for (int i = 0; i < copied; i++)
        {
            copy[i] = headers[i];
        }

        for (int i = copied; i < count; i++)
        {
            copy[i] = i.ToString();
        }

        return copy;
    }

    private static string[] CopyExpectedCaptureGroupHeaders(IReadOnlyList<string>? columnHeaders)
    {
        if (columnHeaders is null || columnHeaders.Count <= 2)
        {
            return Array.Empty<string>();
        }

        string[] captureHeaders = new string[columnHeaders.Count - 2];
        for (int i = 0; i < captureHeaders.Length; i++)
        {
            captureHeaders[i] = columnHeaders[i + 2];
        }

        return captureHeaders;
    }

    private void SetEmptyViewport(int count)
    {
        _recordWindowCount = Math.Max(1, count);
        _topRecordOrdinal = 0;
        _viewportBytes = 0;
        _currentRecords.Clear();
        _viewportLoaded = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FilteredLogRecordSource));
        }
    }
}
