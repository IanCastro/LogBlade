using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class LogOutputExporter
{
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static void SaveParsedLog(ILogRecordSource source, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        SaveAtomically(outputPath, writer =>
        {
            bool first = true;
            foreach (LogViewportRecord record in source.EnumerateRecords(null, null))
            {
                if (!first)
                {
                    writer.Write("\r\n");
                }

                writer.Write(record.DisplayText);
                first = false;
            }
        });
    }

    public static void SaveSearchResults(ILogRecordSource source, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        SaveAtomically(outputPath, writer =>
        {
            IReadOnlyList<string> headers = source.ColumnHeaders;
            WriteTsvRow(writer, headers);
            foreach (LogViewportRecord record in source.EnumerateRecords(null, null))
            {
                writer.Write("\r\n");
                WriteSearchRecord(writer, record, headers.Count);
            }
        });
    }

    private static void SaveAtomically(string outputPath, Action<TextWriter> writeContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string fullPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("Output path has no directory.", nameof(outputPath));
        string temporaryPath = Path.Combine(
            directory,
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8WithBom))
            {
                writeContent(writer);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void WriteSearchRecord(TextWriter writer, LogViewportRecord record, int columnCount)
    {
        IReadOnlyList<string>? cells = record.Cells;
        for (int i = 0; i < columnCount; i++)
        {
            if (i > 0)
            {
                writer.Write('\t');
            }

            string value = cells is not null && i < cells.Count
                ? cells[i]
                : i == 1
                    ? record.DisplayText
                    : string.Empty;
            writer.Write(NormalizeTsvCell(value));
        }
    }

    private static void WriteTsvRow(TextWriter writer, IReadOnlyList<string> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                writer.Write('\t');
            }

            writer.Write(NormalizeTsvCell(values[i]));
        }
    }

    private static string NormalizeTsvCell(string value) =>
        value.Replace('\t', ' ').Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
}
