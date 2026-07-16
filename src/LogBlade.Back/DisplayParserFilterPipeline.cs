using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly DisplayParserRule _rule;
    private readonly int[] _filterStageIndices;
    private readonly ParserFilterMatcher[] _filterMatchers;
    private readonly DisplayParserRecordSequence _baseSequence;

    public DisplayParserFilterPipelineSequence(DisplayParserRule rule)
    {
        _rule = rule.Clone();
        List<int> filterStageIndices = new();
        for (int i = 0; i < _rule.Stages.Count; i++)
        {
            if (_rule.Stages[i].Mode == DisplayParserMode.Filter)
            {
                filterStageIndices.Add(i);
            }
        }

        if (filterStageIndices.Count == 0)
        {
            throw new ArgumentException("The parser rule does not contain a Filter stage.", nameof(rule));
        }

        _filterStageIndices = filterStageIndices.ToArray();
        _filterMatchers = new ParserFilterMatcher[_filterStageIndices.Length];
        for (int i = 0; i < _filterMatchers.Length; i++)
        {
            _filterMatchers[i] = new ParserFilterMatcher(_rule.Stages[_filterStageIndices[i]]);
        }

        int prefixStageCount = _filterStageIndices[0];
        DisplayParserRule? prefixRule = null;
        if (prefixStageCount > 0)
        {
            prefixRule = new DisplayParserRule
            {
                Name = _rule.Name,
                Sample = _rule.Sample,
                Stages = _rule.Stages.GetRange(0, prefixStageCount).ConvertAll(stage => stage.Clone())
            };
        }

        _baseSequence = new DisplayParserRecordSequence(prefixRule);
    }

    public int FilterCount => _filterStageIndices.Length;
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
            new IReadOnlyList<DisplayParserPipelineRow>[_filterStageIndices.Length];

        for (int filterIndex = 0; filterIndex < _filterStageIndices.Length; filterIndex++)
        {
            List<DisplayParserPipelineRow> matchedRows = new();
            ParserFilterMatcher matcher = _filterMatchers[filterIndex];
            for (int i = 0; i < currentRows.Count; i++)
            {
                DisplayParserPipelineRow row = currentRows[i];
                if (!matcher.TryInclude(row.Text, out FilteredCaptureGroups? nextCaptureGroups))
                {
                    continue;
                }

                matchedRows.Add(new DisplayParserPipelineRow(
                    row.Text,
                    nextCaptureGroups ?? row.CaptureGroups));
            }

            int segmentStart = _filterStageIndices[filterIndex] + 1;
            int segmentEnd = filterIndex + 1 < _filterStageIndices.Length
                ? _filterStageIndices[filterIndex + 1]
                : _rule.Stages.Count;
            List<DisplayParserPipelineRow> transformedRows = new();
            for (int i = 0; i < matchedRows.Count; i++)
            {
                DisplayParserPipelineRow row = matchedRows[i];
                string transformed = DisplayParserEvaluator.EvaluateStageRangeOrOriginal(
                    _rule,
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

    private sealed class ParserFilterMatcher
    {
        private readonly SearchOptions _options;
        private readonly Regex? _regex;
        private readonly StringComparison _comparison;
        private readonly int[] _captureGroupNumbers;
        private readonly string[] _captureGroupHeaders;

        public ParserFilterMatcher(DisplayParserStage stage)
        {
            _options = new SearchOptions(stage.Rule, stage.UseRegex, stage.IgnoreCase, stage.InvertMatch);
            if (!_options.UseRegex)
            {
                _comparison = _options.IgnoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                _captureGroupNumbers = Array.Empty<int>();
                _captureGroupHeaders = Array.Empty<string>();
                return;
            }

            RegexOptions regexOptions = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
            if (_options.IgnoreCase)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            _regex = new Regex(_options.Query, regexOptions);
            int[] groupNumbers = _regex.GetGroupNumbers();
            string[] groupNames = _regex.GetGroupNames();
            List<int> captureNumbers = new();
            List<string> captureHeaders = new();
            int unnamedCaptureIndex = 0;
            for (int i = 0; i < groupNumbers.Length; i++)
            {
                int groupNumber = groupNumbers[i];
                if (groupNumber == 0)
                {
                    continue;
                }

                captureNumbers.Add(groupNumber);
                string groupName = i < groupNames.Length ? groupNames[i] : groupNumber.ToString();
                if (int.TryParse(groupName, out _))
                {
                    captureHeaders.Add(unnamedCaptureIndex.ToString());
                    unnamedCaptureIndex++;
                }
                else
                {
                    captureHeaders.Add(groupName);
                }
            }

            _captureGroupNumbers = captureNumbers.ToArray();
            _captureGroupHeaders = captureHeaders.ToArray();
        }

        public bool TryInclude(string text, out FilteredCaptureGroups? captureGroups)
        {
            captureGroups = null;
            bool matched;
            if (_regex is null)
            {
                matched = text.IndexOf(_options.Query, _comparison) >= 0;
            }
            else
            {
                Match match = _regex.Match(text);
                matched = match.Success;
                if (matched && !_options.InvertMatch && _captureGroupNumbers.Length > 0)
                {
                    string[] values = new string[_captureGroupNumbers.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        int groupNumber = _captureGroupNumbers[i];
                        Group group = match.Groups[groupNumber];
                        values[i] = group.Success ? group.Value : string.Empty;
                    }

                    captureGroups = new FilteredCaptureGroups(_captureGroupHeaders, values);
                }
            }

            return matched != _options.InvertMatch;
        }
    }
}
