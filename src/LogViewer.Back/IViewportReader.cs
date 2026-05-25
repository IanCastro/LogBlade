using System;
using System.Collections.Generic;

public readonly record struct ViewportRowSelectionKey(long StartOffset, long EndOffset, int SegmentIndex) : IComparable<ViewportRowSelectionKey>
{
    public int CompareTo(ViewportRowSelectionKey other)
    {
        int start = StartOffset.CompareTo(other.StartOffset);
        if (start != 0)
        {
            return start;
        }

        int segment = SegmentIndex.CompareTo(other.SegmentIndex);
        return segment != 0 ? segment : EndOffset.CompareTo(other.EndOffset);
    }
}

public readonly record struct ViewportRowSelectionRange(ViewportRowSelectionKey First, ViewportRowSelectionKey Last)
{
    public ViewportRowSelectionKey Start => First.CompareTo(Last) <= 0 ? First : Last;
    public ViewportRowSelectionKey End => First.CompareTo(Last) <= 0 ? Last : First;
    public bool Contains(ViewportRowSelectionKey key) => key.CompareTo(Start) >= 0 && key.CompareTo(End) <= 0;
}

public readonly record struct ViewportSelectedRow(ViewportRowSelectionKey Key, string Text);

public interface IViewportReader : IDisposable
{
    string FilePath { get; }
    string EncodingName { get; }
    long DataOffset { get; }
    long FileSize { get; }
    long TopOffset { get; }
    long ViewportBytes { get; }
    double ScrollPercentage { get; }
    bool HasContent { get; }
    IReadOnlyList<string> CurrentRows { get; }
    IReadOnlyList<string> ReadNext(int count);
    IReadOnlyList<string> ReadPrevious(int count);
    IReadOnlyList<string> ReadFromPercentage(double percentage, int count);
    IViewportReader CloneForWorker();
}

public interface IColumnViewportReader : IViewportReader
{
    IReadOnlyList<string> ColumnHeaders { get; }
    IReadOnlyList<IReadOnlyList<string>> CurrentCells { get; }
}

public interface IFileOffsetViewportReader : IViewportReader
{
    long TopRowOrdinal { get; }
    bool TryGetRowStartOffset(long rowOrdinal, out long startOffset);
}

public interface ISelectableViewportReader : IViewportReader
{
    IReadOnlyList<ViewportRowSelectionKey> CurrentRowSelectionKeys { get; }
    IReadOnlyList<ViewportSelectedRow> ReadSelectedRows(bool selectAll, IReadOnlyList<ViewportRowSelectionRange> ranges, IReadOnlyList<ViewportRowSelectionKey> excludedKeys);
}
