using System;
using System.Collections.Generic;

internal class ProjectedViewport :
    IViewportReader,
    ISelectableViewportReader,
    IHighlightGroupViewportReader,
    ITextSelectionViewportReader
{
    public const int VisibleSegmentChars = 4096;

    private readonly bool _wrapLongLines;
    private ILogRecordSource _source;
    private readonly List<ProjectedViewportRow> _rows = new();
    private readonly List<string> _currentRows = new();
    private readonly List<IReadOnlyList<string>> _currentCells = new();
    private readonly List<ViewportRowSelectionKey> _selectionKeys = new();
    private readonly List<ViewportTextSegmentKey> _textSegmentKeys = new();
    private int _topVisualIndex;
    private int _visibleLines;
    private bool _projectionIncludesEnd;
    private bool _disposed;

    internal readonly record struct ProjectedViewportRow(
        LogViewportRecord Record,
        int VisualIndex,
        int Start,
        int Length,
        string Text,
        IReadOnlyList<string>? Cells);

    public ProjectedViewport(ILogRecordSource source, bool wrapLongLines)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _wrapLongLines = wrapLongLines;
    }

    public ILogRecordSource Source => _source;
    public string SourceName => _source.SourceName;
    public string EncodingName => _source.EncodingName;
    public long DataOffset => _source.DataOffset;
    public long FileSize => _source.FileSize;
    public long ConfirmedFileSize => _source.ConfirmedFileSize;
    public long TopOffset => _source.TopOffset;
    public long ViewportBytes => _source.ViewportBytes;
    public double ScrollPercentage
    {
        get
        {
            if (!_wrapLongLines ||
                _source is not LogRecordSource ||
                !_source.HasContent ||
                _rows.Count == 0)
            {
                return _source.ScrollPercentage;
            }

            if (_projectionIncludesEnd && (_source.TopOffset > _source.DataOffset || _topVisualIndex > 0))
            {
                return 100d;
            }

            ProjectedViewportRow firstRow = _rows[0];
            double virtualOffset = GetSourcePositionForDisplayCharacter(firstRow.Record, firstRow.Start);
            long contentBytes = Math.Max(1, _source.FileSize - _source.DataOffset);
            double topBytes = Math.Clamp(virtualOffset - _source.DataOffset, 0d, contentBytes);
            return (topBytes * 100d) / contentBytes;
        }
    }
    public bool HasContent => _source.HasContent;
    public bool IsAtEnd => _source.HasContent && _projectionIncludesEnd;
    public bool IsAtConfirmedEnd => _projectionIncludesEnd;
    public long TopRowOrdinal => _source is FilteredLogRecordSource filtered ? filtered.TopRecordOrdinal : 0;
    public long MaxLineNumber => _source is FilteredLogRecordSource filtered ? filtered.MaxLineNumber : 0;
    public IReadOnlyList<string> ColumnHeaders => _source.ColumnHeaders;
    public IReadOnlyList<string> CurrentRows => _currentRows;
    public IReadOnlyList<IReadOnlyList<string>> CurrentCells => _currentCells;
    public IReadOnlyList<ViewportRowSelectionKey> CurrentRowSelectionKeys => _selectionKeys;
    public IReadOnlyList<ViewportTextSegmentKey> CurrentTextSegmentKeys => _textSegmentKeys;

    public void UseCurrentSourceRecords(int count)
    {
        ThrowIfDisposed();
        _visibleLines = Math.Max(1, count);
        _topVisualIndex = FindVisualRowForCharacter(
            _source.CurrentRecords.Count > 0 ? _source.CurrentRecords[0].DisplayText : string.Empty,
            _source.AnchorCharacterIndex);
        BuildProjection();
    }

    public IReadOnlyList<ViewportHighlightGroupKey> CurrentHighlightGroupKeys
    {
        get
        {
            ViewportHighlightGroupKey[] keys = new ViewportHighlightGroupKey[_rows.Count];
            for (int i = 0; i < _rows.Count; i++)
            {
                LogRecordKey key = _rows[i].Record.Key;
                keys[i] = new ViewportHighlightGroupKey(key.StartOffset, key.EndOffset);
            }

            return keys;
        }
    }

    public IReadOnlyList<string> ReadNext(int count)
    {
        ThrowIfDisposed();
        if (count <= 0 || !_source.HasContent)
        {
            return CurrentRows;
        }

        EnsureLoaded(Math.Max(1, _visibleLines));
        if (!_wrapLongLines)
        {
            _source.ReadNextRecords(count);
            _topVisualIndex = 0;
            BuildProjection();
            return CurrentRows;
        }

        int remaining = count;
        while (remaining > 0 && _source.CurrentRecords.Count > 0)
        {
            IReadOnlyList<LogViewportRecord> records = _source.CurrentRecords;
            int targetRecordIndex = -1;
            int targetVisualIndex = 0;
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                int startVisualIndex = recordIndex == 0 ? _topVisualIndex : 0;
                int recordRows = GetVisualRowCount(records[recordIndex].DisplayText) - startVisualIndex;
                if (remaining < recordRows)
                {
                    targetRecordIndex = recordIndex;
                    targetVisualIndex = startVisualIndex + remaining;
                    break;
                }

                remaining -= recordRows;
            }

            if (targetRecordIndex >= 0)
            {
                if (targetRecordIndex > 0)
                {
                    _source.ReadNextRecords(targetRecordIndex);
                }

                _topVisualIndex = targetVisualIndex;
                break;
            }

            if (_source.IsAtEnd)
            {
                int lastRecordIndex = records.Count - 1;
                if (lastRecordIndex > 0)
                {
                    _source.ReadNextRecords(lastRecordIndex);
                }

                IReadOnlyList<LogViewportRecord> finalRecords = _source.CurrentRecords;
                _topVisualIndex = finalRecords.Count == 0
                    ? 0
                    : GetVisualRowCount(finalRecords[0].DisplayText) - 1;
                break;
            }

            long previousTop = _source.TopOffset;
            _source.ReadNextRecords(records.Count);
            if (_source.TopOffset == previousTop)
            {
                break;
            }

            _topVisualIndex = 0;
        }

        BuildProjection();
        return CurrentRows;
    }

    public IReadOnlyList<string> ReadPrevious(int count)
    {
        ThrowIfDisposed();
        if (count <= 0 || !_source.HasContent)
        {
            return CurrentRows;
        }

        EnsureLoaded(Math.Max(1, _visibleLines));
        if (!_wrapLongLines)
        {
            _source.ReadPreviousRecords(count);
            _topVisualIndex = 0;
            BuildProjection();
            return CurrentRows;
        }

        if (count <= _topVisualIndex)
        {
            _topVisualIndex -= count;
            BuildProjection();
            return CurrentRows;
        }

        int remaining = count - _topVisualIndex;
        _topVisualIndex = 0;
        int previousRecordBatch = Math.Max(1, GetSourceWindowCount() - 1);
        while (remaining > 0 && _source.CurrentRecords.Count > 0)
        {
            LogRecordKey previousTopKey = _source.CurrentRecords[0].Key;
            _source.ReadPreviousRecords(Math.Min(remaining, previousRecordBatch));
            IReadOnlyList<LogViewportRecord> records = _source.CurrentRecords;
            int previousTopIndex = FindRecordIndex(records, previousTopKey);
            if (previousTopIndex <= 0)
            {
                _topVisualIndex = 0;
                break;
            }

            bool found = false;
            for (int recordIndex = previousTopIndex - 1; recordIndex >= 0; recordIndex--)
            {
                int recordRows = GetVisualRowCount(records[recordIndex].DisplayText);
                if (remaining <= recordRows)
                {
                    if (recordIndex > 0)
                    {
                        _source.ReadNextRecords(recordIndex);
                    }

                    _topVisualIndex = recordRows - remaining;
                    found = true;
                    break;
                }

                remaining -= recordRows;
            }

            if (found)
            {
                break;
            }
        }

        BuildProjection();
        return CurrentRows;
    }

    public IReadOnlyList<string> ReadFromPercentage(double percentage, int count)
    {
        ThrowIfDisposed();
        _visibleLines = Math.Max(1, count);
        double requestedPosition = GetSourcePositionForPercentage(percentage);
        if (_wrapLongLines &&
            percentage < 100d &&
            TryPositionWithinLoadedRecord(requestedPosition))
        {
            BuildProjection();
            return CurrentRows;
        }

        _source.ReadFromPercentage(percentage, GetSourceWindowCount());
        SetTopVisualIndexAfterSourceLoad(requestedPosition);
        if (percentage >= 100d)
        {
            AlignToEnd();
        }

        BuildProjection();
        return CurrentRows;
    }

    public IReadOnlyList<string> ReadFromOffset(long offset, int count)
    {
        ThrowIfDisposed();
        _visibleLines = Math.Max(1, count);
        if (_source is LogRecordSource main)
        {
            main.ReadFromOffset(offset, GetSourceWindowCount());
            SetTopVisualIndexAfterSourceLoad(offset);
        }

        BuildProjection();
        return CurrentRows;
    }

    public IReadOnlyList<string> ReadFromRowOrdinal(long ordinal, int count)
    {
        ThrowIfDisposed();
        _visibleLines = Math.Max(1, count);
        if (_source is FilteredLogRecordSource filtered)
        {
            filtered.ReadFromRecordOrdinal(ordinal, GetSourceWindowCount());
        }

        _topVisualIndex = 0;
        BuildProjection();
        return CurrentRows;
    }

    public IReadOnlyList<string> RefreshTail(int count)
    {
        ThrowIfDisposed();
        _visibleLines = Math.Max(1, count);
        if (_source is LogRecordSource main)
        {
            main.RefreshTail(GetSourceWindowCount());
            if (main.IsAtEnd)
            {
                AlignToEnd();
            }
        }

        BuildProjection();
        return CurrentRows;
    }

    public IReadOnlyList<string> ReloadAfterFileChange(int count)
    {
        ThrowIfDisposed();
        _visibleLines = Math.Max(1, count);
        if (_source is LogRecordSource main)
        {
            main.ReloadAfterFileChange(GetSourceWindowCount());
        }
        else if (_source is FilteredLogRecordSource filtered)
        {
            filtered.ReloadAfterFileChange(GetSourceWindowCount());
        }

        _topVisualIndex = Math.Max(0, _topVisualIndex);
        BuildProjection();
        return CurrentRows;
    }

    public void MarkObservedZeroFileSize()
    {
        if (_source is LogRecordSource main)
        {
            main.MarkObservedZeroFileSize();
        }
        else if (_source is FilteredLogRecordSource filtered)
        {
            filtered.MarkObservedZeroFileSize();
        }

        BuildProjection();
    }

    public void ClearObservedZeroFileSize()
    {
        if (_source is LogRecordSource main)
        {
            main.ClearObservedZeroFileSize();
        }
        else if (_source is FilteredLogRecordSource filtered)
        {
            filtered.ClearObservedZeroFileSize();
        }

        BuildProjection();
    }

    public bool RefreshFileSize() => _source is LogRecordSource main
        ? main.RefreshFileSize()
        : false;

    public bool TryGetRowStartOffset(long rowOrdinal, out long startOffset)
    {
        startOffset = 0;
        return _source is FilteredLogRecordSource filtered &&
            filtered.TryGetRecordStartOffset(rowOrdinal, out startOffset);
    }

    public bool TryGetRowOrdinal(ViewportRowSelectionKey key, out long rowOrdinal)
    {
        rowOrdinal = 0;
        return _source is FilteredLogRecordSource filtered &&
            filtered.TryGetRecordOrdinal(new LogRecordKey(key.StartOffset, key.EndOffset, key.SegmentIndex), out rowOrdinal);
    }

    public IReadOnlyList<ViewportSelectedRow> ReadSelectedRows(
        bool selectAll,
        IReadOnlyList<ViewportRowSelectionRange> ranges,
        IReadOnlyList<ViewportRowSelectionKey> excludedKeys)
    {
        ThrowIfDisposed();
        if (!selectAll && ranges.Count == 0)
        {
            return Array.Empty<ViewportSelectedRow>();
        }

        HashSet<LogRecordKey> excluded = new();
        for (int i = 0; i < excludedKeys.Count; i++)
        {
            ViewportRowSelectionKey key = excludedKeys[i];
            excluded.Add(ToRecordKey(key));
        }

        HashSet<LogRecordKey> emitted = new();
        List<ViewportSelectedRow> selected = new();
        if (selectAll)
        {
            AppendSelectedRange(null, null, excluded, emitted, selected);
        }
        else
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                AppendSelectedRange(
                    ToRecordKey(ranges[i].Start),
                    ToRecordKey(ranges[i].End),
                    excluded,
                    emitted,
                    selected);
            }
        }

        selected.Sort((left, right) => left.Key.CompareTo(right.Key));
        return selected;
    }

    public IReadOnlyList<ViewportHighlightGroup> ReadCurrentHighlightGroups()
    {
        ThrowIfDisposed();
        Dictionary<ViewportHighlightGroupKey, string> groups = new();
        IReadOnlyList<LogViewportRecord> records = _source.CurrentRecords;
        for (int i = 0; i < records.Count; i++)
        {
            LogViewportRecord record = records[i];
            var key = new ViewportHighlightGroupKey(record.Key.StartOffset, record.Key.EndOffset);
            groups.TryAdd(key, record.LogicalText);
        }

        ViewportHighlightGroup[] result = new ViewportHighlightGroup[groups.Count];
        int index = 0;
        foreach ((ViewportHighlightGroupKey key, string text) in groups)
        {
            result[index++] = new ViewportHighlightGroup(key, text);
        }

        return result;
    }

    public bool TryReadTextSelectionContext(ViewportTextSegmentKey key, out ViewportTextSelectionContext context)
    {
        ThrowIfDisposed();
        context = default;
        LogViewportRecord? record = FindCurrentRecord(key.GroupKey, key.SegmentIndex);
        if (record is null)
        {
            return false;
        }

        if (!_wrapLongLines)
        {
            IReadOnlyList<TextRange> explicitRows = GetExplicitLineRanges(record.LogicalText);
            if (key.SegmentIndex < 0 || key.SegmentIndex >= explicitRows.Count)
            {
                return false;
            }

            ViewportTextSegmentRange[] explicitSegments = new ViewportTextSegmentRange[explicitRows.Count];
            for (int i = 0; i < explicitRows.Count; i++)
            {
                TextRange range = explicitRows[i];
                explicitSegments[i] = new ViewportTextSegmentRange(
                    new ViewportTextSegmentKey(key.GroupKey, i),
                    range.Start,
                    range.Length);
            }

            context = new ViewportTextSelectionContext(
                key.GroupKey,
                record.LogicalText,
                explicitSegments);
            return true;
        }

        IReadOnlyList<TextRange> ranges = GetVisualRanges(record.DisplayText);
        if (key.SegmentIndex < 0 || key.SegmentIndex >= ranges.Count)
        {
            return false;
        }

        ViewportTextSegmentRange[] segments = new ViewportTextSegmentRange[ranges.Count];
        for (int i = 0; i < ranges.Count; i++)
        {
            TextRange range = ranges[i];
            segments[i] = new ViewportTextSegmentRange(
                new ViewportTextSegmentKey(key.GroupKey, i),
                range.Start,
                range.Length);
        }

        context = new ViewportTextSelectionContext(key.GroupKey, record.DisplayText, segments);
        return true;
    }

    public ProjectedViewport CloneForWorker()
    {
        ThrowIfDisposed();
        ProjectedViewport clone = CreateClone(_source.CloneForWorker());
        clone._topVisualIndex = _topVisualIndex;
        clone._visibleLines = _visibleLines;
        clone._projectionIncludesEnd = _projectionIncludesEnd;
        clone.BuildProjection();
        return clone;
    }

    protected virtual ProjectedViewport CreateClone(ILogRecordSource source)
    {
        return new ProjectedViewport(source, _wrapLongLines);
    }

    IViewportReader IViewportReader.CloneForWorker() => CloneForWorker();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source.Dispose();
        _rows.Clear();
        _currentRows.Clear();
        _currentCells.Clear();
        _selectionKeys.Clear();
        _textSegmentKeys.Clear();
        _disposed = true;
    }

    private void EnsureLoaded(int visibleLines)
    {
        if (_source.CurrentRecords.Count == 0 && _source.HasContent)
        {
            _source.ReadFromPercentage(0d, Math.Max(2, visibleLines + 1));
            _topVisualIndex = 0;
        }

        _visibleLines = Math.Max(1, visibleLines);
    }

    private void BuildProjection()
    {
        _rows.Clear();
        _currentRows.Clear();
        _currentCells.Clear();
        _selectionKeys.Clear();
        _textSegmentKeys.Clear();
        _projectionIncludesEnd = false;
        if (!_source.HasContent || _source.CurrentRecords.Count == 0)
        {
            return;
        }

        int limit = Math.Max(1, _visibleLines);
        IReadOnlyList<LogViewportRecord> records = _source.CurrentRecords;
        bool consumedAllRecords = true;
        for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            LogViewportRecord record = records[recordIndex];
            IReadOnlyList<TextRange> ranges = _wrapLongLines
                ? GetVisualRanges(record.DisplayText)
                : new[] { new TextRange(0, record.DisplayText.Length) };
            int startVisual = recordIndex == 0 ? Math.Clamp(_topVisualIndex, 0, ranges.Count - 1) : 0;
            for (int visualIndex = startVisual; visualIndex < ranges.Count; visualIndex++)
            {
                if (_rows.Count >= limit)
                {
                    consumedAllRecords = false;
                    break;
                }

                TextRange range = ranges[visualIndex];
                string text = record.DisplayText.Substring(range.Start, range.Length);
                IReadOnlyList<string>? cells = record.Cells;
                _rows.Add(new ProjectedViewportRow(record, visualIndex, range.Start, range.Length, text, cells));
                _currentRows.Add(text);
                if (cells is not null)
                {
                    _currentCells.Add(cells);
                }

                int selectionIndex = _wrapLongLines ? 0 : record.Key.ExplicitRowIndex;
                _selectionKeys.Add(new ViewportRowSelectionKey(
                    record.Key.StartOffset,
                    record.Key.EndOffset,
                    selectionIndex));
                int textSegmentIndex = _wrapLongLines ? visualIndex : record.Key.ExplicitRowIndex;
                _textSegmentKeys.Add(new ViewportTextSegmentKey(
                    new ViewportHighlightGroupKey(record.Key.StartOffset, record.Key.EndOffset),
                    textSegmentIndex));
            }

            if (_rows.Count >= limit)
            {
                if (recordIndex < records.Count - 1)
                {
                    consumedAllRecords = false;
                }

                break;
            }
        }

        _projectionIncludesEnd = _source.IsAtEnd && consumedAllRecords;
    }

    private void AlignToEnd()
    {
        IReadOnlyList<LogViewportRecord> records = _source.CurrentRecords;
        if (records.Count == 0)
        {
            _topVisualIndex = 0;
            return;
        }

        int totalVisualRows = 0;
        int[] counts = new int[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            counts[i] = GetVisualRowCount(records[i].DisplayText);
            totalVisualRows += counts[i];
        }

        int target = Math.Max(0, totalVisualRows - Math.Max(1, _visibleLines));
        int recordIndex = 0;
        while (recordIndex < counts.Length - 1 && target >= counts[recordIndex])
        {
            target -= counts[recordIndex++];
        }

        if (recordIndex > 0)
        {
            _source.ReadNextRecords(recordIndex);
        }

        _topVisualIndex = target;
    }

    private void AppendSelectedRange(
        LogRecordKey? start,
        LogRecordKey? end,
        HashSet<LogRecordKey> excluded,
        HashSet<LogRecordKey> emitted,
        List<ViewportSelectedRow> target)
    {
        foreach (LogViewportRecord record in _source.EnumerateRecords(start, end))
        {
            if (excluded.Contains(record.Key) || !emitted.Add(record.Key))
            {
                continue;
            }

            IReadOnlyList<string>? cells = null;
            if (record.Cells is not null && record.Cells.Count > 1)
            {
                string[] copy = new string[record.Cells.Count - 1];
                for (int i = 1; i < record.Cells.Count; i++)
                {
                    copy[i - 1] = record.Cells[i];
                }

                cells = copy;
            }

            int selectionIndex = _wrapLongLines ? 0 : record.Key.ExplicitRowIndex;
            target.Add(new ViewportSelectedRow(
                new ViewportRowSelectionKey(record.Key.StartOffset, record.Key.EndOffset, selectionIndex),
                record.DisplayText,
                cells));
        }
    }

    private LogViewportRecord? FindCurrentRecord(ViewportHighlightGroupKey key, int visualIndex)
    {
        IReadOnlyList<LogViewportRecord> records = _source.CurrentRecords;
        for (int i = 0; i < records.Count; i++)
        {
            LogViewportRecord record = records[i];
            if (record.Key.StartOffset != key.StartOffset || record.Key.EndOffset != key.EndOffset)
            {
                continue;
            }

            if (_wrapLongLines || record.Key.ExplicitRowIndex == visualIndex)
            {
                return record;
            }
        }

        return null;
    }

    private int GetSourceWindowCount()
    {
        int visibleLines = Math.Max(1, _visibleLines);
        return _wrapLongLines ? visibleLines + 1 : visibleLines;
    }

    private int GetVisualRowCount(string text) => _wrapLongLines ? GetVisualRanges(text).Count : 1;

    private double GetSourcePositionForPercentage(double percentage)
    {
        double clamped = Math.Clamp(percentage, 0d, 100d);
        long contentBytes = Math.Max(0, _source.FileSize - _source.DataOffset);
        return _source.DataOffset + ((clamped / 100d) * contentBytes);
    }

    private bool TryPositionWithinLoadedRecord(double sourcePosition)
    {
        IReadOnlyList<LogViewportRecord> records = _source.CurrentRecords;
        for (int i = 0; i < records.Count; i++)
        {
            LogViewportRecord record = records[i];
            if (sourcePosition < record.Key.StartOffset || sourcePosition > record.Key.EndOffset)
            {
                continue;
            }

            int characterIndex = GetDisplayCharacterForSourcePosition(record, sourcePosition);
            int visualIndex = FindVisualRowForCharacter(record.DisplayText, characterIndex);
            if (!CanFillViewportFrom(records, i, visualIndex))
            {
                return false;
            }

            if (i > 0)
            {
                _source.ReadNextRecords(i);
            }

            _topVisualIndex = visualIndex;
            return true;
        }

        return false;
    }

    private bool CanFillViewportFrom(
        IReadOnlyList<LogViewportRecord> records,
        int recordIndex,
        int visualIndex)
    {
        int requiredRows = Math.Max(1, _visibleLines);
        for (int i = recordIndex; i < records.Count; i++)
        {
            int availableRows = GetVisualRowCount(records[i].DisplayText);
            if (i == recordIndex)
            {
                availableRows -= visualIndex;
            }

            requiredRows -= Math.Max(0, availableRows);
            if (requiredRows <= 0)
            {
                return true;
            }
        }

        return _source.IsAtEnd;
    }

    private void SetTopVisualIndexForPosition(double sourcePosition)
    {
        if (_source.CurrentRecords.Count == 0)
        {
            _topVisualIndex = 0;
            return;
        }

        LogViewportRecord record = _source.CurrentRecords[0];
        int characterIndex = GetDisplayCharacterForSourcePosition(record, sourcePosition);
        _topVisualIndex = FindVisualRowForCharacter(record.DisplayText, characterIndex);
    }

    private void SetTopVisualIndexAfterSourceLoad(double sourcePosition)
    {
        if (_source.CurrentRecords.Count == 0)
        {
            _topVisualIndex = 0;
            return;
        }

        if (_source is LogRecordSource main && main.HasExactDisplaySourceMapping)
        {
            _topVisualIndex = FindVisualRowForCharacter(
                _source.CurrentRecords[0].DisplayText,
                _source.AnchorCharacterIndex);
            return;
        }

        SetTopVisualIndexForPosition(sourcePosition);
    }

    private int GetDisplayCharacterForSourcePosition(LogViewportRecord record, double sourcePosition)
    {
        if (_source is LogRecordSource main &&
            main.TryGetDisplayCharacterForSourceOffset(
                record,
                (long)Math.Round(sourcePosition),
                out int characterIndex))
        {
            return characterIndex;
        }

        long recordBytes = Math.Max(1, record.Key.EndOffset - record.Key.StartOffset);
        double relativeBytes = Math.Clamp(sourcePosition - record.Key.StartOffset, 0d, recordBytes);
        return (int)Math.Round((relativeBytes / recordBytes) * record.DisplayText.Length);
    }

    private double GetSourcePositionForDisplayCharacter(LogViewportRecord record, int characterIndex)
    {
        if (_source is LogRecordSource main &&
            main.TryGetSourceOffsetForDisplayCharacter(record, characterIndex, out long sourceOffset))
        {
            return sourceOffset;
        }

        int textLength = Math.Max(1, record.DisplayText.Length);
        int boundedCharacter = Math.Clamp(characterIndex, 0, record.DisplayText.Length);
        double fraction = boundedCharacter / (double)textLength;
        long recordBytes = Math.Max(0, record.Key.EndOffset - record.Key.StartOffset);
        return record.Key.StartOffset + (recordBytes * fraction);
    }

    private static int FindRecordIndex(IReadOnlyList<LogViewportRecord> records, LogRecordKey key)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindVisualRowForCharacter(string text, int characterIndex)
    {
        IReadOnlyList<TextRange> ranges = GetVisualRanges(text);
        int bounded = Math.Clamp(characterIndex, 0, text.Length);
        for (int i = 0; i < ranges.Count; i++)
        {
            TextRange range = ranges[i];
            if (range.Length == 0 && bounded == range.Start)
            {
                return i;
            }

            if (bounded < range.Start + range.Length)
            {
                return i;
            }
        }

        return Math.Max(0, ranges.Count - 1);
    }

    private readonly record struct TextRange(int Start, int Length);

    private static IReadOnlyList<TextRange> GetVisualRanges(string text)
    {
        return GetTextRanges(text, wrapLongLines: true);
    }

    private static IReadOnlyList<TextRange> GetExplicitLineRanges(string text)
    {
        return GetTextRanges(text, wrapLongLines: false);
    }

    private static IReadOnlyList<TextRange> GetTextRanges(string text, bool wrapLongLines)
    {
        List<TextRange> ranges = new();
        int lineStart = 0;
        while (true)
        {
            int lineEnd = FindLineEnd(text, lineStart);
            int lineLength = lineEnd - lineStart;
            if (lineLength == 0)
            {
                ranges.Add(new TextRange(lineStart, 0));
            }
            else if (wrapLongLines)
            {
                for (int start = lineStart; start < lineEnd; start += VisibleSegmentChars)
                {
                    ranges.Add(new TextRange(start, Math.Min(VisibleSegmentChars, lineEnd - start)));
                }
            }
            else
            {
                ranges.Add(new TextRange(lineStart, lineLength));
            }

            if (lineEnd >= text.Length)
            {
                return ranges;
            }

            lineStart = text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n'
                ? lineEnd + 2
                : lineEnd + 1;
        }
    }

    private static int FindLineEnd(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n')
            {
                return i;
            }
        }

        return text.Length;
    }

    private static LogRecordKey ToRecordKey(ViewportRowSelectionKey key) =>
        new(key.StartOffset, key.EndOffset, key.SegmentIndex);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProjectedViewport));
        }
    }
}

internal sealed class FilteredProjectedViewport :
    ProjectedViewport,
    IColumnViewportReader,
    ILineNumberColumnViewportReader,
    IFileOffsetViewportReader,
    IRowOrdinalViewportReader
{
    public FilteredProjectedViewport(FilteredLogRecordSource source)
        : base(source, wrapLongLines: false)
    {
    }

    protected override ProjectedViewport CreateClone(ILogRecordSource source)
    {
        return new FilteredProjectedViewport((FilteredLogRecordSource)source);
    }
}
