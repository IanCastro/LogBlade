using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

internal readonly record struct DisplayParserRecord(
    long StartOffset,
    long EndOffset,
    long NextOffset,
    string Text,
    long LineNumber,
    long LastLineNumber,
    long GroupStartOffset);

internal enum JsonAssemblyState
{
    None,
    Incomplete,
    Complete,
    Invalid
}

internal sealed class DisplayParserRecordSequence
{
    internal const int MaximumRecordChars = 16 * 1024 * 1024;
    internal const int MaximumRecordLines = 4096;

    private readonly DisplayParserRule? _rule;
    private readonly int _jsonStageIndex;

    public DisplayParserRecordSequence(DisplayParserRule? rule)
    {
        _rule = DisplayParserEvaluator.CloneRule(rule);
        _jsonStageIndex = DisplayParserEvaluator.FindFirstJsonStageIndex(_rule);
    }

    public long LastLineNumberSeen { get; private set; }
    public long IncompleteRecordStartOffset { get; private set; } = -1;
    public long IncompleteRecordLineNumber { get; private set; }

    public IEnumerable<DisplayParserRecord> Enumerate(
        IEnumerable<RealLineData> source,
        CancellationToken cancellationToken = default)
    {
        if (_rule is null || _jsonStageIndex < 0)
        {
            foreach (RealLineData line in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastLineNumberSeen = line.LineNumber;
                yield return CreateSingle(line, DisplayParserEvaluator.EvaluateOrOriginal(_rule, line.Text));
            }

            yield break;
        }

        List<RealLineData>? bufferedLines = null;
        StringBuilder? bufferedJson = null;
        foreach (RealLineData line in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastLineNumberSeen = line.LineNumber;

            if (bufferedLines is null)
            {
                foreach (DisplayParserRecord record in ProcessUnbufferedLine(line, out bufferedLines, out bufferedJson))
                {
                    yield return record;
                }

                continue;
            }

            if (!TryEvaluatePrefix(line.Text, out string fragment))
            {
                foreach (DisplayParserRecord fallback in FlushOriginals(bufferedLines))
                {
                    yield return fallback;
                }

                ResetBuffer();
                foreach (DisplayParserRecord record in ProcessUnbufferedLine(line, out bufferedLines, out bufferedJson))
                {
                    yield return record;
                }

                continue;
            }

            bufferedLines.Add(line);
            bufferedJson!.Append(fragment);
            if (bufferedLines.Count > MaximumRecordLines || bufferedJson.Length > MaximumRecordChars)
            {
                foreach (DisplayParserRecord fallback in FlushOriginals(bufferedLines))
                {
                    yield return fallback;
                }

                ResetBuffer();
                continue;
            }

            JsonAssemblyState state = GetJsonAssemblyState(bufferedJson.ToString());
            if (state == JsonAssemblyState.Incomplete)
            {
                continue;
            }

            if (state == JsonAssemblyState.Complete &&
                TryEvaluateJsonAndRemaining(bufferedJson.ToString(), out string parsed))
            {
                RealLineData first = bufferedLines[0];
                RealLineData last = bufferedLines[^1];
                yield return new DisplayParserRecord(
                    first.StartOffset,
                    last.EndOffset,
                    GetNextOffset(last),
                    parsed,
                    first.LineNumber,
                    last.LineNumber,
                    first.StartOffset);
            }
            else
            {
                foreach (DisplayParserRecord fallback in FlushOriginals(bufferedLines))
                {
                    yield return fallback;
                }
            }

            ResetBuffer();
        }

        if (bufferedLines is not null)
        {
            IncompleteRecordStartOffset = bufferedLines[0].StartOffset;
            IncompleteRecordLineNumber = bufferedLines[0].LineNumber;
            foreach (DisplayParserRecord fallback in FlushOriginals(bufferedLines))
            {
                yield return fallback;
            }
        }

        List<DisplayParserRecord> ProcessUnbufferedLine(
            RealLineData line,
            out List<RealLineData>? nextBufferedLines,
            out StringBuilder? nextBufferedJson)
        {
            List<DisplayParserRecord> records = new(1);
            nextBufferedLines = null;
            nextBufferedJson = null;
            if (!TryEvaluatePrefix(line.Text, out string fragment))
            {
                records.Add(CreateSingle(line, DisplayParserEvaluator.EvaluateOrOriginal(_rule, line.Text)));
                return records;
            }

            JsonAssemblyState state = GetJsonAssemblyState(fragment);
            if (state == JsonAssemblyState.Incomplete)
            {
                nextBufferedLines = new List<RealLineData> { line };
                nextBufferedJson = new StringBuilder(fragment);
                IncompleteRecordStartOffset = line.StartOffset;
                IncompleteRecordLineNumber = line.LineNumber;
                return records;
            }

            if (state == JsonAssemblyState.Complete &&
                TryEvaluateJsonAndRemaining(fragment, out string parsed))
            {
                records.Add(CreateSingle(line, parsed));
                return records;
            }

            if (state == JsonAssemblyState.Invalid)
            {
                records.Add(CreateSingle(line, line.Text));
                return records;
            }

            records.Add(CreateSingle(line, DisplayParserEvaluator.EvaluateOrOriginal(_rule, line.Text)));
            return records;
        }

        IEnumerable<DisplayParserRecord> FlushOriginals(IReadOnlyList<RealLineData> lines)
        {
            long groupStartOffset = lines.Count > 0 ? lines[0].StartOffset : -1;
            for (int i = 0; i < lines.Count; i++)
            {
                yield return CreateSingle(lines[i], lines[i].Text, groupStartOffset);
            }
        }

        void ResetBuffer()
        {
            bufferedLines = null;
            bufferedJson = null;
            IncompleteRecordStartOffset = -1;
            IncompleteRecordLineNumber = 0;
        }
    }

