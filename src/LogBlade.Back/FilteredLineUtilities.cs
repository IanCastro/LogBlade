using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public readonly record struct FilteredCaptureGroups(string[] Headers, string[] Values);

public readonly record struct FilteredLineDescriptor(long StartOffset, long EndOffset, int VisualRowCount, FilteredCaptureGroups? CaptureGroups = null, long LineNumber = 0);

public sealed class FilteredLineStaleException : IOException
{
    public FilteredLineStaleException(string message)
        : base(message)
    {
    }
}

internal readonly record struct RealLineData(long StartOffset, long EndOffset, string Text, long LineNumber = 0, long NextOffset = -1);

internal static class FilteredLineUtilities
{
    public static bool TryReadNextRealLine(FileStream fs, LogEncodingKind kind, Encoding encoding, long fileSize, out RealLineData line)
    {
        if (fs.Position >= fileSize)
        {
            line = default;
            return false;
        }

        long startOffset = fs.Position;
        switch (kind)
        {
            case LogEncodingKind.Utf16Le:
            case LogEncodingKind.Utf16Be:
                return TryReadUtf16RealLine(fs, kind == LogEncodingKind.Utf16Le, fileSize, startOffset, out line);
            case LogEncodingKind.Utf8:
            case LogEncodingKind.Windows1252:
                return TryReadSingleByteRealLine(fs, encoding, fileSize, startOffset, out line);
            default:
                line = default;
                return false;
        }
    }

    public static string ReadLineText(string filePath, Encoding encoding, long startOffset, long endOffset)
    {
        using FileStream fs = VisualRowReader.OpenSourceStream(filePath);
        return ReadLineText(fs, encoding, startOffset, endOffset);
    }

    public static string ReadLineText(FileStream fs, Encoding encoding, long startOffset, long endOffset)
    {
        if (endOffset < startOffset)
        {
            throw new FilteredLineStaleException("Filtered line range is invalid.");
        }

        ValidateLineRange(fs, encoding, startOffset, endOffset);
        byte[] buffer = ReadRange(fs, startOffset, endOffset);
        if (buffer.Length != endOffset - startOffset)
        {
            throw new FilteredLineStaleException("Filtered line range is no longer readable.");
        }

        return encoding.GetString(buffer, 0, buffer.Length);
    }

    public static int CountVisualRows(string text)
    {
        int rowCount = 0;
        int lineStart = 0;
        while (true)
        {
            int lineEnd = FindLineEnd(text, lineStart);
            int lineLength = lineEnd - lineStart;
            rowCount += Math.Max(1, (lineLength + VisualRowReader.VisibleSegmentChars - 1) / VisualRowReader.VisibleSegmentChars);
            if (lineEnd >= text.Length)
            {
                return rowCount;
            }

            lineStart = SkipLineBreak(text, lineEnd);
        }
    }

    public static string GetVisualRowText(string text, int segmentIndex)
    {
        if (segmentIndex < 0)
        {
            return string.Empty;
        }

        int lineStart = 0;
        int remainingSegmentIndex = segmentIndex;
        while (true)
        {
            int lineEnd = FindLineEnd(text, lineStart);
            int lineLength = lineEnd - lineStart;
            int lineSegmentCount = Math.Max(1, (lineLength + VisualRowReader.VisibleSegmentChars - 1) / VisualRowReader.VisibleSegmentChars);
            if (remainingSegmentIndex < lineSegmentCount)
            {
                int start = lineStart + (remainingSegmentIndex * VisualRowReader.VisibleSegmentChars);
                int count = Math.Min(VisualRowReader.VisibleSegmentChars, lineEnd - start);
                return text.Substring(start, count);
            }

            remainingSegmentIndex -= lineSegmentCount;
            if (lineEnd >= text.Length)
            {
                return string.Empty;
            }

            lineStart = SkipLineBreak(text, lineEnd);
        }
    }

