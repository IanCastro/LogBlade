using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class LogSearchBuilder
{
    public static FilteredVisualRowReader BuildFilteredReader(string filePath, Encoding encoding, long dataOffset, string query)
    {
        string fullPath = Path.GetFullPath(filePath);
        long fileSize = new FileInfo(fullPath).Length;
        LogEncodingKind kind = VisualRowReader.InferKind(encoding, dataOffset);
        List<FilteredLineDescriptor> descriptors = new();

        using FileStream fs = VisualRowReader.OpenSourceStream(fullPath);
        fs.Position = dataOffset;
        while (FilteredLineUtilities.TryReadNextRealLine(fs, kind, encoding, fileSize, out RealLineData line))
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
}
