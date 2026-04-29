using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal enum FileEncodingKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Windows1252
}

internal readonly record struct Checkpoint(long LineNumber, long ByteOffset);

internal sealed class LogFile : IDisposable
{
    private const int CheckpointInterval = 4096;
    private readonly string _filePath;
    private readonly FileEncodingKind _kind;
    private readonly Encoding _encoding;
    private readonly long _dataOffset;
    private readonly List<Checkpoint> _checkpoints = new();

    private LogFile(string filePath, FileEncodingKind kind, Encoding encoding, long dataOffset)
    {
        _filePath = filePath;
        _kind = kind;
        _encoding = encoding;
        _dataOffset = dataOffset;
    }

    public string FilePath => _filePath;
    public long DataOffset => _dataOffset;
    public string EncodingName => _kind switch
    {
        FileEncodingKind.Utf8 => "UTF-8 BOM",
        FileEncodingKind.Utf16Le => "UTF-16 LE BOM",
        FileEncodingKind.Utf16Be => "UTF-16 BE BOM",
        _ => "Windows-1252"
    };
    public long LineCount { get; private set; }
    public int CheckpointCount => _checkpoints.Count;

    public static LogFile Open(string path)
    {
        AppLog.Instance.Info("file.open.begin", "begin", new LogField("path", path));
        using FileStream fs = OpenSourceStream(path);
        (FileEncodingKind Kind, Encoding Encoding, long DataOffset) detection;
        try
        {
            detection = DetectEncoding(fs);
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("path", path),
                new LogField("stage", "encoding_detect_failed"),
                new LogField("reason", ex.Message),
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name));
            throw;
        }

        AppLog.Instance.Info(
            "encoding.detected",
            "detected",
            new LogField("path", path),
            new LogField("encoding", DescribeEncoding(detection.Kind)),
            new LogField("dataOffset", detection.DataOffset.ToString()));

        var logFile = new LogFile(path, detection.Kind, detection.Encoding, detection.DataOffset);
        AppLog.Instance.Info("index.begin", "begin", new LogField("path", path));
        try
        {
            logFile.BuildIndex();
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("path", path),
                new LogField("stage", "index_build_failed"),
                new LogField("reason", ex.Message),
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name));
            throw;
        }

        AppLog.Instance.Info(
            "index.complete",
            "complete",
            new LogField("path", path),
            new LogField("lineCount", logFile.LineCount.ToString()),
            new LogField("checkpointCount", logFile.CheckpointCount.ToString()));
        return logFile;
    }

    private static FileStream OpenSourceStream(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("path", path),
                new LogField("stage", "open_failed"),
                new LogField("reason", ex.Message),
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name));
            throw;
        }
    }

    public string ReadLine(long lineNumber)
    {
        if (lineNumber < 1 || lineNumber > LineCount)
        {
            return string.Empty;
        }

        Checkpoint checkpoint = FindCheckpoint(lineNumber);
        using FileStream fs = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
        fs.Position = checkpoint.ByteOffset;
        using StreamReader reader = new(fs, _encoding, false, 4096, false);

        for (long current = checkpoint.LineNumber; current < lineNumber; current++)
        {
            if (reader.ReadLine() is null)
            {
                return string.Empty;
            }
        }

        return reader.ReadLine() ?? string.Empty;
    }

    public List<KeyValuePair<long, string>> ReadLines(long startLine, long endLine)
    {
        List<KeyValuePair<long, string>> lines = new();
        if (startLine < 1 || endLine < startLine || startLine > LineCount)
        {
            return lines;
        }

        endLine = Math.Min(endLine, LineCount);
        Checkpoint checkpoint = FindCheckpoint(startLine);
        using FileStream fs = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
        fs.Position = checkpoint.ByteOffset;
        using StreamReader reader = new(fs, _encoding, false, 4096, false);

        for (long current = checkpoint.LineNumber; current < startLine; current++)
        {
            if (reader.ReadLine() is null)
            {
                return lines;
            }
        }

        for (long current = startLine; current <= endLine; current++)
        {
            string? text = reader.ReadLine();
            if (text is null)
            {
                break;
            }

            lines.Add(new KeyValuePair<long, string>(current, text));
        }

        return lines;
    }

    public void Dispose()
    {
    }

    private void BuildIndex()
    {
        using FileStream fs = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
        fs.Position = _dataOffset;

        if (fs.Position < fs.Length)
        {
            _checkpoints.Add(new Checkpoint(1, _dataOffset));
            LineCount = 1;
        }
        else
        {
            LineCount = 0;
            return;
        }

        byte[] buffer = new byte[64 * 1024];
        long absoluteOffset = _dataOffset;

        if (_kind is FileEncodingKind.Utf16Le or FileEncodingKind.Utf16Be)
        {
            ScanUtf16(fs, buffer, ref absoluteOffset);
        }
        else
        {
            ScanSingleByte(fs, buffer, ref absoluteOffset);
        }
    }

    private void ScanSingleByte(FileStream fs, byte[] buffer, ref long absoluteOffset)
    {
        bool pendingCR = false;
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            long chunkStart = absoluteOffset;
            int index = 0;
            if (pendingCR)
            {
                pendingCR = false;
                if (buffer[0] == 0x0A)
                {
                    index = 1;
                    FinishLine(chunkStart + 1);
                }
                else
                {
                    FinishLine(chunkStart);
                }
            }

            while (index < read)
            {
                byte b = buffer[index];
                if (b == 0x0D)
                {
                    if (index + 1 < read)
                    {
                        if (buffer[index + 1] == 0x0A)
                        {
                            index += 2;
                        }
                        else
                        {
                            index += 1;
                        }

                        FinishLine(chunkStart + index);
                    }
                    else
                    {
                        pendingCR = true;
                        index++;
                    }
                }
                else if (b == 0x0A)
                {
                    index++;
                    FinishLine(chunkStart + index);
                }
                else
                {
                    index++;
                }
            }

            absoluteOffset += read;
        }

        if (pendingCR)
        {
            FinishLine(absoluteOffset);
        }
    }

    private void ScanUtf16(FileStream fs, byte[] buffer, ref long absoluteOffset)
    {
        bool littleEndian = _kind is FileEncodingKind.Utf16Le;
        bool pendingCR = false;
        bool hasCarry = false;
        byte carry = 0;
        int read;

        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            long chunkStart = absoluteOffset;
            int index = 0;
            if (hasCarry)
            {
                byte first = carry;
                byte second = buffer[0];
                ushort unit = littleEndian ? (ushort)(first | (second << 8)) : (ushort)((first << 8) | second);
                long carriedUnitStart = chunkStart - 1;
                index = 1;
                hasCarry = false;
                if (pendingCR)
                {
                    if (unit == 0x000A)
                    {
                        FinishLine(carriedUnitStart + 2);
                        pendingCR = false;
                    }
                    else
                    {
                        FinishLine(carriedUnitStart);
                        pendingCR = false;
                    }
                }

                if (unit == 0x000D)
                {
                    pendingCR = true;
                }
                else if (unit == 0x000A)
                {
                    FinishLine(carriedUnitStart + 2);
                }
            }

            while (index + 1 < read)
            {
                ushort unit = littleEndian
                    ? (ushort)(buffer[index] | (buffer[index + 1] << 8))
                    : (ushort)((buffer[index] << 8) | buffer[index + 1]);

                index += 2;
                long lineStart = chunkStart + index;

                if (pendingCR)
                {
                    if (unit == 0x000A)
                    {
                        FinishLine(lineStart);
                        pendingCR = false;
                        continue;
                    }

                    FinishLine(lineStart - 2);
                    pendingCR = false;
                }

                if (unit == 0x000D)
                {
                    pendingCR = true;
                }
                else if (unit == 0x000A)
                {
                    FinishLine(absoluteOffset);
                }
            }

            if (index < read)
            {
                carry = buffer[index];
                hasCarry = true;
            }

            absoluteOffset += read;
        }

        if (pendingCR)
        {
            FinishLine(absoluteOffset);
        }
    }

    private void FinishLine(long nextLineStartOffset)
    {
        LineCount++;
        if (LineCount > 1 && (LineCount % CheckpointInterval == 0))
        {
            _checkpoints.Add(new Checkpoint(LineCount, nextLineStartOffset));
        }
    }

    private Checkpoint FindCheckpoint(long lineNumber)
    {
        Checkpoint best = _checkpoints[0];
        for (int i = 1; i < _checkpoints.Count; i++)
        {
            Checkpoint candidate = _checkpoints[i];
            if (candidate.LineNumber > lineNumber)
            {
                break;
            }

            best = candidate;
        }

        return best;
    }

    private static (FileEncodingKind Kind, Encoding Encoding, long DataOffset) DetectEncoding(FileStream fs)
    {
        fs.Position = 0;
        byte[] sample = new byte[Math.Min(65536, (int)Math.Min(int.MaxValue, Math.Max(0, fs.Length)))];
        int read = fs.Read(sample, 0, sample.Length);

        if (read >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            return (FileEncodingKind.Utf8, new UTF8Encoding(false, true), 3);
        }

        if (read >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
        {
            return (FileEncodingKind.Utf16Le, new UnicodeEncoding(false, false, true), 2);
        }

        if (read >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
        {
            return (FileEncodingKind.Utf16Be, new UnicodeEncoding(true, false, true), 2);
        }

        fs.Position = 0;
        return (FileEncodingKind.Windows1252, Windows1252Encoding.Instance, 0);
    }

    private static string DescribeEncoding(FileEncodingKind kind)
    {
        return kind switch
        {
            FileEncodingKind.Utf8 => "UTF-8 BOM",
            FileEncodingKind.Utf16Le => "UTF-16 LE BOM",
            FileEncodingKind.Utf16Be => "UTF-16 BE BOM",
            _ => "Windows-1252"
        };
    }
}
