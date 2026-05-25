using System;
using System.Collections.Generic;

public readonly record struct ViewportRowSelectionKey(long StartOffset, long EndOffset, int SegmentIndex);

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
}
