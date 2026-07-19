using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

internal readonly record struct DisplayParserPipelineRow(
    string Text,
    FilteredCaptureGroups? CaptureGroups);

internal sealed class DisplayParserPipelineRecord
{
    public DisplayParserPipelineRecord(
        DisplayParserRecord sourceRecord,
        IReadOnlyList<DisplayParserPipelineRow>[] filterOutputs)
    {
        SourceRecord = sourceRecord;
        FilterOutputs = filterOutputs;
    }

    public DisplayParserRecord SourceRecord { get; }
    public IReadOnlyList<DisplayParserPipelineRow>[] FilterOutputs { get; }
    public IReadOnlyList<DisplayParserPipelineRow> FinalRows =>
        FilterOutputs.Length == 0
            ? Array.Empty<DisplayParserPipelineRow>()
            : FilterOutputs[^1];
}

internal sealed class DisplayParserFilterPipelineSequence
{
    private readonly DisplayParserRuntime _runtime;
    private readonly DisplayParserRecordSequence _baseSequence;

    public DisplayParserFilterPipelineSequence(DisplayParserRule rule)
        : this(new DisplayParserRuntime(rule))
    {
    }

    internal DisplayParserFilterPipelineSequence(DisplayParserRuntime runtime)
    {
        _runtime = runtime;
        if (runtime.FilterCount == 0)
        {
            throw new ArgumentException("The parser rule does not contain a Filter stage.", nameof(runtime));
        }

        _baseSequence = new DisplayParserRecordSequence(
            runtime,
            runtime.GetFilterStageIndex(0));
    }

    public int FilterCount => _runtime.FilterCount;
    public long LastLineNumberSeen => _baseSequence.LastLineNumberSeen;
    public long IncompleteRecordStartOffset => _baseSequence.IncompleteRecordStartOffset;
    public long IncompleteRecordLineNumber => _baseSequence.IncompleteRecordLineNumber;

    public IEnumerable<DisplayParserPipelineRecord> Enumerate(
        IEnumerable<RealLineData> source,
        CancellationToken cancellationToken = default)
    {
        foreach (DisplayParserRecord record in _baseSequence.Enumerate(source, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return EvaluateRecord(record);
        }
    }

    private DisplayParserPipelineRecord EvaluateRecord(DisplayParserRecord record)
    {
        List<DisplayParserPipelineRow> currentRows = SplitRows(record.Text, captureGroups: null);
        IReadOnlyList<DisplayParserPipelineRow>[] outputs =
            new IReadOnlyList<DisplayParserPipelineRow>[_runtime.FilterCount];

        for (int filterIndex = 0; filterIndex < _runtime.FilterCount; filterIndex++)
        {
            List<DisplayParserPipelineRow> matchedRows = new();
            int filterStageIndex = _runtime.GetFilterStageIndex(filterIndex);
            for (int i = 0; i < currentRows.Count; i++)
            {
                DisplayParserPipelineRow row = currentRows[i];
                if (!_runtime.TryIncludeFilter(
                    filterStageIndex,
                    row.Text,
                    out FilteredCaptureGroups? nextCaptureGroups))
                {
                    continue;
                }

                matchedRows.Add(new DisplayParserPipelineRow(
                    row.Text,
                    nextCaptureGroups ?? row.CaptureGroups));
            }

            int segmentStart = filterStageIndex + 1;
            int segmentEnd = filterIndex + 1 < _runtime.FilterCount
                ? _runtime.GetFilterStageIndex(filterIndex + 1)
                : _runtime.StageCount;
            List<DisplayParserPipelineRow> transformedRows = new();
            for (int i = 0; i < matchedRows.Count; i++)
            {
                DisplayParserPipelineRow row = matchedRows[i];
                string transformed = _runtime.EvaluateStageRangeOrOriginal(
                    segmentStart,
                    segmentEnd,
                    row.Text);
                transformedRows.AddRange(SplitRows(transformed, row.CaptureGroups));
            }

            outputs[filterIndex] = transformedRows;
            currentRows = transformedRows;
        }

        return new DisplayParserPipelineRecord(record, outputs);
    }

    private static List<DisplayParserPipelineRow> SplitRows(
        string text,
        FilteredCaptureGroups? captureGroups)
    {
        List<DisplayParserPipelineRow> rows = new();
        foreach (ExplicitRowData row in FilteredLineUtilities.EnumerateExplicitRows(text))
        {
            rows.Add(new DisplayParserPipelineRow(row.Text, captureGroups));
        }

        return rows;
    }
}
