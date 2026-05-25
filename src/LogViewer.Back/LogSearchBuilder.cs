using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

public readonly record struct SearchProgressUpdate(double ProgressPercentage, long MatchedLineCount, long ElapsedMilliseconds, FilteredVisualRowReader? Reader, bool IsFinal);

public readonly record struct SearchOptions(string Query, bool UseRegex, bool IgnoreCase, bool InvertMatch = false);

public static class LogSearchBuilder
{
    private const int ProgressPublishIntervalMs = 150;
    private const int BackwardScanBlockBytes = 64 * 1024;

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
            AddDescriptorIfIncluded(descriptors, line, matcher, options);
        }

        return new FilteredVisualRowReader(fullPath, kind, encoding, dataOffset, fileSize, descriptors);
    }

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, string query, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, new SearchOptions(query, UseRegex: false, IgnoreCase: true), preloadedVisibleLines, onProgress);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, string query, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress, CancellationToken cancellationToken)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, new SearchOptions(query, UseRegex: false, IgnoreCase: true), preloadedVisibleLines, onProgress, cancellationToken);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, SearchOptions options, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, options, preloadedVisibleLines, onProgress, CancellationToken.None);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, SearchOptions options, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        foreach (RealLineData line in SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedOffset = line.EndOffset;
            AddDescriptorIfIncluded(descriptors, line, matcher, options);

            long now = Environment.TickCount64;
            bool firstPartialWithMatches = !partialPublished && descriptors.Count > 0;
            bool intervalElapsed = now - lastPublishedTick >= ProgressPublishIntervalMs;
            if (firstPartialWithMatches || intervalElapsed)
            {
                PublishSnapshot(isFinal: false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        PublishSnapshot(isFinal: true);
        return;

        void PublishSnapshot(bool isFinal)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            lastPublishedTick = Environment.TickCount64;
            partialPublished = partialPublished || reader is not null;
        }
    }

    public static void BuildAppendedFilteredReaderIncremental(
        FilteredVisualRowReader previousReader,
        SearchOptions options,
        long newFileSize,
        int preloadedVisibleLines,
        Action<SearchProgressUpdate> onProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = previousReader.FilePath;
        long oldFileSize = previousReader.FileSize;
        if (newFileSize <= oldFileSize)
        {
            FilteredVisualRowReader unchanged = new(
                fullPath,
                previousReader.Kind,
                previousReader.SourceEncoding,
                previousReader.DataOffset,
                oldFileSize,
                previousReader.CopyDescriptorsBefore(long.MaxValue));
            unchanged.ReadFromPercentage(0d, Math.Max(1, preloadedVisibleLines));
            onProgress(new SearchProgressUpdate(100d, unchanged.MatchedLineCount, 0, unchanged, IsFinal: true));
            return;
        }

        LogEncodingKind kind = previousReader.Kind;
        Encoding encoding = previousReader.SourceEncoding;
        long dataOffset = previousReader.DataOffset;
        long rescanStartOffset = GetAppendRescanStart(fullPath, kind, dataOffset, oldFileSize);
        List<FilteredLineDescriptor> descriptors = new(previousReader.CopyDescriptorsBefore(rescanStartOffset));
        long searchableBytes = Math.Max(1, newFileSize - rescanStartOffset);
        long scannedOffset = rescanStartOffset;
        long lastPublishedTick = Environment.TickCount64;
        int lastPublishedDescriptorCount = descriptors.Count;
        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchMatcher matcher = SearchMatcher.Create(options);

        foreach (RealLineData line in SearchRealLineScanner.Enumerate(fullPath, encoding, kind, rescanStartOffset, newFileSize, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedOffset = line.EndOffset;
            AddDescriptorIfIncluded(descriptors, line, matcher, options);

            long now = Environment.TickCount64;
            if (now - lastPublishedTick >= ProgressPublishIntervalMs)
            {
                PublishSnapshot(isFinal: false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        PublishSnapshot(isFinal: true);
        return;

        void PublishSnapshot(bool isFinal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double progress = Math.Clamp(((scannedOffset - rescanStartOffset) * 100d) / searchableBytes, 0d, 100d);
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader? reader = null;
            bool shouldBuildReader = isFinal || descriptors.Count != lastPublishedDescriptorCount;
            if (shouldBuildReader)
            {
                reader = new FilteredVisualRowReader(fullPath, kind, encoding, dataOffset, newFileSize, descriptors);
                reader.ReadFromPercentage(0d, Math.Max(1, preloadedVisibleLines));
                lastPublishedDescriptorCount = descriptors.Count;
            }

            onProgress(new SearchProgressUpdate(progress, descriptors.Count, stopwatch.ElapsedMilliseconds, reader, isFinal));
            cancellationToken.ThrowIfCancellationRequested();
            lastPublishedTick = Environment.TickCount64;
        }
    }

    private static long GetAppendRescanStart(string filePath, LogEncodingKind kind, long dataOffset, long oldFileSize)
    {
        if (oldFileSize <= dataOffset)
        {
            return dataOffset;
        }

        using FileStream fs = VisualRowReader.OpenSourceStream(filePath);
        if (EndsWithLineBreak(fs, kind, dataOffset, oldFileSize))
        {
            return oldFileSize;
        }

        return kind switch
        {
            LogEncodingKind.Utf16Le => FindPreviousUtf16LineStart(fs, dataOffset, oldFileSize, littleEndian: true),
            LogEncodingKind.Utf16Be => FindPreviousUtf16LineStart(fs, dataOffset, oldFileSize, littleEndian: false),
            _ => FindPreviousSingleByteLineStart(fs, dataOffset, oldFileSize)
        };
    }

    private static void AddDescriptorIfIncluded(
        List<FilteredLineDescriptor> descriptors,
        RealLineData line,
        SearchMatcher matcher,
        SearchOptions options)
    {
        bool matched = matcher.TryMatch(line.Text, out string[]? captureGroups);
        if (matched == options.InvertMatch)
        {
            return;
        }

        descriptors.Add(new FilteredLineDescriptor(
            line.StartOffset,
            line.EndOffset,
            FilteredLineUtilities.CountVisualRows(line.Text),
            matched && !options.InvertMatch ? captureGroups : null));
    }

    private static bool EndsWithLineBreak(FileStream fs, LogEncodingKind kind, long dataOffset, long oldFileSize)
    {
        if (oldFileSize <= dataOffset)
        {
            return false;
        }

        if (kind is LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be)
        {
            if (oldFileSize - dataOffset < 2)
            {
                return false;
            }

            long offset = oldFileSize - 2;
            Span<byte> bytes = stackalloc byte[2];
            fs.Position = offset;
            fs.ReadExactly(bytes);
            ushort unit = kind == LogEncodingKind.Utf16Le
                ? (ushort)(bytes[0] | (bytes[1] << 8))
                : (ushort)((bytes[0] << 8) | bytes[1]);
            return unit is 0x000D or 0x000A;
        }

        fs.Position = oldFileSize - 1;
        int value = fs.ReadByte();
        return value is 0x0D or 0x0A;
    }

    private static long FindPreviousSingleByteLineStart(FileStream fs, long dataOffset, long oldFileSize)
    {
        byte[] buffer = new byte[BackwardScanBlockBytes];
        long cursor = oldFileSize;
        while (cursor > dataOffset)
        {
            int bytesToRead = (int)Math.Min(buffer.Length, cursor - dataOffset);
            long blockStart = cursor - bytesToRead;
            fs.Position = blockStart;
            fs.ReadExactly(buffer.AsSpan(0, bytesToRead));
            for (int i = bytesToRead - 1; i >= 0; i--)
            {
                if (buffer[i] is 0x0D or 0x0A)
                {
                    return blockStart + i + 1;
                }
            }

            cursor = blockStart;
        }

        return dataOffset;
    }

    private static long FindPreviousUtf16LineStart(FileStream fs, long dataOffset, long oldFileSize, bool littleEndian)
    {
        byte[] buffer = new byte[BackwardScanBlockBytes];
        long cursor = oldFileSize - ((oldFileSize - dataOffset) % 2);
        while (cursor > dataOffset)
        {
            int bytesToRead = (int)Math.Min(buffer.Length, cursor - dataOffset);
            bytesToRead &= ~1;
            if (bytesToRead <= 0)
            {
                break;
            }

            long blockStart = cursor - bytesToRead;
            fs.Position = blockStart;
            fs.ReadExactly(buffer.AsSpan(0, bytesToRead));
            for (int i = bytesToRead - 2; i >= 0; i -= 2)
            {
                ushort unit = littleEndian
                    ? (ushort)(buffer[i] | (buffer[i + 1] << 8))
                    : (ushort)((buffer[i] << 8) | buffer[i + 1]);
                if (unit is 0x000D or 0x000A)
                {
                    return blockStart + i + 2;
                }
            }

            cursor = blockStart;
        }

        return dataOffset;
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
