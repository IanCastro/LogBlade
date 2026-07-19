using System;
using System.Collections.Generic;
using System.Threading;

internal sealed class ParserSearchRecord
{
    public ParserSearchRecord(
        DisplayParserRecord sourceRecord,
        IReadOnlyList<DisplayParserPipelineRow>[] filterOutputs,
        IReadOnlyList<DisplayParserPipelineRow> finalRows)
    {
        SourceRecord = sourceRecord;
        FilterOutputs = filterOutputs;
        FinalRows = finalRows;
    }

    public DisplayParserRecord SourceRecord { get; }
    public IReadOnlyList<DisplayParserPipelineRow>[] FilterOutputs { get; }
    public IReadOnlyList<DisplayParserPipelineRow> FinalRows { get; }
}

internal sealed class ParserSearchRecordSequence
{
    private readonly DisplayParserRecordSequence? _recordSequence;
    private readonly DisplayParserFilterPipelineSequence? _filterSequence;

    public ParserSearchRecordSequence(DisplayParserRule? rule)
    {
        DisplayParserRuntime runtime = new(rule);
        if (runtime.FilterCount > 0)
        {
            _filterSequence = new DisplayParserFilterPipelineSequence(runtime);
            FilterCount = _filterSequence.FilterCount;
        }
        else
        {
            _recordSequence = new DisplayParserRecordSequence(runtime);
        }
    }

    public int FilterCount { get; }
    public long LastLineNumberSeen =>
        _filterSequence?.LastLineNumberSeen ?? _recordSequence?.LastLineNumberSeen ?? 0;
    public long IncompleteRecordStartOffset =>
        _filterSequence?.IncompleteRecordStartOffset ?? _recordSequence?.IncompleteRecordStartOffset ?? -1;
    public long IncompleteRecordLineNumber =>
        _filterSequence?.IncompleteRecordLineNumber ?? _recordSequence?.IncompleteRecordLineNumber ?? 0;

    public IEnumerable<ParserSearchRecord> Enumerate(
        IEnumerable<RealLineData> source,
        CancellationToken cancellationToken = default)
    {
        if (_filterSequence is not null)
        {
            foreach (DisplayParserPipelineRecord record in _filterSequence.Enumerate(source, cancellationToken))
            {
                yield return new ParserSearchRecord(
                    record.SourceRecord,
                    record.FilterOutputs,
                    record.FinalRows);
            }

            yield break;
        }

        foreach (DisplayParserRecord record in _recordSequence!.Enumerate(source, cancellationToken))
        {
            List<DisplayParserPipelineRow> rows = new();
            foreach (ExplicitRowData row in FilteredLineUtilities.EnumerateExplicitRows(record.Text))
            {
                rows.Add(new DisplayParserPipelineRow(row.Text, CaptureGroups: null));
            }

            yield return new ParserSearchRecord(
                record,
                Array.Empty<IReadOnlyList<DisplayParserPipelineRow>>(),
                rows);
        }
    }
}