    private bool TryEvaluatePrefix(string input, out string fragment) =>
        DisplayParserEvaluator.TryEvaluateStageRange(_rule!, 0, _jsonStageIndex, input, out fragment);

    private bool TryEvaluateJsonAndRemaining(string input, out string parsed) =>
        DisplayParserEvaluator.TryEvaluateStageRange(_rule!, _jsonStageIndex, _rule!.Stages.Count, input, out parsed);

    private static DisplayParserRecord CreateSingle(RealLineData line, string text, long groupStartOffset = -1) =>
        new(
            line.StartOffset,
            line.EndOffset,
            GetNextOffset(line),
            text,
            line.LineNumber,
            line.LineNumber,
            groupStartOffset >= 0 ? groupStartOffset : line.StartOffset);

    private static long GetNextOffset(RealLineData line) =>
        line.NextOffset >= 0 ? line.NextOffset : line.EndOffset;

    internal static JsonAssemblyState GetJsonAssemblyState(string input)
    {
        bool foundCandidate = false;
        for (int start = 0; start < input.Length; start++)
        {
            if (input[start] is not ('{' or '['))
            {
                continue;
            }

            foundCandidate = true;
            Stack<char> expectedClosers = new();
            bool inString = false;
            bool escaped = false;
            bool invalid = false;
            for (int i = start; i < input.Length; i++)
            {
                char current = input[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current is '{' or '[')
                {
                    expectedClosers.Push(current == '{' ? '}' : ']');
                    continue;
                }

                if (current is not ('}' or ']'))
                {
                    continue;
                }

                if (expectedClosers.Count == 0 || expectedClosers.Pop() != current)
                {
                    invalid = true;
                    break;
                }

                if (expectedClosers.Count != 0)
                {
                    continue;
                }

                string candidate = input.Substring(start, i - start + 1);
                try
                {
                    using JsonDocument document = JsonDocument.Parse(candidate);
                    return JsonAssemblyState.Complete;
                }
                catch (JsonException)
                {
                    break;
                }
            }

            if (!invalid && (expectedClosers.Count > 0 || inString || escaped))
            {
                return JsonAssemblyState.Incomplete;
            }
        }

        return foundCandidate ? JsonAssemblyState.Invalid : JsonAssemblyState.None;
    }
}

internal static class DisplayParserRecordEvaluator
{
    public static IEnumerable<DisplayParserRecord> Enumerate(
        IEnumerable<RealLineData> source,
        DisplayParserRule? rule,
        CancellationToken cancellationToken = default)
    {
        return new DisplayParserRecordSequence(rule).Enumerate(source, cancellationToken);
    }

    public static string ReadRecordText(
        string filePath,
        Encoding encoding,
        LogEncodingKind kind,
        long startOffset,
        long endOffset,
        long lineNumber,
        DisplayParserRule? rule)
    {
        using (FileStream fs = VisualRowReader.OpenSourceStream(filePath))
        {
            FilteredLineUtilities.ValidateLineRange(fs, encoding, startOffset, endOffset);
        }

        DisplayParserRecordSequence sequence = new(rule);
        foreach (DisplayParserRecord record in sequence.Enumerate(
            SearchRealLineScanner.Enumerate(
                filePath,
                encoding,
                kind,
                startOffset,
                endOffset,
                CancellationToken.None,
                Math.Max(1, lineNumber))))
        {
            if (record.StartOffset == startOffset && record.EndOffset == endOffset)
            {
                return record.Text;
            }

            if (record.StartOffset == startOffset)
            {
                return record.Text;
            }
        }

        return string.Empty;
    }
}
