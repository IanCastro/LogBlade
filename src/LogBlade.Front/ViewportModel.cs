using System;
using System.Collections.Generic;

internal readonly record struct ViewportRowSelectionKey(long StartOffset, long EndOffset, int SegmentIndex) : IComparable<ViewportRowSelectionKey>
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

internal readonly record struct ViewportRowSelectionRange(ViewportRowSelectionKey First, ViewportRowSelectionKey Last)
{
    public ViewportRowSelectionKey Start => First.CompareTo(Last) <= 0 ? First : Last;
    public ViewportRowSelectionKey End => First.CompareTo(Last) <= 0 ? Last : First;
    public bool Contains(ViewportRowSelectionKey key) => key.CompareTo(Start) >= 0 && key.CompareTo(End) <= 0;
}

internal readonly record struct ViewportSelectedRow(ViewportRowSelectionKey Key, string Text, IReadOnlyList<string>? Cells = null);
internal readonly record struct ViewportHighlightGroupKey(long StartOffset, long EndOffset);
internal readonly record struct ViewportHighlightGroup(ViewportHighlightGroupKey Key, string Text);
internal readonly record struct ViewportTextSegmentKey(ViewportHighlightGroupKey GroupKey, int SegmentIndex);
internal readonly record struct ViewportTextSegmentRange(ViewportTextSegmentKey Key, int Start, int Length);
internal readonly record struct ViewportTextSelectionContext(
    ViewportHighlightGroupKey GroupKey,
    string Text,
    IReadOnlyList<ViewportTextSegmentRange> Segments);

internal interface IViewportReader : IDisposable
{
    string SourceName { get; }
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

internal interface IColumnViewportReader : IViewportReader
{
    IReadOnlyList<string> ColumnHeaders { get; }
    IReadOnlyList<IReadOnlyList<string>> CurrentCells { get; }
}

internal interface ILineNumberColumnViewportReader : IColumnViewportReader
{
    long MaxLineNumber { get; }
}

internal interface IFileOffsetViewportReader : IViewportReader
{
    long TopRowOrdinal { get; }
    bool TryGetRowStartOffset(long rowOrdinal, out long startOffset);
}

internal interface IRowOrdinalViewportReader : IViewportReader
{
    bool TryGetRowOrdinal(ViewportRowSelectionKey key, out long rowOrdinal);
}

internal interface ISelectableViewportReader : IViewportReader
{
    IReadOnlyList<ViewportRowSelectionKey> CurrentRowSelectionKeys { get; }
    IReadOnlyList<ViewportSelectedRow> ReadSelectedRows(
        bool selectAll,
        IReadOnlyList<ViewportRowSelectionRange> ranges,
        IReadOnlyList<ViewportRowSelectionKey> excludedKeys);
}

internal interface IHighlightGroupViewportReader : IViewportReader
{
    IReadOnlyList<ViewportHighlightGroupKey> CurrentHighlightGroupKeys { get; }
    IReadOnlyList<ViewportHighlightGroup> ReadCurrentHighlightGroups();
}

internal interface ITextSelectionViewportReader : IViewportReader
{
    IReadOnlyList<ViewportTextSegmentKey> CurrentTextSegmentKeys { get; }
    bool TryReadTextSelectionContext(ViewportTextSegmentKey key, out ViewportTextSelectionContext context);
}
