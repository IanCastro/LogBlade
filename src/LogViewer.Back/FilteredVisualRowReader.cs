using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class FilteredVisualRowReader : IColumnViewportReader, IFileOffsetViewportReader, ISelectableViewportReader
{
    private string _filePath;
    private LogEncodingKind _kind;
    private Encoding _encoding;
    private long _dataOffset;
    private long _fileSize;
    private FilteredLineDescriptor[] _descriptors;
    private long[] _descriptorRowStarts;
    private string[] _columnHeaders = Array.Empty<string>();
    private readonly List<string> _currentRows = new();
    private readonly List<IReadOnlyList<string>> _currentCells = new();
    private readonly List<ViewportRowSelectionKey> _currentRowSelectionKeys = new();
    private int _captureGroupCount;
    private long _totalVisualRows;
    private long _topRowOrdinal;
    private long _viewportBytes;
    private int _viewportVisibleLines;
    private int _topDescriptorIndex;
    private bool _viewportLoaded;
    private bool _disposed;

    internal FilteredVisualRowReader(string filePath, LogEncodingKind kind, Encoding encoding, long dataOffset, long fileSize, IReadOnlyList<FilteredLineDescriptor> descriptors)
    {
        _filePath = filePath;
        _kind = kind;
        _encoding = encoding;
        _dataOffset = dataOffset;
        _fileSize = fileSize;
        _descriptors = new FilteredLineDescriptor[descriptors.Count];
        _descriptorRowStarts = new long[descriptors.Count];

        long runningRows = 0;
        for (int i = 0; i < descriptors.Count; i++)
        {
            _descriptors[i] = descriptors[i];
            _descriptorRowStarts[i] = runningRows;
            runningRows++;
            _captureGroupCount = Math.Max(_captureGroupCount, descriptors[i].CaptureGroups?.Length ?? 0);
        }

        if (_captureGroupCount > 0)
        {
            _columnHeaders = new string[_captureGroupCount + 1];
            _columnHeaders[0] = "Text";
            for (int i = 0; i < _captureGroupCount; i++)
            {
                _columnHeaders[i + 1] = i.ToString();
            }
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
    public long FileSize => _fileSize;
    public long TopOffset => HasContent ? _descriptors[_topDescriptorIndex].StartOffset : _dataOffset;
    public long TopRowOrdinal => _topRowOrdinal;
    public long MatchedLineCount => _descriptors.Length;
    public long ViewportBytes => _viewportBytes;
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
    public bool HasContent => _descriptors.Length > 0;
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

    public IReadOnlyList<string> CurrentRows
    {
        get
        {
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
            ViewportRowSelectionKey[] keys = new ViewportRowSelectionKey[_currentRowSelectionKeys.Count];
            _currentRowSelectionKeys.CopyTo(keys);
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
        using FileStream fs = VisualRowReader.OpenSourceStream(_filePath);
        for (int i = 0; i < _descriptors.Length; i++)
        {
            FilteredLineDescriptor descriptor = _descriptors[i];
            ViewportRowSelectionKey key = new(descriptor.StartOffset, descriptor.EndOffset, 0);
            if (excluded.Contains(key) || !emitted.Add(key))
            {
                continue;
            }

            if (!selectAll && !IsSelectionKeyInRanges(key, ranges))
            {
                continue;
            }

            string text = FilteredLineUtilities.ReadLineText(fs, _encoding, descriptor.StartOffset, descriptor.EndOffset);
            rows.Add(new ViewportSelectedRow(key, text));
        }

        return rows;
    }

    public IReadOnlyList<string> ReadNext(int count)
    {
        ThrowIfDisposed();
        if (count <= 0)
        {
            return Array.Empty<string>();
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

    public IViewportReader CloneForWorker()
    {
        ThrowIfDisposed();
        var clone = new FilteredVisualRowReader(_filePath, _kind, _encoding, _dataOffset, _fileSize, _descriptors)
        {
            _topRowOrdinal = _topRowOrdinal,
            _viewportBytes = _viewportBytes,
            _viewportVisibleLines = _viewportVisibleLines,
            _topDescriptorIndex = _topDescriptorIndex,
            _viewportLoaded = _viewportLoaded
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

        (int descriptorIndex, _) = MapTopRow(_topRowOrdinal);
        _topDescriptorIndex = descriptorIndex;

        using FileStream fs = VisualRowReader.OpenSourceStream(_filePath);
        int currentDescriptorIndex = descriptorIndex;
        long firstStart = _descriptors[descriptorIndex].StartOffset;
        long lastEnd = firstStart;

        while (_currentRows.Count < visibleLines && currentDescriptorIndex < _descriptors.Length)
        {
            FilteredLineDescriptor descriptor = _descriptors[currentDescriptorIndex];
            string text = FilteredLineUtilities.ReadLineText(fs, _encoding, descriptor.StartOffset, descriptor.EndOffset);
            _currentRows.Add(text);
            _currentRowSelectionKeys.Add(new ViewportRowSelectionKey(descriptor.StartOffset, descriptor.EndOffset, 0));
            if (_captureGroupCount > 0)
            {
                _currentCells.Add(CreateCells(text, descriptor, includeCaptureGroups: true));
            }

            lastEnd = descriptor.EndOffset;
            currentDescriptorIndex++;
        }

        _viewportBytes = _currentRows.Count == 0 ? 0 : Math.Max(0, lastEnd - firstStart);
    }

    private string[] CreateCells(string rowText, FilteredLineDescriptor descriptor, bool includeCaptureGroups)
    {
        string[] cells = new string[_captureGroupCount + 1];
        Array.Fill(cells, string.Empty);
        cells[0] = rowText;

        if (!includeCaptureGroups)
        {
            return cells;
        }

        string[]? captureGroups = descriptor.CaptureGroups;
        int groupsToCopy = Math.Min(_captureGroupCount, captureGroups?.Length ?? 0);
        for (int i = 0; i < groupsToCopy; i++)
        {
            cells[i + 1] = captureGroups![i];
        }

        return cells;
    }

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
