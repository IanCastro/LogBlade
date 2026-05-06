using System;
using System.Text;

internal sealed class Windows1252Encoding : Encoding
{
    public static readonly Windows1252Encoding Instance = new();

    private Windows1252Encoding()
    {
    }

    public override int CodePage => 1252;
    public override string WebName => "windows-1252";
    public override string EncodingName => "Windows-1252";
    public override bool IsSingleByte => true;

    public override int GetByteCount(char[] chars, int index, int count) => count;

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        int written = 0;
        for (int i = 0; i < charCount; i++)
        {
            bytes[byteIndex + written++] = EncodeChar(chars[charIndex + i]);
        }

        return written;
    }

    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        int count = Math.Min(chars.Length, bytes.Length);
        for (int i = 0; i < count; i++)
        {
            bytes[i] = EncodeChar(chars[i]);
        }

        return count;
    }

    public override int GetCharCount(byte[] bytes, int index, int count) => count;

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        for (int i = 0; i < byteCount; i++)
        {
            chars[charIndex + i] = DecodeByte(bytes[byteIndex + i]);
        }

        return byteCount;
    }

    public override Decoder GetDecoder() => new Windows1252Decoder();
    public override byte[] GetPreamble() => Array.Empty<byte>();
    public override int GetMaxByteCount(int charCount) => charCount;
    public override int GetMaxCharCount(int byteCount) => byteCount;

    private static byte EncodeChar(char value)
    {
        return value switch
        {
            '\u20AC' => 0x80,
            '\u201A' => 0x82,
            '\u0192' => 0x83,
            '\u201E' => 0x84,
            '\u2026' => 0x85,
            '\u2020' => 0x86,
            '\u2021' => 0x87,
            '\u02C6' => 0x88,
            '\u2030' => 0x89,
            '\u0160' => 0x8A,
            '\u2039' => 0x8B,
            '\u0152' => 0x8C,
            '\u017D' => 0x8E,
            '\u2018' => 0x91,
            '\u2019' => 0x92,
            '\u201C' => 0x93,
            '\u201D' => 0x94,
            '\u2022' => 0x95,
            '\u2013' => 0x96,
            '\u2014' => 0x97,
            '\u02DC' => 0x98,
            '\u2122' => 0x99,
            '\u0161' => 0x9A,
            '\u203A' => 0x9B,
            '\u0153' => 0x9C,
            '\u017E' => 0x9E,
            '\u0178' => 0x9F,
            <= '\u00FF' => (byte)value,
            _ => (byte)'?'
        };
    }

    private static char DecodeByte(byte value)
    {
        return value switch
        {
            0x80 => '\u20AC',
            0x82 => '\u201A',
            0x83 => '\u0192',
            0x84 => '\u201E',
            0x85 => '\u2026',
            0x86 => '\u2020',
            0x87 => '\u2021',
            0x88 => '\u02C6',
            0x89 => '\u2030',
            0x8A => '\u0160',
            0x8B => '\u2039',
            0x8C => '\u0152',
            0x8E => '\u017D',
            0x91 => '\u2018',
            0x92 => '\u2019',
            0x93 => '\u201C',
            0x94 => '\u201D',
            0x95 => '\u2022',
            0x96 => '\u2013',
            0x97 => '\u2014',
            0x98 => '\u02DC',
            0x99 => '\u2122',
            0x9A => '\u0161',
            0x9B => '\u203A',
            0x9C => '\u0153',
            0x9E => '\u017E',
            0x9F => '\u0178',
            _ => (char)value
        };
    }

    private sealed class Windows1252Decoder : Decoder
    {
        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            for (int i = 0; i < byteCount; i++)
            {
                chars[charIndex + i] = DecodeByte(bytes[byteIndex + i]);
            }

            return byteCount;
        }
    }
}
