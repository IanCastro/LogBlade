using System;
using System.IO;
using System.Text;

internal static class LogFileUtilities
{
    private const int ReverseScanBlockBytes = 64 * 1024;

    public static Stream OpenSourceStream(string path) => LogContentSource.FromFile(path).OpenRead();

    public static Stream OpenSourceStream(LogContentSource source) => source.OpenRead();

    public static DetectedEncodingInfo DetectEncoding(Stream fs)
    {
        fs.Position = 0;
        Span<byte> sample = stackalloc byte[3];
        int read = fs.Read(sample);
        fs.Position = 0;

        if (read >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            return new(LogEncodingKind.Utf8, Encoding.UTF8, 3);
        }

        if (read >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
        {
            return new(LogEncodingKind.Utf16Le, Encoding.Unicode, 2);
        }

        if (read >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
        {
            return new(LogEncodingKind.Utf16Be, Encoding.BigEndianUnicode, 2);
        }

        return new(LogEncodingKind.Windows1252, Windows1252Encoding.Instance, 0);
    }

    public static LogEncodingKind InferKind(Encoding encoding, long dataOffset)
    {
        if (dataOffset == 3 && string.Equals(encoding.WebName, Encoding.UTF8.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return LogEncodingKind.Utf8;
        }

        if (dataOffset == 2 && string.Equals(encoding.WebName, Encoding.Unicode.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return LogEncodingKind.Utf16Le;
        }

        if (dataOffset == 2 && string.Equals(encoding.WebName, Encoding.BigEndianUnicode.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return LogEncodingKind.Utf16Be;
        }

        return LogEncodingKind.Windows1252;
    }

    public static string DescribeEncoding(LogEncodingKind kind) => kind switch
    {
        LogEncodingKind.Utf8 => "UTF-8 BOM",
        LogEncodingKind.Utf16Le => "UTF-16 LE BOM",
        LogEncodingKind.Utf16Be => "UTF-16 BE BOM",
        _ => "Windows-1252"
    };

    public static long FindLineStartContaining(
        string filePath,
        LogEncodingKind kind,
        long dataOffset,
        long fileSize,
        long requestedOffset) => FindLineStartContaining(
            LogContentSource.FromFile(filePath),
            kind,
            dataOffset,
            fileSize,
            requestedOffset);

    public static long FindLineStartContaining(
        LogContentSource source,
        LogEncodingKind kind,
        long dataOffset,
        long fileSize,
        long requestedOffset)
    {
        if (fileSize <= dataOffset || requestedOffset <= dataOffset)
        {
            return dataOffset;
        }

        long bounded = Math.Clamp(requestedOffset, dataOffset, fileSize);
        using Stream fs = OpenSourceStream(source);
        return kind is LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be
            ? FindUtf16LineStart(fs, dataOffset, bounded, kind == LogEncodingKind.Utf16Le)
            : FindSingleByteLineStart(fs, dataOffset, bounded);
    }

    public static long FindPreviousLineStart(
        string filePath,
        LogEncodingKind kind,
        long dataOffset,
        long currentLineStart) => FindPreviousLineStart(
            LogContentSource.FromFile(filePath),
            kind,
            dataOffset,
            currentLineStart);

    public static long FindPreviousLineStart(
        LogContentSource source,
        LogEncodingKind kind,
        long dataOffset,
        long currentLineStart)
    {
        if (currentLineStart <= dataOffset)
        {
            return dataOffset;
        }

        using Stream fs = OpenSourceStream(source);
        return kind is LogEncodingKind.Utf16Le or LogEncodingKind.Utf16Be
            ? FindPreviousUtf16LineStart(fs, dataOffset, currentLineStart, kind == LogEncodingKind.Utf16Le)
            : FindPreviousSingleByteLineStart(fs, dataOffset, currentLineStart);
    }

    private static long FindSingleByteLineStart(Stream fs, long dataOffset, long scanEndExclusive)
    {
        long blockEnd = scanEndExclusive;
        while (blockEnd > dataOffset)
        {
            long blockStart = Math.Max(dataOffset, blockEnd - ReverseScanBlockBytes);
            byte[] buffer = ReadWindow(fs, blockStart, blockEnd);
            for (int i = buffer.Length; i > 0; i--)
            {
                if (buffer[i - 1] is 0x0D or 0x0A)
                {
                    return blockStart + i;
                }
            }

            blockEnd = blockStart;
        }

        return dataOffset;
    }

    private static long FindUtf16LineStart(Stream fs, long dataOffset, long scanEndExclusive, bool littleEndian)
    {
        long alignedEnd = AlignUtf16Offset(dataOffset, scanEndExclusive);
        long blockEnd = alignedEnd;
        while (blockEnd > dataOffset)
        {
            long blockStart = Math.Max(dataOffset, blockEnd - ReverseScanBlockBytes);
            blockStart = AlignUtf16Offset(dataOffset, blockStart);
            byte[] buffer = ReadWindow(fs, blockStart, blockEnd);
            for (int i = buffer.Length; i >= 2; i -= 2)
            {
                ushort unit = littleEndian
                    ? (ushort)(buffer[i - 2] | (buffer[i - 1] << 8))
                    : (ushort)((buffer[i - 2] << 8) | buffer[i - 1]);
                if (unit is 0x000D or 0x000A)
                {
                    return blockStart + i;
                }
            }

            blockEnd = blockStart;
        }

        return dataOffset;
    }

    private static long FindPreviousSingleByteLineStart(Stream fs, long dataOffset, long currentLineStart)
    {
        long cursor = currentLineStart;
        while (cursor > dataOffset)
        {
            fs.Position = cursor - 1;
            int value = fs.ReadByte();
            if (value is not (0x0D or 0x0A))
            {
                break;
            }

            cursor--;
        }

        return FindSingleByteLineStart(fs, dataOffset, cursor);
    }

    private static long FindPreviousUtf16LineStart(Stream fs, long dataOffset, long currentLineStart, bool littleEndian)
    {
        long cursor = AlignUtf16Offset(dataOffset, currentLineStart);
        Span<byte> bytes = stackalloc byte[2];
        while (cursor >= dataOffset + 2)
        {
            fs.Position = cursor - 2;
            fs.ReadExactly(bytes);
            ushort unit = littleEndian
                ? (ushort)(bytes[0] | (bytes[1] << 8))
                : (ushort)((bytes[0] << 8) | bytes[1]);
            if (unit is not (0x000D or 0x000A))
            {
                break;
            }

            cursor -= 2;
        }

        return FindUtf16LineStart(fs, dataOffset, cursor, littleEndian);
    }

    private static long AlignUtf16Offset(long dataOffset, long offset)
    {
        long delta = Math.Max(0, offset - dataOffset);
        return dataOffset + (delta - (delta % 2));
    }

    private static byte[] ReadWindow(Stream fs, long startOffset, long endOffset)
    {
        int length = checked((int)Math.Max(0, endOffset - startOffset));
        byte[] buffer = new byte[length];
        fs.Position = startOffset;
        int total = 0;
        while (total < buffer.Length)
        {
            int read = fs.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        if (total != buffer.Length)
        {
            Array.Resize(ref buffer, total);
        }

        return buffer;
    }
}
