using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class SearchRealLineScanner
{
    private const int BlockBytes = 1024 * 1024;
    internal const int RequiredBufferBytes = BlockBytes + 1;

    public static IEnumerable<RealLineData> Enumerate(string filePath, Encoding encoding, LogEncodingKind kind, long dataOffset, long fileSize)
        => Enumerate(LogContentSource.FromFile(filePath), encoding, kind, dataOffset, fileSize, CancellationToken.None);

    public static IEnumerable<RealLineData> Enumerate(string filePath, Encoding encoding, LogEncodingKind kind, long dataOffset, long fileSize, CancellationToken cancellationToken)
        => Enumerate(LogContentSource.FromFile(filePath), encoding, kind, dataOffset, fileSize, cancellationToken, firstLineNumber: 1);

    public static IEnumerable<RealLineData> Enumerate(string filePath, Encoding encoding, LogEncodingKind kind, long dataOffset, long fileSize, CancellationToken cancellationToken, long firstLineNumber)
        => Enumerate(LogContentSource.FromFile(filePath), encoding, kind, dataOffset, fileSize, cancellationToken, firstLineNumber);

    public static IEnumerable<RealLineData> Enumerate(LogContentSource source, Encoding encoding, LogEncodingKind kind, long dataOffset, long fileSize)
        => Enumerate(source, encoding, kind, dataOffset, fileSize, CancellationToken.None);

    public static IEnumerable<RealLineData> Enumerate(LogContentSource source, Encoding encoding, LogEncodingKind kind, long dataOffset, long fileSize, CancellationToken cancellationToken)
        => Enumerate(source, encoding, kind, dataOffset, fileSize, cancellationToken, firstLineNumber: 1);

    public static IEnumerable<RealLineData> Enumerate(LogContentSource source, Encoding encoding, LogEncodingKind kind, long dataOffset, long fileSize, CancellationToken cancellationToken, long firstLineNumber)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Stream fs = LogFileUtilities.OpenSourceStream(source);
        byte[] buffer = new byte[RequiredBufferBytes];
        foreach (RealLineData line in Enumerate(fs, encoding, kind, dataOffset, fileSize, buffer, cancellationToken, firstLineNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    internal static IEnumerable<RealLineData> Enumerate(
        Stream fs,
        Encoding encoding,
        LogEncodingKind kind,
        long dataOffset,
        long fileSize,
        byte[] buffer,
        CancellationToken cancellationToken,
        long firstLineNumber)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length < RequiredBufferBytes)
        {
            throw new ArgumentException($"Scanner buffer must contain at least {RequiredBufferBytes} bytes.", nameof(buffer));
        }

        cancellationToken.ThrowIfCancellationRequested();
        fs.Position = dataOffset;
        IEnumerable<RealLineData> lines = kind switch
        {
            LogEncodingKind.Utf16Le => EnumerateUtf16(fs, encoding, dataOffset, fileSize, buffer, littleEndian: true, cancellationToken: cancellationToken, firstLineNumber: firstLineNumber),
            LogEncodingKind.Utf16Be => EnumerateUtf16(fs, encoding, dataOffset, fileSize, buffer, littleEndian: false, cancellationToken: cancellationToken, firstLineNumber: firstLineNumber),
            LogEncodingKind.Utf8 or LogEncodingKind.Windows1252 => EnumerateSingleByte(fs, encoding, dataOffset, fileSize, buffer, cancellationToken, firstLineNumber),
            _ => Array.Empty<RealLineData>()
        };

        foreach (RealLineData line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    private static IEnumerable<RealLineData> EnumerateSingleByte(Stream fs, Encoding encoding, long dataOffset, long fileSize, byte[] buffer, CancellationToken cancellationToken, long firstLineNumber)
    {
        ArrayBufferWriter<byte>? pendingLine = null;
        long lineStart = dataOffset;
        long lineNumber = Math.Max(1, firstLineNumber);
        bool skipLf = false;

        while (fs.Position < fileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long bufferStart = fs.Position;
            int read = fs.Read(buffer, 0, (int)Math.Min(BlockBytes, fileSize - fs.Position));
            if (read <= 0)
            {
                break;
            }

            int segmentStart = 0;
            int index = 0;
            if (skipLf)
            {
                skipLf = false;
                if (read > 0 && buffer[0] == 0x0A)
                {
                    index = 1;
                    segmentStart = 1;
                    lineStart = bufferStart + 1;
                }
            }

            for (; index < read; index++)
            {
                byte value = buffer[index];
                if (value != 0x0D && value != 0x0A)
                {
                    continue;
                }

                long breakOffset = bufferStart + index;
                long nextOffset = bufferStart + index + 1;
                cancellationToken.ThrowIfCancellationRequested();
                string text = DecodeCurrentLine(encoding, buffer.AsSpan(segmentStart, index - segmentStart), pendingLine);
                pendingLine = null;
                if (value == 0x0D && index + 1 < read && buffer[index + 1] == 0x0A)
                {
                    nextOffset = bufferStart + index + 2;
                }

                yield return new RealLineData(lineStart, breakOffset, text, lineNumber++, nextOffset);
                cancellationToken.ThrowIfCancellationRequested();

                if (value == 0x0D)
                {
                    if (index + 1 < read && buffer[index + 1] == 0x0A)
                    {
                        index++;
                    }
                    else if (index + 1 == read)
                    {
                        skipLf = true;
                    }
                }

                lineStart = bufferStart + index + 1;
                segmentStart = index + 1;
            }

            if (segmentStart < read)
            {
                pendingLine ??= new ArrayBufferWriter<byte>(Math.Max(256, read - segmentStart));
                pendingLine.Write(buffer.AsSpan(segmentStart, read - segmentStart));
            }
        }

        if (pendingLine is not null && pendingLine.WrittenCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
                yield return new RealLineData(lineStart, fileSize, encoding.GetString(pendingLine.WrittenSpan), lineNumber, fileSize);
        }
        else if (lineStart < fileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RealLineData(lineStart, fileSize, string.Empty, lineNumber, fileSize);
        }
    }

    private static IEnumerable<RealLineData> EnumerateUtf16(Stream fs, Encoding encoding, long dataOffset, long fileSize, byte[] buffer, bool littleEndian, CancellationToken cancellationToken, long firstLineNumber)
    {
        ArrayBufferWriter<byte>? pendingLine = null;
        long lineStart = dataOffset;
        long lineNumber = Math.Max(1, firstLineNumber);
        bool skipLf = false;
        int carryCount = 0;
        long carryOffset = 0;

        while (fs.Position < fileSize || carryCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long readStart = fs.Position;
            int bytesRead = fs.Position < fileSize
                ? fs.Read(buffer, carryCount, (int)Math.Min(BlockBytes, fileSize - fs.Position))
                : 0;
            int total = carryCount + bytesRead;
            if (total <= 0)
            {
                break;
            }

            long bufferStart = carryCount > 0 ? carryOffset : readStart;
            int processLength = total & ~1;
            int segmentStart = 0;
            int index = 0;

            if (skipLf && processLength >= 2)
            {
                skipLf = false;
                if (ReadCodeUnit(buffer, 0, littleEndian) == 0x000A)
                {
                    index = 2;
                    segmentStart = 2;
                    lineStart = bufferStart + 2;
                }
            }

            for (; index + 1 < processLength; index += 2)
            {
                ushort unit = ReadCodeUnit(buffer, index, littleEndian);
                if (unit != 0x000D && unit != 0x000A)
                {
                    continue;
                }

                long breakOffset = bufferStart + index;
                long nextOffset = bufferStart + index + 2;
                cancellationToken.ThrowIfCancellationRequested();
                string text = DecodeCurrentLine(encoding, buffer.AsSpan(segmentStart, index - segmentStart), pendingLine);
                pendingLine = null;
                if (unit == 0x000D && index + 3 < processLength && ReadCodeUnit(buffer, index + 2, littleEndian) == 0x000A)
                {
                    nextOffset = bufferStart + index + 4;
                }

                yield return new RealLineData(lineStart, breakOffset, text, lineNumber++, nextOffset);
                cancellationToken.ThrowIfCancellationRequested();

                if (unit == 0x000D)
                {
                    if (index + 3 < processLength && ReadCodeUnit(buffer, index + 2, littleEndian) == 0x000A)
                    {
                        index += 2;
                    }
                    else if (index + 2 == processLength)
                    {
                        skipLf = true;
                    }
                }

                lineStart = bufferStart + index + 2;
                segmentStart = index + 2;
            }

            if (segmentStart < processLength)
            {
                pendingLine ??= new ArrayBufferWriter<byte>(Math.Max(256, processLength - segmentStart));
                pendingLine.Write(buffer.AsSpan(segmentStart, processLength - segmentStart));
            }

            carryCount = total - processLength;
            if (carryCount > 0)
            {
                carryOffset = bufferStart + processLength;
                buffer[0] = buffer[processLength];
            }
        }

        if (pendingLine is not null && pendingLine.WrittenCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
                yield return new RealLineData(lineStart, fileSize, encoding.GetString(pendingLine.WrittenSpan), lineNumber, fileSize);
        }
        else if (lineStart < fileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RealLineData(lineStart, fileSize, string.Empty, lineNumber, fileSize);
        }
    }

    private static string DecodeCurrentLine(Encoding encoding, ReadOnlySpan<byte> currentSegment, ArrayBufferWriter<byte>? pendingLine)
    {
        if (pendingLine is null)
        {
            return encoding.GetString(currentSegment);
        }

        if (!currentSegment.IsEmpty)
        {
            pendingLine.Write(currentSegment);
        }

        return encoding.GetString(pendingLine.WrittenSpan);
    }

    private static ushort ReadCodeUnit(byte[] buffer, int index, bool littleEndian)
    {
        return littleEndian
            ? (ushort)(buffer[index] | (buffer[index + 1] << 8))
            : (ushort)((buffer[index] << 8) | buffer[index + 1]);
    }
}
