using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public readonly record struct SearchProgressUpdate(double ProgressPercentage, long MatchedLineCount, long ElapsedMilliseconds, FilteredVisualRowReader? Reader, bool IsFinal);

public readonly record struct SearchOptions(string Query, bool UseRegex, bool IgnoreCase);

public static class LogSearchBuilder
{
    private const int ProgressPublishIntervalMs = 150;

    public static FilteredVisualRowReader BuildFilteredReader(string filePath, Encoding encoding, long dataOffset, string query)
        => BuildFilteredReader(filePath, encoding, dataOffset, new SearchOptions(query, UseRegex: false, IgnoreCase: true));

    public static void ValidateOptions(SearchOptions options)
    {
        if (options.UseRegex)
        {
            _ = CreateRegex(options);
        }
    }

    public static FilteredVisualRowReader BuildFilteredReader(string filePath, Encoding encoding, long dataOffset, SearchOptions options)
    {
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor> descriptors = new();
        SearchMatcher matcher = SearchMatcher.Create(options);

        foreach (RealLineData line in SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize))
        {
            if (matcher.TryMatch(line.Text, out string[]? captureGroups))
            {
                descriptors.Add(new FilteredLineDescriptor(
                    line.StartOffset,
                    line.EndOffset,
                    FilteredLineUtilities.CountVisualRows(line.Text),
                    captureGroups));
            }
        }

        return new FilteredVisualRowReader(fullPath, kind, encoding, dataOffset, fileSize, descriptors);
    }

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, string query, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, new SearchOptions(query, UseRegex: false, IgnoreCase: true), preloadedVisibleLines, onProgress);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, SearchOptions options, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress)
    {
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor> descriptors = new();
        long searchableBytes = Math.Max(1, fileSize - dataOffset);
        long lastPublishedTick = Environment.TickCount64;
        int lastPublishedDescriptorCount = -1;
        bool partialPublished = false;
        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchMatcher matcher = SearchMatcher.Create(options);

        long scannedOffset = dataOffset;
        foreach (RealLineData line in SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize))
        {
            scannedOffset = line.EndOffset;
            if (matcher.TryMatch(line.Text, out string[]? captureGroups))
            {
                descriptors.Add(new FilteredLineDescriptor(
                    line.StartOffset,
                    line.EndOffset,
                    FilteredLineUtilities.CountVisualRows(line.Text),
                    captureGroups));
            }

            long now = Environment.TickCount64;
            bool firstPartialWithMatches = !partialPublished && descriptors.Count > 0;
            bool intervalElapsed = now - lastPublishedTick >= ProgressPublishIntervalMs;
            if (firstPartialWithMatches || intervalElapsed)
            {
                PublishSnapshot(isFinal: false);
            }
        }

        PublishSnapshot(isFinal: true);
        return;

        void PublishSnapshot(bool isFinal)
        {
            double progress = Math.Clamp(((scannedOffset - dataOffset) * 100d) / searchableBytes, 0d, 100d);
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader? reader = null;
            bool shouldBuildReader = isFinal || descriptors.Count != lastPublishedDescriptorCount;
            if (shouldBuildReader && (descriptors.Count > 0 || isFinal))
            {
                reader = new FilteredVisualRowReader(fullPath, kind, encoding, dataOffset, fileSize, descriptors);
                reader.ReadFromPercentage(0d, Math.Max(1, preloadedVisibleLines));
                lastPublishedDescriptorCount = descriptors.Count;
            }

            onProgress(new SearchProgressUpdate(progress, descriptors.Count, stopwatch.ElapsedMilliseconds, reader, isFinal));
            lastPublishedTick = Environment.TickCount64;
            partialPublished = partialPublished || reader is not null;
        }
    }

    private static Regex CreateRegex(SearchOptions options)
    {
        RegexOptions regexOptions = RegexOptions.CultureInvariant;
        if (options.IgnoreCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        return new Regex(options.Query, regexOptions);
    }

    private readonly struct SearchMatcher
    {
        private readonly Regex? _regex;
        private readonly string _query;
        private readonly StringComparison _literalComparison;
        private readonly int _captureGroupCount;

        private SearchMatcher(Regex regex)
        {
            _regex = regex;
            _query = string.Empty;
            _literalComparison = StringComparison.Ordinal;
            _captureGroupCount = Math.Max(0, regex.GetGroupNumbers().Length - 1);
        }

        private SearchMatcher(string query, StringComparison literalComparison)
        {
            _regex = null;
            _query = query;
            _literalComparison = literalComparison;
            _captureGroupCount = 0;
        }

        public static SearchMatcher Create(SearchOptions options)
        {
            if (options.UseRegex)
            {
                return new SearchMatcher(CreateRegex(options));
            }

            StringComparison comparison = options.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return new SearchMatcher(options.Query, comparison);
        }

        public bool TryMatch(string text, out string[]? captureGroups)
        {
            captureGroups = null;
            if (_regex is not null)
            {
                Match match = _regex.Match(text);
                if (!match.Success)
                {
                    return false;
                }

                if (_captureGroupCount > 0)
                {
                    captureGroups = new string[_captureGroupCount];
                    int groupsToCopy = Math.Min(_captureGroupCount, match.Groups.Count - 1);
                    for (int i = 0; i < groupsToCopy; i++)
                    {
                        Group group = match.Groups[i + 1];
                        captureGroups[i] = group.Success ? group.Value : string.Empty;
                    }

                    for (int i = groupsToCopy; i < captureGroups.Length; i++)
                    {
                        captureGroups[i] = string.Empty;
                    }
                }

                return true;
            }

            return text.IndexOf(_query, _literalComparison) >= 0;
        }
    }
}
