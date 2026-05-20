using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public readonly record struct FilteredLineDescriptor(long StartOffset, long EndOffset, int VisualRowCount, string[]? CaptureGroups = null);

internal readonly record struct RealLineData(long StartOffset, long EndOffset, string Text);

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
        if (endOffset <= startOffset)
        {
            return string.Empty;
        }

        byte[] buffer = ReadRange(fs, startOffset, endOffset);
        return encoding.GetString(buffer, 0, buffer.Length);
    }

    public static int CountVisualRows(string text)
    {
        int length = Math.Max(0, text.Length);
        return Math.Max(1, (length + VisualRowReader.VisibleSegmentChars - 1) / VisualRowReader.VisibleSegmentChars);
    }

    public static string GetVisualRowText(string text, int segmentIndex)
    {
        if (segmentIndex < 0)
        {
            return string.Empty;
        }

        int start = segmentIndex * VisualRowReader.VisibleSegmentChars;
        if (start >= text.Length)
        {
            return string.Empty;
        }

        int count = Math.Min(VisualRowReader.VisibleSegmentChars, text.Length - start);
        return text.Substring(start, count);
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
