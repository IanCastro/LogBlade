using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class FilteredVisualRowReader : ILineNumberColumnViewportReader, IFileOffsetViewportReader, IRowOrdinalViewportReader, ISelectableViewportReader, IHighlightGroupViewportReader, ITextSelectionViewportReader
{
    private string _filePath;
    private LogEncodingKind _kind;
    private Encoding _encoding;
    private long _dataOffset;
    private long _fileSize;
    private readonly DisplayParserRule? _displayParserRule;
    private FilteredLineDescriptor[] _descriptors;
    private long[] _descriptorRowStarts;
    private string[] _columnHeaders = Array.Empty<string>();
    private readonly List<string> _currentRows = new();
    private readonly List<IReadOnlyList<string>> _currentCells = new();
    private readonly List<ViewportRowSelectionKey> _currentRowSelectionKeys = new();
    private bool _observedZeroFileSize;
    private int _captureGroupCount;
    private string[] _captureGroupHeaders = Array.Empty<string>();
    private long _totalVisualRows;
    private long _topRowOrdinal;
    private long _viewportBytes;
    private int _viewportVisibleLines;
    private int _topDescriptorIndex;
    private long _totalLineCount;
    private long _parserRescanOffset;
    private long _parserRescanLineNumber;
    private bool _viewportLoaded;
    private bool _disposed;

    internal FilteredVisualRowReader(
        string filePath,
        LogEncodingKind kind,
        Encoding encoding,
        long dataOffset,
        long fileSize,
        IReadOnlyList<FilteredLineDescriptor> descriptors,
        long totalLineCount = 0,
        DisplayParserRule? displayParserRule = null,
        long parserRescanOffset = -1,
        long parserRescanLineNumber = 0)
    {
        _filePath = filePath;
        _kind = kind;
        _encoding = encoding;
        _dataOffset = dataOffset;
        _fileSize = fileSize;
        _displayParserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        _parserRescanOffset = parserRescanOffset >= dataOffset ? parserRescanOffset : fileSize;
        _parserRescanLineNumber = parserRescanLineNumber > 0 ? parserRescanLineNumber : totalLineCount + 1;
        _descriptors = new FilteredLineDescriptor[descriptors.Count];
        _descriptorRowStarts = new long[descriptors.Count];

        long runningRows = 0;
        for (int i = 0; i < descriptors.Count; i++)
        {
            _descriptors[i] = descriptors[i];
            _descriptorRowStarts[i] = runningRows;
            runningRows += GetDescriptorVisualRowCount(descriptors[i]);
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

        _totalVisualRows = runningRows;
    }

    public string FilePath => _filePath;
    public string EncodingName => _kind switch
    {
        LogEncodingKind.Utf8 => "UTF-8 BOM",
        LogEncodingKind.Utf16Le => "UTF-16 LE BOM",
        LogEncodingKind.Utf16Be => "UTF-16 BE BOM",
        _ => "Windows-1252"
    };
    public long DataOffset => _dataOffset;
    public long FileSize => _observedZeroFileSize ? 0 : _fileSize;
    public long ConfirmedFileSize => _fileSize;
    public long TopOffset => HasContent ? _descriptors[_topDescriptorIndex].StartOffset : _dataOffset;
    public long TopRowOrdinal => _topRowOrdinal;
    public long MatchedLineCount => _observedZeroFileSize ? 0 : _descriptors.Length;
    public long ViewportBytes => _observedZeroFileSize ? 0 : _viewportBytes;
    public bool IsAtEnd
    {
        get
        {
            if (!HasContent)
            {
                return true;
            }

            long maxTopRow = Math.Max(0, _totalVisualRows - Math.Max(1, _viewportVisibleLines));
            return _viewportLoaded && _topRowOrdinal >= maxTopRow;
        }
    }
    public bool IsAtConfirmedEnd
    {
        get
        {
            if (_descriptors.Length == 0)
            {
                return true;
            }

            long maxTopRow = Math.Max(0, _totalVisualRows - Math.Max(1, _viewportVisibleLines));
            return _viewportLoaded && _topRowOrdinal >= maxTopRow;
        }
    }
    public double ScrollPercentage
    {
        get
        {
            if (!HasContent)
            {
                return 0d;
            }

            long maxTopRow = Math.Max(0, _totalVisualRows - Math.Max(1, _viewportVisibleLines));
            if (maxTopRow <= 0)
            {
                return 0d;
            }

            return (_topRowOrdinal * 100d) / maxTopRow;
        }
    }
    public bool HasContent => !_observedZeroFileSize && _descriptors.Length > 0;
    public IReadOnlyList<string> ColumnHeaders
    {
        get
        {
            string[] headers = new string[_columnHeaders.Length];
            Array.Copy(_columnHeaders, headers, _columnHeaders.Length);
            return headers;
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

            IReadOnlyList<string>[] cells = new IReadOnlyList<string>[_currentCells.Count];
            for (int i = 0; i < _currentCells.Count; i++)
            {
                IReadOnlyList<string> source = _currentCells[i];
                string[] row = new string[source.Count];
                for (int j = 0; j < source.Count; j++)
                {
                    row[j] = source[j];
                }

                cells[i] = row;
            }

            return cells;
        }
    }

    public long MaxLineNumber => _totalLineCount;

    public IReadOnlyList<string> CurrentRows
    {
        get
        {
            if (_observedZeroFileSize)
            {
                return Array.Empty<string>();
            }

            string[] rows = new string[_currentRows.Count];
            for (int i = 0; i < _currentRows.Count; i++)
            {
                rows[i] = _currentRows[i];
            }

            return rows;
        }
    }

    public IReadOnlyList<ViewportRowSelectionKey> CurrentRowSelectionKeys
    {
        get
        {
            if (_observedZeroFileSize)
            {
                return Array.Empty<ViewportRowSelectionKey>();
            }

            ViewportRowSelectionKey[] keys = new ViewportRowSelectionKey[_currentRowSelectionKeys.Count];
            _currentRowSelectionKeys.CopyTo(keys);
            return keys;
        }
    }

    public IReadOnlyList<ViewportHighlightGroupKey> CurrentHighlightGroupKeys
    {
        get
        {
            if (_observedZeroFileSize)
            {
                return Array.Empty<ViewportHighlightGroupKey>();
            }

            ViewportHighlightGroupKey[] keys = new ViewportHighlightGroupKey[_currentRowSelectionKeys.Count];
            for (int i = 0; i < _currentRowSelectionKeys.Count; i++)
            {
                ViewportRowSelectionKey rowKey = _currentRowSelectionKeys[i];
                keys[i] = new ViewportHighlightGroupKey(rowKey.StartOffset, rowKey.EndOffset);
            }

            return keys;
        }
    }

    public IReadOnlyList<ViewportTextSegmentKey> CurrentTextSegmentKeys
    {
        get
        {
            if (_observedZeroFileSize)
            {
                return Array.Empty<ViewportTextSegmentKey>();
            }

            ViewportTextSegmentKey[] keys = new ViewportTextSegmentKey[_currentRowSelectionKeys.Count];
            for (int i = 0; i < _currentRowSelectionKeys.Count; i++)
            {
                ViewportRowSelectionKey rowKey = _currentRowSelectionKeys[i];
                ViewportHighlightGroupKey groupKey = new(rowKey.StartOffset, rowKey.EndOffset);
                keys[i] = new ViewportTextSegmentKey(groupKey, rowKey.SegmentIndex);
            }

            return keys;
        }
    }

    public IReadOnlyList<ViewportSelectedRow> ReadSelectedRows(bool selectAll, IReadOnlyList<ViewportRowSelectionRange> ranges, IReadOnlyList<ViewportRowSelectionKey> excludedKeys)
    {
        ThrowIfDisposed();
        if (!HasContent || (!selectAll && ranges.Count == 0))
        {
            return Array.Empty<ViewportSelectedRow>();
        }

        HashSet<ViewportRowSelectionKey> excluded = new(excludedKeys);
        HashSet<ViewportRowSelectionKey> emitted = new();
        List<ViewportSelectedRow> rows = new();
        for (int i = 0; i < _descriptors.Length; i++)
        {
            FilteredLineDescriptor descriptor = _descriptors[i];
            string text = ReadDescriptorText(descriptor);
            int visualRowCount = GetDescriptorVisualRowCount(descriptor);
            for (int segmentIndex = 0; segmentIndex < visualRowCount; segmentIndex++)
            {
                ViewportRowSelectionKey key = new(descriptor.StartOffset, descriptor.EndOffset, segmentIndex);
                if (excluded.Contains(key) || !emitted.Add(key))
                {
                    continue;
                }

                if (!selectAll && !IsSelectionKeyInRanges(key, ranges))
                {
                    continue;
                }

                string rowText = FilteredLineUtilities.GetExplicitRowText(text, segmentIndex);
                rows.Add(new ViewportSelectedRow(key, rowText, CreateSelectedCells(rowText, descriptor)));
            }
        }

        return rows;
    }

    public IReadOnlyList<ViewportHighlightGroup> ReadCurrentHighlightGroups()
    {
        ThrowIfDisposed();
        if (_observedZeroFileSize || _currentRowSelectionKeys.Count == 0)
        {
            return Array.Empty<ViewportHighlightGroup>();
        }

        HashSet<ViewportHighlightGroupKey> pending = new(CurrentHighlightGroupKeys);
        List<ViewportHighlightGroup> groups = new(pending.Count);
        for (int i = _topDescriptorIndex; i < _descriptors.Length && pending.Count > 0; i++)
        {
            FilteredLineDescriptor descriptor = _descriptors[i];
            ViewportHighlightGroupKey key = new(descriptor.StartOffset, descriptor.EndOffset);
            if (!pending.Remove(key))
            {
                continue;
            }

            groups.Add(new ViewportHighlightGroup(key, ReadDescriptorText(descriptor)));
        }

        return groups;
    }

    public bool TryReadTextSelectionContext(ViewportTextSegmentKey key, out ViewportTextSelectionContext context)
    {
        ThrowIfDisposed();
        context = default;
        ViewportRowSelectionKey rowKey = new(key.GroupKey.StartOffset, key.GroupKey.EndOffset, key.SegmentIndex);
        if (_observedZeroFileSize || !TryGetRowOrdinal(rowKey, out long rowOrdinal))
        {
            return false;
        }

        (int descriptorIndex, _) = MapTopRow(rowOrdinal);
        if (descriptorIndex < 0 || descriptorIndex >= _descriptors.Length)
        {
            return false;
        }

        FilteredLineDescriptor descriptor = _descriptors[descriptorIndex];
        string text = ReadDescriptorText(descriptor);
        int segmentCount = GetDescriptorVisualRowCount(descriptor);
        ViewportTextSegmentRange[] segments = new ViewportTextSegmentRange[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            if (!FilteredLineUtilities.TryGetExplicitRowRange(text, i, out int start, out int length))
            {
                return false;
            }

            segments[i] = new ViewportTextSegmentRange(
                new ViewportTextSegmentKey(key.GroupKey, i),
                start,
                length);
        }

        context = new ViewportTextSelectionContext(key.GroupKey, text, segments);
        return true;
    }

    public IReadOnlyList<string> ReadNext(int count)
    {
        ThrowIfDisposed();
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        if (_observedZeroFileSize)
        {
            return CurrentRows;
        }

        if (!_viewportLoaded)
        {
            LoadViewportAtRow(0, count);
            return CurrentRows;
        }

        int visibleLines = Math.Max(1, _viewportVisibleLines);
        long maxTopRow = Math.Max(0, _totalVisualRows - visibleLines);
        long nextTop = Math.Min(maxTopRow, _topRowOrdinal + count);
        LoadViewportAtRow(nextTop, visibleLines);
        return CurrentRows;
    }

    public IReadOnlyList<string> ReadPrevious(int count)
    {
        ThrowIfDisposed();
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        if (_observedZeroFileSize)
        {
            return CurrentRows;
        }

        if (!_viewportLoaded)
        {
            LoadViewportAtRow(0, count);
            return CurrentRows;
        }

        int visibleLines = Math.Max(1, _viewportVisibleLines);
        long nextTop = Math.Max(0, _topRowOrdinal - count);
        LoadViewportAtRow(nextTop, visibleLines);
        return CurrentRows;
    }

    public IReadOnlyList<string> ReadFromPercentage(double percentage, int count)
    {
        ThrowIfDisposed();
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        if (_observedZeroFileSize)
        {
            return CurrentRows;
        }

        if (!HasContent)
        {
            _viewportVisibleLines = Math.Max(1, count);
            _topRowOrdinal = 0;
            _viewportBytes = 0;
            _currentRows.Clear();
            _currentCells.Clear();
            _viewportLoaded = true;
            return Array.Empty<string>();
        }

        double clamped = Math.Clamp(percentage, 0d, 100d);
        long maxTopRow = Math.Max(0, _totalVisualRows - count);
        long nextTop = clamped >= 100d
            ? maxTopRow
            : Math.Min(maxTopRow, (long)((clamped / 100d) * _totalVisualRows));
        LoadViewportAtRow(nextTop, count);
        return CurrentRows;
    }

    public IReadOnlyList<string> ReadFromRowOrdinal(long topRowOrdinal, int visibleLines)
    {
        ThrowIfDisposed();
        if (visibleLines <= 0)
        {
            return Array.Empty<string>();
        }

        if (_observedZeroFileSize)
        {
            return CurrentRows;
        }

        if (!HasContent)
        {
            _viewportVisibleLines = Math.Max(1, visibleLines);
            _topRowOrdinal = 0;
            _viewportBytes = 0;
            _currentRows.Clear();
            _currentCells.Clear();
            _viewportLoaded = true;
            return Array.Empty<string>();
        }

        LoadViewportAtRow(topRowOrdinal, visibleLines);
        return CurrentRows;
    }

    public IReadOnlyList<string> ReloadAfterFileChange(int visibleLines)
    {
        ThrowIfDisposed();
        if (visibleLines <= 0)
        {
            return Array.Empty<string>();
        }

        long previousTopRowOrdinal = _topRowOrdinal;
        bool wasAtEnd = IsAtConfirmedEnd;
        try
        {
            long currentSize = new FileInfo(_filePath).Length;
            if (currentSize == 0)
            {
                MarkObservedZeroFileSize();
                return CurrentRows;
            }

            _observedZeroFileSize = false;
            _fileSize = currentSize;
        }
        catch (IOException)
        {
            return CurrentRows;
        }
        catch (UnauthorizedAccessException)
        {
            return CurrentRows;
        }

        _viewportLoaded = false;
        if (!HasContent)
        {
            _viewportVisibleLines = Math.Max(1, visibleLines);
            _topRowOrdinal = 0;
            _viewportBytes = 0;
            _currentRows.Clear();
            _currentCells.Clear();
            _currentRowSelectionKeys.Clear();
            _viewportLoaded = true;
            return CurrentRows;
        }

        if (wasAtEnd)
        {
            ReadFromPercentage(100d, visibleLines);
        }
        else
        {
            ReadFromRowOrdinal(previousTopRowOrdinal, visibleLines);
        }

        return CurrentRows;
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

    public bool TryGetRowStartOffset(long rowOrdinal, out long startOffset)
    {
        ThrowIfDisposed();
        startOffset = 0;
        if (rowOrdinal < 0 || rowOrdinal >= _totalVisualRows || _descriptors.Length == 0)
        {
            return false;
        }

        (int descriptorIndex, _) = MapTopRow(rowOrdinal);
        if (descriptorIndex < 0 || descriptorIndex >= _descriptors.Length)
        {
            return false;
        }

        FilteredLineDescriptor descriptor = _descriptors[descriptorIndex];
        using FileStream fs = VisualRowReader.OpenSourceStream(_filePath);
        FilteredLineUtilities.ValidateLineRange(fs, _encoding, descriptor.StartOffset, descriptor.EndOffset);
        startOffset = descriptor.StartOffset;
        return true;
    }

    public bool TryGetRowOrdinal(ViewportRowSelectionKey key, out long rowOrdinal)
    {
        ThrowIfDisposed();
        rowOrdinal = 0;
        if (key.SegmentIndex < 0 || _descriptors.Length == 0)
        {
            return false;
        }

        int low = 0;
        int high = _descriptors.Length - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            long startOffset = _descriptors[mid].StartOffset;
            if (startOffset < key.StartOffset)
            {
                low = mid + 1;
                continue;
            }

            if (startOffset > key.StartOffset)
            {
                high = mid - 1;
                continue;
            }

            FilteredLineDescriptor descriptor = _descriptors[mid];
            if (descriptor.EndOffset != key.EndOffset)
            {
                return false;
            }

            int visualRowCount = GetDescriptorVisualRowCount(descriptor);
            if (key.SegmentIndex >= visualRowCount)
            {
                return false;
            }

            rowOrdinal = _descriptorRowStarts[mid] + key.SegmentIndex;
            return true;
        }

        return false;
    }

    public IViewportReader CloneForWorker()
    {
        ThrowIfDisposed();
        var clone = new FilteredVisualRowReader(
            _filePath,
            _kind,
            _encoding,
            _dataOffset,
            _fileSize,
            _descriptors,
            _totalLineCount,
            _displayParserRule,
            _parserRescanOffset,
            _parserRescanLineNumber)
        {
            _topRowOrdinal = _topRowOrdinal,
            _viewportBytes = _viewportBytes,
            _viewportVisibleLines = _viewportVisibleLines,
            _topDescriptorIndex = _topDescriptorIndex,
            _viewportLoaded = _viewportLoaded,
            _observedZeroFileSize = _observedZeroFileSize
        };
        clone._currentRows.AddRange(_currentRows);
        clone._currentRowSelectionKeys.AddRange(_currentRowSelectionKeys);
        foreach (IReadOnlyList<string> row in _currentCells)
        {
            string[] copy = new string[row.Count];
            for (int i = 0; i < row.Count; i++)
            {
                copy[i] = row[i];
            }

            clone._currentCells.Add(copy);
        }

        return clone;
    }

    internal LogEncodingKind Kind => _kind;
    internal Encoding SourceEncoding => _encoding;
    internal long TotalLineCount => _totalLineCount;
    internal long ParserRescanOffset => _parserRescanOffset;
    internal long ParserRescanLineNumber => _parserRescanLineNumber;

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
        FilteredLineDescriptor[] copy = new FilteredLineDescriptor[_descriptors.Length];
        Array.Copy(_descriptors, copy, _descriptors.Length);
        return copy;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _currentRows.Clear();
        _currentCells.Clear();
        _currentRowSelectionKeys.Clear();
        _descriptors = Array.Empty<FilteredLineDescriptor>();
        _descriptorRowStarts = Array.Empty<long>();
        _columnHeaders = Array.Empty<string>();
        _captureGroupCount = 0;
        _totalVisualRows = 0;
        _totalLineCount = 0;
        _parserRescanOffset = 0;
        _parserRescanLineNumber = 0;
        _topRowOrdinal = 0;
        _viewportBytes = 0;
        _viewportVisibleLines = 0;
        _topDescriptorIndex = 0;
        _viewportLoaded = false;
        _filePath = string.Empty;
        _encoding = Encoding.UTF8;
        _dataOffset = 0;
        _fileSize = 0;
        _kind = LogEncodingKind.Windows1252;
        _disposed = true;
    }

    private void LoadViewportAtRow(long topRowOrdinal, int visibleLines)
    {
        visibleLines = Math.Max(1, visibleLines);
        _viewportVisibleLines = visibleLines;
        _currentRows.Clear();
        _currentCells.Clear();
        _currentRowSelectionKeys.Clear();
        _viewportBytes = 0;
        _viewportLoaded = true;

        if (!HasContent)
        {
            _topRowOrdinal = 0;
            _topDescriptorIndex = 0;
            return;
        }

        long maxTopRow = Math.Max(0, _totalVisualRows - visibleLines);
        _topRowOrdinal = Math.Clamp(topRowOrdinal, 0, maxTopRow);

        (int descriptorIndex, int segmentIndex) = MapTopRow(_topRowOrdinal);
        _topDescriptorIndex = descriptorIndex;

        int currentDescriptorIndex = descriptorIndex;
        long firstStart = _descriptors[descriptorIndex].StartOffset;
        long lastEnd = firstStart;

        while (_currentRows.Count < visibleLines && currentDescriptorIndex < _descriptors.Length)
        {
            FilteredLineDescriptor descriptor = _descriptors[currentDescriptorIndex];
            string text = ReadDescriptorText(descriptor);
            int visualRowCount = GetDescriptorVisualRowCount(descriptor);
            while (_currentRows.Count < visibleLines && segmentIndex < visualRowCount)
            {
                string rowText = FilteredLineUtilities.GetExplicitRowText(text, segmentIndex);
                _currentRows.Add(rowText);
                _currentRowSelectionKeys.Add(new ViewportRowSelectionKey(descriptor.StartOffset, descriptor.EndOffset, segmentIndex));
                _currentCells.Add(CreateCells(rowText, descriptor, includeCaptureGroups: true));
                segmentIndex++;
            }

            lastEnd = descriptor.EndOffset;
            currentDescriptorIndex++;
            segmentIndex = 0;
        }

        _viewportBytes = _currentRows.Count == 0 ? 0 : Math.Max(0, lastEnd - firstStart);
    }

    private string[] CreateCells(string rowText, FilteredLineDescriptor descriptor, bool includeCaptureGroups)
    {
        string[] cells = new string[_captureGroupCount + 2];
        Array.Fill(cells, string.Empty);
        cells[0] = descriptor.LineNumber > 0 ? descriptor.LineNumber.ToString() : string.Empty;
        cells[1] = rowText;

        if (!includeCaptureGroups)
        {
            return cells;
        }

        string[]? captureGroups = descriptor.CaptureGroups?.Values;
        int groupsToCopy = Math.Min(_captureGroupCount, captureGroups?.Length ?? 0);
        for (int i = 0; i < groupsToCopy; i++)
        {
            cells[i + 2] = captureGroups![i];
        }

        return cells;
    }

    private static string[] CopyCaptureGroupHeaders(IReadOnlyList<string> headers, int captureGroupCount)
    {
        string[] copy = new string[captureGroupCount];
        int headersToCopy = Math.Min(headers.Count, copy.Length);
        for (int i = 0; i < headersToCopy; i++)
        {
            copy[i] = headers[i];
        }

        for (int i = headersToCopy; i < copy.Length; i++)
        {
            copy[i] = i.ToString();
        }

        return copy;
    }

    private string[] CreateSelectedCells(string rowText, FilteredLineDescriptor descriptor)
    {
        string[] visibleCells = CreateCells(rowText, descriptor, includeCaptureGroups: true);
        string[] selectedCells = new string[Math.Max(1, visibleCells.Length - 1)];
        Array.Copy(visibleCells, 1, selectedCells, 0, selectedCells.Length);
        return selectedCells;
    }

    private string ReadDescriptorText(FilteredLineDescriptor descriptor) =>
        DisplayParserRecordEvaluator.ReadRecordText(
            _filePath,
            _encoding,
            _kind,
            descriptor.StartOffset,
            descriptor.EndOffset,
            descriptor.LineNumber,
            _displayParserRule);

    private static int GetDescriptorVisualRowCount(FilteredLineDescriptor descriptor) =>
        Math.Max(1, descriptor.VisualRowCount);

    private (int DescriptorIndex, int SegmentIndex) MapTopRow(long rowOrdinal)
    {
        if (_descriptors.Length == 0)
        {
            return (0, 0);
        }

        int index = Array.BinarySearch(_descriptorRowStarts, rowOrdinal);
        if (index < 0)
        {
            index = Math.Max(0, (~index) - 1);
        }

        long rowStart = _descriptorRowStarts[index];
        int segmentIndex = (int)Math.Max(0, rowOrdinal - rowStart);
        return (index, segmentIndex);
    }

    private static bool IsSelectionKeyInRanges(ViewportRowSelectionKey key, IReadOnlyList<ViewportRowSelectionRange> ranges)
    {
        for (int i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
