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
        => DetectEncoding(LogContentSource.FromFile(path));

    public static DetectedEncodingInfo DetectEncoding(LogContentSource source)
    {
        using Stream fs = LogFileUtilities.OpenSourceStream(source);
        return LogFileUtilities.DetectEncoding(fs);
    }
}
