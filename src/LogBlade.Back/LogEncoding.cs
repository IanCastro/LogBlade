using System.IO;
using System.Text;

public enum LogEncodingKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Windows1252
}

public readonly record struct DetectedEncodingInfo(LogEncodingKind Kind, Encoding Encoding, long DataOffset);

public static class LogEncodingDetector
{
    public static DetectedEncodingInfo DetectEncoding(string path)
    {
        using FileStream fs = LogFileUtilities.OpenSourceStream(path);
        return LogFileUtilities.DetectEncoding(fs);
    }
}
