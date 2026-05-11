using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class FilteredVisualRowReader : IViewportReader
{
    private readonly string _filePath;
    private readonly LogEncodingKind _kind;
    private readonly Encoding _encoding;
    private readonly long _dataOffset;
    private readonly long _fileSize;
    private readonly FilteredLineDescriptor[] _descriptors;
    private readonly long[] _descriptorRowStarts;
    private readonly List<string> _currentRows = new();
    private readonly long _totalVisualRows;
    private long _topRowOrdinal;
    private long _viewportBytes;
    private int _viewportVisibleLines;
    private int _topDescriptorIndex;
    private bool _viewportLoaded;

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
            runningRows += Math.Max(1, descriptors[i].VisualRowCount);
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
    public long ViewportBytes => _viewportBytes;
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

    public IReadOnlyList<string> ReadNext(int count)
    {
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

    public IViewportReader CloneForWorker()
    {
        var clone = new FilteredVisualRowReader(_filePath, _kind, _encoding, _dataOffset, _fileSize, _descriptors)
        {
            _topRowOrdinal = _topRowOrdinal,
            _viewportBytes = _viewportBytes,
            _viewportVisibleLines = _viewportVisibleLines,
            _topDescriptorIndex = _topDescriptorIndex,
            _viewportLoaded = _viewportLoaded
        };
        clone._currentRows.AddRange(_currentRows);
        return clone;
    }

    public void Dispose()
    {
    }

    private void LoadViewportAtRow(long topRowOrdinal, int visibleLines)
    {
        visibleLines = Math.Max(1, visibleLines);
        _viewportVisibleLines = visibleLines;
        _currentRows.Clear();
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

        using FileStream fs = VisualRowReader.OpenSourceStream(_filePath);
        int currentDescriptorIndex = descriptorIndex;
        int currentSegmentIndex = segmentIndex;
        long firstStart = _descriptors[descriptorIndex].StartOffset;
        long lastEnd = firstStart;

        while (_currentRows.Count < visibleLines && currentDescriptorIndex < _descriptors.Length)
        {
            FilteredLineDescriptor descriptor = _descriptors[currentDescriptorIndex];
            string text = FilteredLineUtilities.ReadLineText(fs, _encoding, descriptor.StartOffset, descriptor.EndOffset);
            for (int i = currentSegmentIndex; i < descriptor.VisualRowCount && _currentRows.Count < visibleLines; i++)
            {
                _currentRows.Add(FilteredLineUtilities.GetVisualRowText(text, i));
                lastEnd = descriptor.EndOffset;
            }

            currentDescriptorIndex++;
            currentSegmentIndex = 0;
        }

        _viewportBytes = _currentRows.Count == 0 ? 0 : Math.Max(0, lastEnd - firstStart);
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
}
