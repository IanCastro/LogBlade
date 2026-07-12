using System;
using System.Collections.Generic;

public readonly record struct LogRecordKey(
    long StartOffset,
    long EndOffset,
    int ExplicitRowIndex = 0) : IComparable<LogRecordKey>
{
    public int CompareTo(LogRecordKey other)
    {
        int start = StartOffset.CompareTo(other.StartOffset);
        if (start != 0)
        {
            return start;
        }

        int row = ExplicitRowIndex.CompareTo(other.ExplicitRowIndex);
        return row != 0 ? row : EndOffset.CompareTo(other.EndOffset);
    }
}

public sealed record LogViewportRecord(
    LogRecordKey Key,
    long NextOffset,
    string DisplayText,
    string LogicalText,
    IReadOnlyList<string>? Cells = null,
    long GroupStartOffset = -1);

public interface ILogRecordSource : IDisposable
{
    string SourceName { get; }
    string EncodingName { get; }
    long DataOffset { get; }
    long FileSize { get; }
    long ConfirmedFileSize { get; }
    long TopOffset { get; }
    long ViewportBytes { get; }
    double ScrollPercentage { get; }
    bool HasContent { get; }
    bool IsAtEnd { get; }
    int AnchorCharacterIndex { get; }
    IReadOnlyList<string> ColumnHeaders { get; }
    IReadOnlyList<LogViewportRecord> CurrentRecords { get; }
    IReadOnlyList<LogViewportRecord> ReadNextRecords(int count);
    IReadOnlyList<LogViewportRecord> ReadPreviousRecords(int count);
    IReadOnlyList<LogViewportRecord> ReadFromPercentage(double percentage, int count);
    IEnumerable<LogViewportRecord> EnumerateRecords(LogRecordKey? start, LogRecordKey? end);
    ILogRecordSource CloneForWorker();
}