    private static int FindLineEnd(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n')
            {
                return i;
            }
        }

        return text.Length;
    }

    private static int SkipLineBreak(string text, int lineEnd)
    {
        if (lineEnd >= text.Length)
        {
            return text.Length;
        }

        if (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n')
        {
            return lineEnd + 2;
        }

        return lineEnd + 1;
    }

    private static byte[] ReadRange(FileStream fs, long startOffset, long endOffset)
    {
        long bytesToRead = Math.Max(0, endOffset - startOffset);
        byte[] buffer = new byte[bytesToRead];
        fs.Position = startOffset;
        int total = 0;
        while (total < bytesToRead)
        {
            int read = fs.Read(buffer, total, (int)bytesToRead - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        if (total == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, total);
        return buffer;
    }

    public static void ValidateLineRange(FileStream fs, Encoding encoding, long startOffset, long endOffset)
    {
        long fileSize = fs.Length;
        if (startOffset < 0 || endOffset < startOffset || endOffset > fileSize)
        {
            throw new FilteredLineStaleException("Filtered line range is outside the current file.");
        }

        bool utf16 = IsUtf16Encoding(encoding);
        int unitSize = utf16 ? 2 : 1;
        if (utf16 && ((startOffset | endOffset) & 1) != 0)
        {
            throw new FilteredLineStaleException("Filtered UTF-16 line range is not code-unit aligned.");
        }

        if (!IsLineStart(fs, encoding, startOffset, unitSize))
        {
            throw new FilteredLineStaleException("Filtered line start is no longer at a line boundary.");
        }

        if (!IsLineEnd(fs, encoding, endOffset, fileSize, unitSize))
        {
            throw new FilteredLineStaleException("Filtered line end is no longer at a line boundary.");
        }
    }

    private static bool IsLineStart(FileStream fs, Encoding encoding, long startOffset, int unitSize)
    {
        if (startOffset == 0 || IsEncodingDataOffset(fs, encoding, startOffset))
        {
            return true;
        }

        if (startOffset < unitSize)
        {
            return false;
        }

        return IsLineBreakAt(fs, encoding, startOffset - unitSize);
    }

    private static bool IsLineEnd(FileStream fs, Encoding encoding, long endOffset, long fileSize, int unitSize)
    {
        if (endOffset == fileSize)
        {
            return true;
        }

        if (endOffset > fileSize - unitSize)
        {
            return false;
        }

        return IsLineBreakAt(fs, encoding, endOffset);
    }

    private static bool IsLineBreakAt(FileStream fs, Encoding encoding, long offset)
    {
        if (IsUtf16Encoding(encoding))
        {
            if (offset < 0 || offset > fs.Length - 2)
            {
                return false;
            }

            fs.Position = offset;
            Span<byte> bytes = stackalloc byte[2];
            int read = fs.Read(bytes);
            if (read != 2)
            {
                throw new FilteredLineStaleException("Filtered line boundary is no longer readable.");
            }

            ushort unit = IsUtf16LittleEndian(encoding)
                ? (ushort)(bytes[0] | (bytes[1] << 8))
                : (ushort)((bytes[0] << 8) | bytes[1]);
            return unit is 0x000D or 0x000A;
        }

        if (offset < 0 || offset >= fs.Length)
        {
            return false;
        }

        fs.Position = offset;
        int value = fs.ReadByte();
        if (value < 0)
        {
            throw new FilteredLineStaleException("Filtered line boundary is no longer readable.");
        }

        return value is 0x0D or 0x0A;
    }

    private static bool IsEncodingDataOffset(FileStream fs, Encoding encoding, long startOffset)
    {
        if (startOffset == 3 && string.Equals(encoding.WebName, Encoding.UTF8.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return HasBom(fs, [0xEF, 0xBB, 0xBF]);
        }

        if (startOffset == 2 && string.Equals(encoding.WebName, Encoding.Unicode.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return HasBom(fs, [0xFF, 0xFE]);
        }

        if (startOffset == 2 && string.Equals(encoding.WebName, Encoding.BigEndianUnicode.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return HasBom(fs, [0xFE, 0xFF]);
        }

        return false;
    }

    private static bool HasBom(FileStream fs, ReadOnlySpan<byte> bom)
    {
        if (fs.Length < bom.Length)
        {
            return false;
        }

        fs.Position = 0;
        Span<byte> bytes = stackalloc byte[3];
        int read = fs.Read(bytes[..bom.Length]);
        return read == bom.Length && bytes[..bom.Length].SequenceEqual(bom);
    }

    private static bool IsUtf16Encoding(Encoding encoding)
    {
        return string.Equals(encoding.WebName, Encoding.Unicode.WebName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(encoding.WebName, Encoding.BigEndianUnicode.WebName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUtf16LittleEndian(Encoding encoding)
    {
        return !string.Equals(encoding.WebName, Encoding.BigEndianUnicode.WebName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSingleByteRealLine(FileStream fs, Encoding encoding, long fileSize, long startOffset, out RealLineData line)
    {
        List<byte> bytes = new(256);
        while (fs.Position < fileSize)
        {
            long byteStart = fs.Position;
            int read = fs.ReadByte();
            if (read < 0)
            {
                break;
            }

            byte value = (byte)read;
            if (value == 0x0D || value == 0x0A)
            {
                if (value == 0x0D)
                {
                    TryConsumeSingleByteLf(fs, fileSize);
                }

                line = new RealLineData(startOffset, byteStart, encoding.GetString(bytes.ToArray(), 0, bytes.Count));
                return true;
            }

            bytes.Add(value);
        }

        line = new RealLineData(startOffset, fs.Position, encoding.GetString(bytes.ToArray(), 0, bytes.Count));
        return true;
    }

    private static bool TryReadUtf16RealLine(FileStream fs, bool littleEndian, long fileSize, long startOffset, out RealLineData line)
    {
        List<ushort> units = new(256);
        Span<byte> bytes = stackalloc byte[2];
        while (fs.Position + 1 < fileSize)
        {
            long unitStart = fs.Position;
            fs.ReadExactly(bytes);
            ushort unit = littleEndian
                ? (ushort)(bytes[0] | (bytes[1] << 8))
                : (ushort)((bytes[0] << 8) | bytes[1]);

            if (unit == 0x000D || unit == 0x000A)
            {
                if (unit == 0x000D)
                {
                    TryConsumeUtf16Lf(fs, fileSize, littleEndian);
                }

                line = new RealLineData(startOffset, unitStart, DecodeUtf16(units));
                return true;
            }

            units.Add(unit);
        }

        line = new RealLineData(startOffset, fs.Position, DecodeUtf16(units));
        return true;
    }

    private static string DecodeUtf16(List<ushort> units)
    {
        if (units.Count == 0)
        {
            return string.Empty;
        }

        char[] chars = new char[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            chars[i] = (char)units[i];
        }

        return new string(chars);
    }

    private static void TryConsumeSingleByteLf(FileStream fs, long fileSize)
    {
        if (fs.Position >= fileSize)
        {
            return;
        }

        int next = fs.ReadByte();
        if (next != 0x0A && next >= 0)
        {
            fs.Position--;
        }
    }

    private static void TryConsumeUtf16Lf(FileStream fs, long fileSize, bool littleEndian)
    {
        if (fs.Position + 1 >= fileSize)
        {
            return;
        }

        Span<byte> bytes = stackalloc byte[2];
        fs.ReadExactly(bytes);
        ushort unit = littleEndian
            ? (ushort)(bytes[0] | (bytes[1] << 8))
            : (ushort)((bytes[0] << 8) | bytes[1]);
        if (unit != 0x000A)
        {
            fs.Position -= 2;
        }
    }
}
