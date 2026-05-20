using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

public readonly record struct SearchProgressUpdate(double ProgressPercentage, long MatchedLineCount, long ElapsedMilliseconds, FilteredVisualRowReader? Reader, bool IsFinal);

public static class LogSearchBuilder
{
    private const int ProgressPublishIntervalMs = 150;

    public static FilteredVisualRowReader BuildFilteredReader(string filePath, Encoding encoding, long dataOffset, string query)
    {
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor> descriptors = new();

        foreach (RealLineData line in SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize))
        {
            if (line.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                descriptors.Add(new FilteredLineDescriptor(
                    line.StartOffset,
                    line.EndOffset,
                    FilteredLineUtilities.CountVisualRows(line.Text)));
            }
        }

        return new FilteredVisualRowReader(fullPath, kind, encoding, dataOffset, fileSize, descriptors);
    }

    public static void BuildFilteredReaderIncremental(string filePath, Encoding encoding, long dataOffset, string query, int preloadedVisibleLines, Action<SearchProgressUpdate> onProgress)
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

        long scannedOffset = dataOffset;
        foreach (RealLineData line in SearchRealLineScanner.Enumerate(fullPath, encoding, kind, dataOffset, fileSize))
        {
            scannedOffset = line.EndOffset;
            if (line.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                descriptors.Add(new FilteredLineDescriptor(
                    line.StartOffset,
                    line.EndOffset,
                    FilteredLineUtilities.CountVisualRows(line.Text)));
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
}
