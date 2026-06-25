using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

public readonly record struct SearchProgressUpdate(double ProgressPercentage, long MatchedLineCount, long ElapsedMilliseconds, FilteredVisualRowReader? Reader, bool IsFinal);

public readonly record struct StagedSearchProgressUpdate(
    double ProgressPercentage,
    long[] MatchedLineCounts,
    long ElapsedMilliseconds,
    FilteredVisualRowReader?[] Readers,
    bool IsFinal,
    bool IsPaused,
    long ProcessedOffset,
    long TargetFileSize,
    long TotalLineCount);

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

    public static void ValidateOptions(IReadOnlyList<SearchOptions> options)
    {
        foreach (SearchOptions option in options)
        {
            ValidateOptions(option);
        }
    }

    public static FilteredVisualRowReader BuildFilteredReader(string filePath, Encoding encoding, long dataOffset, SearchOptions options, DisplayParserRule? displayParserRule = null)
        => BuildFilteredReader(filePath, encoding, dataOffset, new[] { options }, displayParserRule);

    public static FilteredVisualRowReader BuildFilteredReader(string filePath, Encoding encoding, long dataOffset, IReadOnlyList<SearchOptions> options, DisplayParserRule? displayParserRule = null)
    {
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor> descriptors = new();
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        long totalLineCount = 0;

        DisplayParserRecordSequence recordSequence = new(parserRule);
        foreach (DisplayParserRecord record in recordSequence.Enumerate(
            SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize)))
        {
            totalLineCount = record.LastLineNumber;
            AddDescriptorIfIncluded(descriptors, record, matcher);
        }
        totalLineCount = Math.Max(totalLineCount, recordSequence.LastLineNumberSeen);

        return new FilteredVisualRowReader(
            fullPath,
            kind,
            encoding,
            dataOffset,
            fileSize,
            descriptors,
            totalLineCount,
            parserRule,
            GetParserRescanOffset(recordSequence, fileSize),
            GetParserRescanLineNumber(recordSequence, totalLineCount));
    }

    public static FilteredVisualRowReader[] BuildStagedFilteredReaders(string filePath, Encoding encoding, long dataOffset, IReadOnlyList<SearchOptions> options, DisplayParserRule? displayParserRule = null)
    {
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor>[] descriptors = CreateDescriptorLists(options.Count);
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        long totalLineCount = 0;

        DisplayParserRecordSequence recordSequence = new(parserRule);
        foreach (DisplayParserRecord record in recordSequence.Enumerate(
            SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize)))
        {
            totalLineCount = record.LastLineNumber;
            AddDescriptorsByStage(descriptors, record, matcher);
        }
        totalLineCount = Math.Max(totalLineCount, recordSequence.LastLineNumberSeen);

        return BuildReaders(
            fullPath,
            kind,
            encoding,
            dataOffset,
            fileSize,
            descriptors,
            totalLineCount,
            Array.Empty<int>(),
            parserRule,
            GetParserRescanOffset(recordSequence, fileSize),
            GetParserRescanLineNumber(recordSequence, totalLineCount));
    }

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, string query, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, new SearchOptions(query, UseRegex: false, IgnoreCase: true), preloadedVisibleLines, onProgress);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, string query, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress, CancellationToken cancellationToken)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, new SearchOptions(query, UseRegex: false, IgnoreCase: true), preloadedVisibleLines, onProgress, cancellationToken);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, SearchOptions options, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress, DisplayParserRule? displayParserRule = null)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, options, preloadedVisibleLines, onProgress, CancellationToken.None, displayParserRule);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, SearchOptions options, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress, CancellationToken cancellationToken, DisplayParserRule? displayParserRule = null)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, new[] { options }, preloadedVisibleLines, onProgress, cancellationToken, displayParserRule);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, IReadOnlyList<SearchOptions> options, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress, DisplayParserRule? displayParserRule = null)
        => BuildFilteredReaderIncremental(filePath, encoding, dataOffset, options, preloadedVisibleLines, onProgress, CancellationToken.None, displayParserRule);

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, IReadOnlyList<SearchOptions> options, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress, CancellationToken cancellationToken, DisplayParserRule? displayParserRule = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor> descriptors = new();
        long lastPublishedTick = Environment.TickCount64;
        int lastPublishedDescriptorCount = -1;
        bool partialPublished = false;
        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);

        long scannedOffset = dataOffset;
        long totalLineCount = 0;
        DisplayParserRecordSequence recordSequence = new(parserRule);
        foreach (DisplayParserRecord record in recordSequence.Enumerate(
            SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize, cancellationToken),
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedOffset = record.NextOffset;
            totalLineCount = record.LastLineNumber;
            AddDescriptorIfIncluded(descriptors, record, matcher);

            long now = Environment.TickCount64;
            bool firstPartialWithMatches = !partialPublished && descriptors.Count > 0;
            bool intervalElapsed = now - lastPublishedTick >= ProgressPublishIntervalMs;
            if (firstPartialWithMatches || intervalElapsed)
            {
                PublishSnapshot(isFinal: false);
            }
        }
        totalLineCount = Math.Max(totalLineCount, recordSequence.LastLineNumberSeen);

        cancellationToken.ThrowIfCancellationRequested();
        PublishSnapshot(isFinal: true);
        return;

        void PublishSnapshot(bool isFinal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double progress = CalculateProgressPercentage(dataOffset, scannedOffset, fileSize);
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader? reader = null;
            bool shouldBuildReader = isFinal || descriptors.Count != lastPublishedDescriptorCount;
            if (shouldBuildReader && (descriptors.Count > 0 || isFinal))
            {
                reader = new FilteredVisualRowReader(
                    fullPath,
                    kind,
                    encoding,
                    dataOffset,
                    fileSize,
                    descriptors,
                    totalLineCount,
                    parserRule,
                    GetParserRescanOffset(recordSequence, isFinal ? fileSize : scannedOffset),
                    GetParserRescanLineNumber(recordSequence, totalLineCount));
                reader.ReadFromPercentage(0d, Math.Max(1, preloadedVisibleLines));
                lastPublishedDescriptorCount = descriptors.Count;
            }

            onProgress(new SearchProgressUpdate(progress, descriptors.Count, stopwatch.ElapsedMilliseconds, reader, isFinal));
            cancellationToken.ThrowIfCancellationRequested();
            lastPublishedTick = Environment.TickCount64;
            partialPublished = partialPublished || reader is not null;
        }
    }

    public static void BuildStagedFilteredReadersIncremental(
        string filePath,
        Encoding encoding,
        long dataOffset,
        IReadOnlyList<SearchOptions> options,
        IReadOnlyList<int> preloadedVisibleLines,
        Action<StagedSearchProgressUpdate> onProgress,
        CancellationToken cancellationToken,
        DisplayParserRule? displayParserRule = null)
    {
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor>[] descriptors = CreateDescriptorLists(options.Count);
        long lastPublishedTick = Environment.TickCount64;
        int[] lastPublishedDescriptorCounts = CreateFilledArray(options.Count, -1);
        bool partialPublished = false;
        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);

        long scannedOffset = dataOffset;
        long totalLineCount = 0;
        DisplayParserRecordSequence recordSequence = new(parserRule);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DisplayParserRecord record in recordSequence.Enumerate(
                SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize, cancellationToken),
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedOffset = record.NextOffset;
                totalLineCount = record.LastLineNumber;
                AddDescriptorsByStage(descriptors, record, matcher);

                long now = Environment.TickCount64;
                bool firstPartialWithMatches = !partialPublished && HasAnyDescriptors(descriptors);
                bool intervalElapsed = now - lastPublishedTick >= ProgressPublishIntervalMs;
                if (firstPartialWithMatches || intervalElapsed)
                {
                    PublishSnapshot(isFinal: false, isPaused: false);
                }
            }
            totalLineCount = Math.Max(totalLineCount, recordSequence.LastLineNumberSeen);

            cancellationToken.ThrowIfCancellationRequested();
            PublishSnapshot(isFinal: true, isPaused: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishSnapshot(isFinal: false, isPaused: true);
            throw;
        }

        return;

        void PublishSnapshot(bool isFinal, bool isPaused)
        {
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double progress = CalculateProgressPercentage(dataOffset, scannedOffset, fileSize);
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader?[] readers = new FilteredVisualRowReader?[descriptors.Length];
            bool publishedReader = false;
            long readerFileSize = isFinal ? fileSize : scannedOffset;
            for (int i = 0; i < descriptors.Length; i++)
            {
                bool shouldBuildReader = isFinal || isPaused || descriptors[i].Count != lastPublishedDescriptorCounts[i];
                if (shouldBuildReader && (descriptors[i].Count > 0 || isFinal))
                {
                    readers[i] = new FilteredVisualRowReader(
                        fullPath,
                        kind,
                        encoding,
                        dataOffset,
                        readerFileSize,
                        descriptors[i],
                        totalLineCount,
                        parserRule,
                        GetParserRescanOffset(recordSequence, readerFileSize),
                        GetParserRescanLineNumber(recordSequence, totalLineCount));
                    if (!isPaused)
                    {
                        readers[i]!.ReadFromPercentage(0d, GetPreloadedVisibleLines(preloadedVisibleLines, i));
                    }

                    lastPublishedDescriptorCounts[i] = descriptors[i].Count;
                    publishedReader = true;
                }
                else if (isPaused)
                {
                    readers[i] = new FilteredVisualRowReader(
                        fullPath,
                        kind,
                        encoding,
                        dataOffset,
                        readerFileSize,
                        descriptors[i],
                        totalLineCount,
                        parserRule,
                        GetParserRescanOffset(recordSequence, readerFileSize),
                        GetParserRescanLineNumber(recordSequence, totalLineCount));
                    publishedReader = true;
                }
            }

            onProgress(new StagedSearchProgressUpdate(
                progress,
                CopyDescriptorCounts(descriptors),
                stopwatch.ElapsedMilliseconds,
                readers,
                isFinal,
                isPaused,
                scannedOffset,
                fileSize,
                totalLineCount));
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            lastPublishedTick = Environment.TickCount64;
            partialPublished = partialPublished || publishedReader;
        }
    }

    public static void BuildChangedStagedFilteredReadersIncremental(
        IReadOnlyList<FilteredVisualRowReader> previousReaders,
        int changedStageIndex,
        IReadOnlyList<SearchOptions> options,
        IReadOnlyList<int> preloadedVisibleLines,
        Action<StagedSearchProgressUpdate> onProgress,
        CancellationToken cancellationToken,
        DisplayParserRule? displayParserRule = null)
    {
        if (options.Count == 0)
        {
            onProgress(new StagedSearchProgressUpdate(
                100d,
                Array.Empty<long>(),
                0,
                Array.Empty<FilteredVisualRowReader?>(),
                IsFinal: true,
                IsPaused: false,
                ProcessedOffset: 0,
                TargetFileSize: 0,
                TotalLineCount: 0));
            return;
        }

        if (changedStageIndex <= 0 || changedStageIndex >= options.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(changedStageIndex));
        }

        if (previousReaders.Count < changedStageIndex)
        {
            throw new ArgumentException("Previous reader count must include the stage before the changed stage.", nameof(previousReaders));
        }

        FilteredVisualRowReader sourceReader = previousReaders[changedStageIndex - 1];
        string fullPath = sourceReader.FilePath;
        LogEncodingKind kind = sourceReader.Kind;
        Encoding encoding = sourceReader.SourceEncoding;
        long dataOffset = sourceReader.DataOffset;
        long fileSize = sourceReader.ConfirmedFileSize;
        long totalLineCount = sourceReader.TotalLineCount;
        FilteredLineDescriptor[] sourceDescriptors = sourceReader.CopyDescriptors();
        List<FilteredLineDescriptor>[] descriptors = CreateDescriptorLists(options.Count);
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        long[] previousMatchedCounts = CopyExistingMatchedLineCounts(previousReaders, changedStageIndex, options.Count);
        long lastPublishedTick = Environment.TickCount64;
        int[] lastPublishedDescriptorCounts = CreateFilledArray(options.Count, -1);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int processedDescriptorCount = 0;
        long processedOffset = dataOffset;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (FilteredLineDescriptor sourceDescriptor in sourceDescriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedDescriptorCount++;
                processedOffset = sourceDescriptor.EndOffset;
                string lineText = DisplayParserRecordEvaluator.ReadRecordText(
                    fullPath,
                    encoding,
                    kind,
                    sourceDescriptor.StartOffset,
                    sourceDescriptor.EndOffset,
                    sourceDescriptor.LineNumber,
                    parserRule);
                AddChangedStageDescriptors(
                    descriptors,
                    changedStageIndex,
                    sourceDescriptor,
                    lineText,
                    matcher);

                long now = Environment.TickCount64;
                if (now - lastPublishedTick >= ProgressPublishIntervalMs)
                {
                    PublishSnapshot(isFinal: false, isPaused: false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            PublishSnapshot(isFinal: true, isPaused: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishSnapshot(isFinal: false, isPaused: true);
            throw;
        }

        return;

        void PublishSnapshot(bool isFinal, bool isPaused)
        {
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double progress = sourceDescriptors.Length == 0
                ? 100d
                : (processedDescriptorCount * 100d) / sourceDescriptors.Length;
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader?[] readers = new FilteredVisualRowReader?[options.Count];
            for (int i = changedStageIndex; i < options.Count; i++)
            {
                bool shouldBuildReader = isFinal || isPaused || descriptors[i].Count != lastPublishedDescriptorCounts[i];
                if (shouldBuildReader)
                {
                    readers[i] = new FilteredVisualRowReader(
                        fullPath,
                        kind,
                        encoding,
                        dataOffset,
                        fileSize,
                        descriptors[i],
                        totalLineCount,
                        parserRule,
                        sourceReader.ParserRescanOffset,
                        sourceReader.ParserRescanLineNumber);
                    if (!isPaused)
                    {
                        readers[i]!.ReadFromPercentage(0d, GetPreloadedVisibleLines(preloadedVisibleLines, i));
                    }

                    lastPublishedDescriptorCounts[i] = descriptors[i].Count;
                }
            }

            onProgress(new StagedSearchProgressUpdate(
                progress,
                CreateChangedStageMatchedLineCounts(previousMatchedCounts, descriptors, changedStageIndex),
                stopwatch.ElapsedMilliseconds,
                readers,
                isFinal,
                isPaused,
                processedOffset,
                fileSize,
                totalLineCount));
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            lastPublishedTick = Environment.TickCount64;
        }
    }

    public static void BuildAppendedFilteredReaderIncremental(
        FilteredVisualRowReader previousReader,
        SearchOptions options,
        long newFileSize,
        int preloadedVisibleLines,
        Action<SearchProgressUpdate> onProgress,
        CancellationToken cancellationToken,
        DisplayParserRule? displayParserRule = null)
        => BuildAppendedFilteredReaderIncremental(previousReader, new[] { options }, newFileSize, preloadedVisibleLines, onProgress, cancellationToken, displayParserRule);

    public static void BuildAppendedFilteredReaderIncremental(
        FilteredVisualRowReader previousReader,
        IReadOnlyList<SearchOptions> options,
        long newFileSize,
        int preloadedVisibleLines,
        Action<SearchProgressUpdate> onProgress,
        CancellationToken cancellationToken,
        DisplayParserRule? displayParserRule = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = previousReader.FilePath;
        long oldFileSize = previousReader.ConfirmedFileSize;
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        if (newFileSize <= oldFileSize)
        {
            FilteredVisualRowReader unchanged = new(
                fullPath,
                previousReader.Kind,
                previousReader.SourceEncoding,
                previousReader.DataOffset,
                oldFileSize,
                previousReader.CopyDescriptorsBefore(long.MaxValue),
                previousReader.TotalLineCount,
                parserRule,
                previousReader.ParserRescanOffset,
                previousReader.ParserRescanLineNumber);
            unchanged.ReadFromPercentage(0d, Math.Max(1, preloadedVisibleLines));
            onProgress(new SearchProgressUpdate(100d, unchanged.MatchedLineCount, 0, unchanged, IsFinal: true));
            return;
        }

        LogEncodingKind kind = previousReader.Kind;
        Encoding encoding = previousReader.SourceEncoding;
        long dataOffset = previousReader.DataOffset;
        long rescanStartOffset = GetAppendRescanStart(fullPath, kind, dataOffset, oldFileSize);
        long firstLineNumber;
        if (previousReader.ParserRescanOffset >= dataOffset &&
            previousReader.ParserRescanOffset < rescanStartOffset)
        {
            rescanStartOffset = previousReader.ParserRescanOffset;
            firstLineNumber = previousReader.ParserRescanLineNumber;
        }
        else
        {
            firstLineNumber = rescanStartOffset == oldFileSize
                ? previousReader.TotalLineCount + 1
                : Math.Max(1, previousReader.TotalLineCount);
        }

        List<FilteredLineDescriptor> descriptors = new(previousReader.CopyDescriptorsBefore(rescanStartOffset));
        long totalLineCount = previousReader.TotalLineCount;
        long scannedOffset = rescanStartOffset;
        long lastPublishedTick = Environment.TickCount64;
        int lastPublishedDescriptorCount = descriptors.Count;
        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);

        DisplayParserRecordSequence recordSequence = new(parserRule);
        foreach (DisplayParserRecord record in recordSequence.Enumerate(
            SearchRealLineScanner.Enumerate(fullPath, encoding, kind, rescanStartOffset, newFileSize, cancellationToken, firstLineNumber),
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedOffset = record.NextOffset;
            totalLineCount = record.LastLineNumber;
            AddDescriptorIfIncluded(descriptors, record, matcher);

            long now = Environment.TickCount64;
            if (now - lastPublishedTick >= ProgressPublishIntervalMs)
            {
                PublishSnapshot(isFinal: false);
            }
        }
        totalLineCount = Math.Max(totalLineCount, recordSequence.LastLineNumberSeen);

        cancellationToken.ThrowIfCancellationRequested();
        PublishSnapshot(isFinal: true);
        return;

        void PublishSnapshot(bool isFinal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double progress = CalculateProgressPercentage(dataOffset, scannedOffset, newFileSize);
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader? reader = null;
            bool shouldBuildReader = isFinal || descriptors.Count != lastPublishedDescriptorCount;
            if (shouldBuildReader)
            {
                reader = new FilteredVisualRowReader(
                    fullPath,
                    kind,
                    encoding,
                    dataOffset,
                    newFileSize,
                    descriptors,
                    totalLineCount,
                    parserRule,
                    GetParserRescanOffset(recordSequence, newFileSize),
                    GetParserRescanLineNumber(recordSequence, totalLineCount));
                reader.ReadFromPercentage(0d, Math.Max(1, preloadedVisibleLines));
                lastPublishedDescriptorCount = descriptors.Count;
            }

            onProgress(new SearchProgressUpdate(progress, descriptors.Count, stopwatch.ElapsedMilliseconds, reader, isFinal));
            cancellationToken.ThrowIfCancellationRequested();
            lastPublishedTick = Environment.TickCount64;
        }
    }

    public static void BuildAppendedStagedFilteredReadersIncremental(
        IReadOnlyList<FilteredVisualRowReader> previousReaders,
        IReadOnlyList<SearchOptions> options,
        long newFileSize,
        IReadOnlyList<int> preloadedVisibleLines,
        Action<StagedSearchProgressUpdate> onProgress,
        CancellationToken cancellationToken,
        DisplayParserRule? displayParserRule = null)
    {
        if (previousReaders.Count != options.Count)
        {
            throw new ArgumentException("Previous reader count must match search option count.", nameof(previousReaders));
        }

        if (previousReaders.Count == 0)
        {
            onProgress(new StagedSearchProgressUpdate(
                100d,
                Array.Empty<long>(),
                0,
                Array.Empty<FilteredVisualRowReader?>(),
                IsFinal: true,
                IsPaused: false,
                ProcessedOffset: 0,
                TargetFileSize: 0,
                TotalLineCount: 0));
            return;
        }

        FilteredVisualRowReader firstReader = previousReaders[0];
        string fullPath = firstReader.FilePath;
        long oldFileSize = firstReader.ConfirmedFileSize;
        LogEncodingKind kind = firstReader.Kind;
        Encoding encoding = firstReader.SourceEncoding;
        long dataOffset = firstReader.DataOffset;
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        if (newFileSize <= oldFileSize)
        {
            FilteredVisualRowReader?[] unchangedReaders = new FilteredVisualRowReader?[previousReaders.Count];
            for (int i = 0; i < previousReaders.Count; i++)
            {
                unchangedReaders[i] = new FilteredVisualRowReader(
                    fullPath,
                    kind,
                    encoding,
                    dataOffset,
                    oldFileSize,
                    previousReaders[i].CopyDescriptorsBefore(long.MaxValue),
                    previousReaders[i].TotalLineCount,
                    parserRule,
                    previousReaders[i].ParserRescanOffset,
                    previousReaders[i].ParserRescanLineNumber);
                unchangedReaders[i]!.ReadFromPercentage(0d, GetPreloadedVisibleLines(preloadedVisibleLines, i));
            }

            onProgress(new StagedSearchProgressUpdate(
                100d,
                CopyMatchedLineCounts(unchangedReaders),
                0,
                unchangedReaders,
                IsFinal: true,
                IsPaused: false,
                ProcessedOffset: oldFileSize,
                TargetFileSize: newFileSize,
                TotalLineCount: firstReader.TotalLineCount));
            return;
        }

        long rescanStartOffset = GetAppendRescanStart(fullPath, kind, dataOffset, oldFileSize);
        long firstLineNumber;
        if (firstReader.ParserRescanOffset >= dataOffset &&
            firstReader.ParserRescanOffset < rescanStartOffset)
        {
            rescanStartOffset = firstReader.ParserRescanOffset;
            firstLineNumber = firstReader.ParserRescanLineNumber;
        }
        else
        {
            firstLineNumber = rescanStartOffset == oldFileSize
                ? firstReader.TotalLineCount + 1
                : Math.Max(1, firstReader.TotalLineCount);
        }

        List<FilteredLineDescriptor>[] descriptors = new List<FilteredLineDescriptor>[previousReaders.Count];
        for (int i = 0; i < previousReaders.Count; i++)
        {
            descriptors[i] = new List<FilteredLineDescriptor>(previousReaders[i].CopyDescriptorsBefore(rescanStartOffset));
        }

        long totalLineCount = firstReader.TotalLineCount;
        long scannedOffset = rescanStartOffset;
        long lastPublishedTick = Environment.TickCount64;
        int[] lastPublishedDescriptorCounts = new int[descriptors.Length];
        for (int i = 0; i < descriptors.Length; i++)
        {
            lastPublishedDescriptorCounts[i] = descriptors[i].Count;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);
        DisplayParserRecordSequence recordSequence = new(parserRule);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DisplayParserRecord record in recordSequence.Enumerate(
                SearchRealLineScanner.Enumerate(fullPath, encoding, kind, rescanStartOffset, newFileSize, cancellationToken, firstLineNumber),
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedOffset = record.NextOffset;
                totalLineCount = record.LastLineNumber;
                AddDescriptorsByStage(descriptors, record, matcher);

                long now = Environment.TickCount64;
                if (now - lastPublishedTick >= ProgressPublishIntervalMs)
                {
                    PublishSnapshot(isFinal: false, isPaused: false);
                }
            }
            totalLineCount = Math.Max(totalLineCount, recordSequence.LastLineNumberSeen);

            cancellationToken.ThrowIfCancellationRequested();
            PublishSnapshot(isFinal: true, isPaused: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishSnapshot(isFinal: false, isPaused: true);
            throw;
        }

        return;

        void PublishSnapshot(bool isFinal, bool isPaused)
        {
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double progress = CalculateProgressPercentage(dataOffset, scannedOffset, newFileSize);
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader?[] readers = new FilteredVisualRowReader?[descriptors.Length];
            long readerFileSize = isFinal ? newFileSize : scannedOffset;
            for (int i = 0; i < descriptors.Length; i++)
            {
                bool shouldBuildReader = isFinal || isPaused || descriptors[i].Count != lastPublishedDescriptorCounts[i];
                if (shouldBuildReader)
                {
                    readers[i] = new FilteredVisualRowReader(
                        fullPath,
                        kind,
                        encoding,
                        dataOffset,
                        readerFileSize,
                        descriptors[i],
                        totalLineCount,
                        parserRule,
                        GetParserRescanOffset(recordSequence, readerFileSize),
                        GetParserRescanLineNumber(recordSequence, totalLineCount));
                    if (!isPaused)
                    {
                        readers[i]!.ReadFromPercentage(0d, GetPreloadedVisibleLines(preloadedVisibleLines, i));
                    }

                    lastPublishedDescriptorCounts[i] = descriptors[i].Count;
                }
            }

            onProgress(new StagedSearchProgressUpdate(
                progress,
                CopyDescriptorCounts(descriptors),
                stopwatch.ElapsedMilliseconds,
                readers,
                isFinal,
                isPaused,
                scannedOffset,
                newFileSize,
                totalLineCount));
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            lastPublishedTick = Environment.TickCount64;
        }
    }

    public static void ResumeStagedFilteredReadersIncremental(
        IReadOnlyList<FilteredVisualRowReader> pausedReaders,
        IReadOnlyList<SearchOptions> options,
        long processedOffset,
        long newFileSize,
        IReadOnlyList<int> preloadedVisibleLines,
        Action<StagedSearchProgressUpdate> onProgress,
        CancellationToken cancellationToken,
        DisplayParserRule? displayParserRule = null)
    {
        if (pausedReaders.Count != options.Count)
        {
            throw new ArgumentException("Paused reader count must match search option count.", nameof(pausedReaders));
        }

        if (pausedReaders.Count == 0)
        {
            onProgress(new StagedSearchProgressUpdate(
                100d,
                Array.Empty<long>(),
                0,
                Array.Empty<FilteredVisualRowReader?>(),
                IsFinal: true,
                IsPaused: false,
                ProcessedOffset: 0,
                TargetFileSize: newFileSize,
                TotalLineCount: 0));
            return;
        }

        FilteredVisualRowReader firstReader = pausedReaders[0];
        string fullPath = firstReader.FilePath;
        LogEncodingKind kind = firstReader.Kind;
        Encoding encoding = firstReader.SourceEncoding;
        long dataOffset = firstReader.DataOffset;
        long boundedProcessedOffset = Math.Clamp(processedOffset, dataOffset, newFileSize);
        long normalizedProcessedOffset = NormalizeResumeStartOffset(fullPath, kind, dataOffset, boundedProcessedOffset, newFileSize);
        bool processedCompleteLine = EndsWithLineBreakAt(fullPath, kind, dataOffset, normalizedProcessedOffset);
        long resumeStartOffset = processedCompleteLine
            ? normalizedProcessedOffset
            : GetAppendRescanStart(fullPath, kind, dataOffset, normalizedProcessedOffset);
        long firstLineNumber;
        if (firstReader.ParserRescanOffset >= dataOffset &&
            firstReader.ParserRescanOffset < resumeStartOffset)
        {
            resumeStartOffset = firstReader.ParserRescanOffset;
            firstLineNumber = firstReader.ParserRescanLineNumber;
        }
        else
        {
            firstLineNumber = processedCompleteLine
                ? firstReader.TotalLineCount + 1
                : Math.Max(1, firstReader.TotalLineCount);
        }
        long totalLineCount = firstLineNumber - 1;
        DisplayParserRule? parserRule = DisplayParserEvaluator.CloneRule(displayParserRule);
        List<FilteredLineDescriptor>[] descriptors = new List<FilteredLineDescriptor>[pausedReaders.Count];
        for (int i = 0; i < pausedReaders.Count; i++)
        {
            descriptors[i] = new List<FilteredLineDescriptor>(pausedReaders[i].CopyDescriptorsBefore(resumeStartOffset));
        }

        long scannedOffset = resumeStartOffset;
        long lastPublishedTick = Environment.TickCount64;
        int[] lastPublishedDescriptorCounts = new int[descriptors.Length];
        for (int i = 0; i < descriptors.Length; i++)
        {
            lastPublishedDescriptorCounts[i] = descriptors[i].Count;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchMatcherCascade matcher = SearchMatcherCascade.Create(options);
        DisplayParserRecordSequence recordSequence = new(parserRule);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DisplayParserRecord record in recordSequence.Enumerate(
                SearchRealLineScanner.Enumerate(fullPath, encoding, kind, resumeStartOffset, newFileSize, cancellationToken, firstLineNumber),
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedOffset = record.NextOffset;
                totalLineCount = record.LastLineNumber;
                AddDescriptorsByStage(descriptors, record, matcher);

                long now = Environment.TickCount64;
                if (now - lastPublishedTick >= ProgressPublishIntervalMs)
                {
                    PublishSnapshot(isFinal: false, isPaused: false);
                }
            }
            totalLineCount = Math.Max(totalLineCount, recordSequence.LastLineNumberSeen);

            cancellationToken.ThrowIfCancellationRequested();
            PublishSnapshot(isFinal: true, isPaused: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishSnapshot(isFinal: false, isPaused: true);
            throw;
        }

        return;

        void PublishSnapshot(bool isFinal, bool isPaused)
        {
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double progress = CalculateProgressPercentage(dataOffset, scannedOffset, newFileSize);
            if (isFinal)
            {
                progress = 100d;
            }

            FilteredVisualRowReader?[] readers = new FilteredVisualRowReader?[descriptors.Length];
            long readerFileSize = isFinal ? newFileSize : scannedOffset;
            for (int i = 0; i < descriptors.Length; i++)
            {
                bool shouldBuildReader = isFinal || isPaused || descriptors[i].Count != lastPublishedDescriptorCounts[i];
                if (shouldBuildReader)
                {
                    readers[i] = new FilteredVisualRowReader(
                        fullPath,
                        kind,
                        encoding,
                        dataOffset,
                        readerFileSize,
                        descriptors[i],
                        totalLineCount,
                        parserRule,
                        GetParserRescanOffset(recordSequence, readerFileSize),
                        GetParserRescanLineNumber(recordSequence, totalLineCount));
                    if (!isPaused)
                    {
                        readers[i]!.ReadFromPercentage(0d, GetPreloadedVisibleLines(preloadedVisibleLines, i));
                    }

                    lastPublishedDescriptorCounts[i] = descriptors[i].Count;
                }
            }

            onProgress(new StagedSearchProgressUpdate(
                progress,
                CopyDescriptorCounts(descriptors),
                stopwatch.ElapsedMilliseconds,
                readers,
                isFinal,
                isPaused,
                scannedOffset,
                newFileSize,
                totalLineCount));
            if (!isPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            lastPublishedTick = Environment.TickCount64;
        }
    }

    private static double CalculateProgressPercentage(long dataOffset, long processedOffset, long targetFileSize)
    {
        return Math.Clamp(((processedOffset - dataOffset) * 100d) / Math.Max(1, targetFileSize - dataOffset), 0d, 100d);
    }

    private static long NormalizeResumeStartOffset(string filePath, LogEncodingKind kind, long dataOffset, long processedOffset, long fileSize)
    {
        if (processedOffset <= dataOffset || processedOffset >= fileSize)
        {
            return processedOffset;
        }

        using FileStream fs = VisualRowReader.OpenSourceStream(filePath);
        if (kind is LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be)
        {
            if (processedOffset - dataOffset < 2 || processedOffset + 2 > fileSize)
            {
                return processedOffset;
            }

            long aligned = processedOffset - ((processedOffset - dataOffset) % 2);
            if (aligned != processedOffset)
            {
                return processedOffset;
            }

            Span<byte> bytes = stackalloc byte[4];
            fs.Position = processedOffset - 2;
            fs.ReadExactly(bytes);
            bool littleEndian = kind == LogEncodingKind.Utf16Le;
            ushort previous = littleEndian
                ? (ushort)(bytes[0] | (bytes[1] << 8))
                : (ushort)((bytes[0] << 8) | bytes[1]);
            ushort current = littleEndian
                ? (ushort)(bytes[2] | (bytes[3] << 8))
                : (ushort)((bytes[2] << 8) | bytes[3]);
            return previous == 0x000D && current == 0x000A
                ? processedOffset + 2
                : processedOffset;
        }

        if (processedOffset - dataOffset < 1 || processedOffset + 1 > fileSize)
        {
            return processedOffset;
        }

        fs.Position = processedOffset - 1;
        int previousByte = fs.ReadByte();
        int currentByte = fs.ReadByte();
        return previousByte == 0x0D && currentByte == 0x0A
            ? processedOffset + 1
            : processedOffset;
    }

    private static bool EndsWithLineBreakAt(string filePath, LogEncodingKind kind, long dataOffset, long offset)
    {
        using FileStream fs = VisualRowReader.OpenSourceStream(filePath);
        return EndsWithLineBreak(fs, kind, dataOffset, offset);
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
        DisplayParserRecord record,
        SearchMatcherCascade matcher)
    {
        string searchableText = record.Text;
        if (!matcher.TryMatch(searchableText, out FilteredCaptureGroups? captureGroups))
        {
            return;
        }

        descriptors.Add(new FilteredLineDescriptor(
            record.StartOffset,
            record.EndOffset,
            FilteredLineUtilities.CountExplicitRows(searchableText),
            captureGroups,
            record.LineNumber));
    }

    private static void AddDescriptorsByStage(
        List<FilteredLineDescriptor>[] descriptors,
        DisplayParserRecord record,
        SearchMatcherCascade matcher)
    {
        if (descriptors.Length == 0)
        {
            return;
        }

        string searchableText = record.Text;
        FilteredCaptureGroups?[] captureGroupsByStage = new FilteredCaptureGroups?[descriptors.Length];
        int includedStages = matcher.MatchStages(searchableText, captureGroupsByStage);
        for (int i = 0; i < includedStages; i++)
        {
            descriptors[i].Add(new FilteredLineDescriptor(
                record.StartOffset,
                record.EndOffset,
                FilteredLineUtilities.CountExplicitRows(searchableText),
                captureGroupsByStage[i],
                record.LineNumber));
        }
    }

    private static void AddChangedStageDescriptors(
        List<FilteredLineDescriptor>[] descriptors,
        int changedStageIndex,
        FilteredLineDescriptor sourceDescriptor,
        string lineText,
        SearchMatcherCascade matcher)
    {
        if (changedStageIndex < 0 || changedStageIndex >= descriptors.Length)
        {
            return;
        }

        string searchableText = lineText;
        FilteredCaptureGroups?[] captureGroupsByStage = new FilteredCaptureGroups?[descriptors.Length];
        int includedStages = matcher.MatchStages(searchableText, captureGroupsByStage);
        for (int i = changedStageIndex; i < includedStages; i++)
        {
            descriptors[i].Add(new FilteredLineDescriptor(
                sourceDescriptor.StartOffset,
                sourceDescriptor.EndOffset,
                FilteredLineUtilities.CountExplicitRows(searchableText),
                captureGroupsByStage[i],
                sourceDescriptor.LineNumber));
        }
    }

    private static List<FilteredLineDescriptor>[] CreateDescriptorLists(int count)
    {
        List<FilteredLineDescriptor>[] descriptors = new List<FilteredLineDescriptor>[count];
        for (int i = 0; i < descriptors.Length; i++)
        {
            descriptors[i] = new List<FilteredLineDescriptor>();
        }

        return descriptors;
    }

    private static FilteredVisualRowReader[] BuildReaders(
        string fullPath,
        LogEncodingKind kind,
        Encoding encoding,
        long dataOffset,
        long fileSize,
        List<FilteredLineDescriptor>[] descriptors,
        long totalLineCount,
        IReadOnlyList<int> preloadedVisibleLines,
        DisplayParserRule? displayParserRule,
        long parserRescanOffset,
        long parserRescanLineNumber)
    {
        FilteredVisualRowReader[] readers = new FilteredVisualRowReader[descriptors.Length];
        for (int i = 0; i < readers.Length; i++)
        {
            readers[i] = new FilteredVisualRowReader(
                fullPath,
                kind,
                encoding,
                dataOffset,
                fileSize,
                descriptors[i],
                totalLineCount,
                displayParserRule,
                parserRescanOffset,
                parserRescanLineNumber);
            readers[i].ReadFromPercentage(0d, GetPreloadedVisibleLines(preloadedVisibleLines, i));
        }

        return readers;
    }

    private static long GetParserRescanOffset(DisplayParserRecordSequence sequence, long fallbackOffset) =>
        sequence.IncompleteRecordStartOffset >= 0
            ? sequence.IncompleteRecordStartOffset
            : fallbackOffset;

    private static long GetParserRescanLineNumber(DisplayParserRecordSequence sequence, long totalLineCount) =>
        sequence.IncompleteRecordLineNumber > 0
            ? sequence.IncompleteRecordLineNumber
            : totalLineCount + 1;

    private static int[] CreateFilledArray(int count, int value)
    {
        int[] values = new int[count];
        Array.Fill(values, value);
        return values;
    }

    private static bool HasAnyDescriptors(List<FilteredLineDescriptor>[] descriptors)
    {
        foreach (List<FilteredLineDescriptor> stageDescriptors in descriptors)
        {
            if (stageDescriptors.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static long[] CopyDescriptorCounts(List<FilteredLineDescriptor>[] descriptors)
    {
        long[] counts = new long[descriptors.Length];
        for (int i = 0; i < descriptors.Length; i++)
        {
            counts[i] = descriptors[i].Count;
        }

        return counts;
    }

    private static long[] CopyExistingMatchedLineCounts(IReadOnlyList<FilteredVisualRowReader> readers, int count, int expectedCount)
    {
        long[] counts = new long[expectedCount];
        int copyCount = Math.Min(Math.Min(count, readers.Count), counts.Length);
        for (int i = 0; i < copyCount; i++)
        {
            counts[i] = readers[i].MatchedLineCount;
        }

        return counts;
    }

    private static long[] CreateChangedStageMatchedLineCounts(
        long[] previousMatchedCounts,
        List<FilteredLineDescriptor>[] descriptors,
        int changedStageIndex)
    {
        long[] counts = new long[descriptors.Length];
        int prefixCount = Math.Min(Math.Min(changedStageIndex, previousMatchedCounts.Length), counts.Length);
        for (int i = 0; i < prefixCount; i++)
        {
            counts[i] = previousMatchedCounts[i];
        }

        for (int i = Math.Max(0, changedStageIndex); i < counts.Length; i++)
        {
            counts[i] = descriptors[i].Count;
        }

        return counts;
    }

    private static long[] CopyMatchedLineCounts(IReadOnlyList<FilteredVisualRowReader?> readers)
    {
        long[] counts = new long[readers.Count];
        for (int i = 0; i < readers.Count; i++)
        {
            counts[i] = readers[i]?.MatchedLineCount ?? 0;
        }

        return counts;
    }

    private static int GetPreloadedVisibleLines(IReadOnlyList<int> visibleLines, int index)
    {
        if (index >= 0 && index < visibleLines.Count)
        {
            return Math.Max(1, visibleLines[index]);
        }

        return 1;
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
        RegexOptions regexOptions = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        if (options.IgnoreCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(options.Query, regexOptions);
        }
        catch (NotSupportedException ex)
        {
            throw new ArgumentException("Regex is not supported by the non-backtracking engine: " + ex.Message, nameof(options), ex);
        }
    }

    private readonly struct SearchMatcherCascade
    {
        private readonly SearchOptions[] _options;
        private readonly SearchMatcher[] _matchers;

        private SearchMatcherCascade(SearchOptions[] options, SearchMatcher[] matchers)
        {
            _options = options;
            _matchers = matchers;
        }

        public static SearchMatcherCascade Create(IReadOnlyList<SearchOptions> options)
        {
            SearchOptions[] optionCopy = new SearchOptions[options.Count];
            SearchMatcher[] matchers = new SearchMatcher[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                optionCopy[i] = options[i];
                matchers[i] = SearchMatcher.Create(options[i]);
            }

            return new SearchMatcherCascade(optionCopy, matchers);
        }

        public bool TryMatch(string text, out FilteredCaptureGroups? captureGroups)
        {
            FilteredCaptureGroups?[] captureGroupsByStage = new FilteredCaptureGroups?[_matchers.Length];
            int includedStages = MatchStages(text, captureGroupsByStage);
            captureGroups = includedStages > 0 ? captureGroupsByStage[includedStages - 1] : null;
            return includedStages == _matchers.Length;
        }

        public int MatchStages(string text, FilteredCaptureGroups?[] captureGroupsByStage)
        {
            FilteredCaptureGroups? currentCaptureGroups = null;
            for (int i = 0; i < _matchers.Length; i++)
            {
                bool matched = _matchers[i].TryMatch(text, out FilteredCaptureGroups? matchCaptureGroups);
                SearchOptions options = _options[i];
                if (matched == options.InvertMatch)
                {
                    return i;
                }

                if (matched && !options.InvertMatch && matchCaptureGroups is not null)
                {
                    currentCaptureGroups = matchCaptureGroups;
                }

                captureGroupsByStage[i] = currentCaptureGroups;
            }

            return _matchers.Length;
        }
    }

    private readonly struct SearchMatcher
    {
        private readonly Regex? _regex;
        private readonly string _query;
        private readonly StringComparison _literalComparison;
        private readonly int[] _captureGroupNumbers;
        private readonly string[] _captureGroupHeaders;

        private SearchMatcher(Regex regex)
        {
            _regex = regex;
            _query = string.Empty;
            _literalComparison = StringComparison.Ordinal;
            (_captureGroupNumbers, _captureGroupHeaders) = CreateCaptureGroupDefinitions(regex);
        }

        private SearchMatcher(string query, StringComparison literalComparison)
        {
            _regex = null;
            _query = query;
            _literalComparison = literalComparison;
            _captureGroupNumbers = Array.Empty<int>();
            _captureGroupHeaders = Array.Empty<string>();
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

        public bool TryMatch(string text, out FilteredCaptureGroups? captureGroups)
        {
            captureGroups = null;
            if (_regex is not null)
            {
                Match match = _regex.Match(text);
                if (!match.Success)
                {
                    return false;
                }

                if (_captureGroupNumbers.Length > 0)
                {
                    string[] values = new string[_captureGroupNumbers.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        int groupNumber = _captureGroupNumbers[i];
                        Group? group = groupNumber >= 0 && groupNumber < match.Groups.Count
                            ? match.Groups[groupNumber]
                            : null;
                        values[i] = group is not null && group.Success ? group.Value : string.Empty;
                    }

                    captureGroups = new FilteredCaptureGroups(_captureGroupHeaders, values);
                }

                return true;
            }

            return text.IndexOf(_query, _literalComparison) >= 0;
        }

        private static (int[] Numbers, string[] Headers) CreateCaptureGroupDefinitions(Regex regex)
        {
            int[] groupNumbers = regex.GetGroupNumbers();
            string[] groupNames = regex.GetGroupNames();
            List<int> captureGroupNumbers = new();
            List<string> captureGroupHeaders = new();
            int unnamedCaptureIndex = 0;

            for (int i = 0; i < groupNumbers.Length; i++)
            {
                int groupNumber = groupNumbers[i];
                if (groupNumber == 0)
                {
                    continue;
                }

                string groupName = i < groupNames.Length ? groupNames[i] : groupNumber.ToString();
                string header;
                if (int.TryParse(groupName, out _))
                {
                    header = unnamedCaptureIndex.ToString();
                    unnamedCaptureIndex++;
                }
                else
                {
                    header = groupName;
                }

                captureGroupNumbers.Add(groupNumber);
                captureGroupHeaders.Add(header);
            }

            return (captureGroupNumbers.ToArray(), captureGroupHeaders.ToArray());
        }
    }
}
