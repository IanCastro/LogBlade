using System;
using System.IO;

public sealed class LogContentSource
{
    private readonly string? _filePath;
    private readonly byte[]? _memory;
    private readonly int _memoryLength;

    private LogContentSource(string displayName, string? filePath, byte[]? memory, int memoryLength)
    {
        DisplayName = displayName;
        _filePath = filePath;
        _memory = memory;
        _memoryLength = memoryLength;
    }

    public string DisplayName { get; }
    public string? FilePath => _filePath;
    public bool IsFile => _filePath is not null;
    public bool Exists => _memory is not null || (_filePath is not null && File.Exists(_filePath));
    public long Length => _memory is not null ? _memoryLength : new FileInfo(_filePath!).Length;

    public static LogContentSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        return new LogContentSource(fullPath, fullPath, null, 0);
    }

    public static LogContentSource FromMemory(string displayName, byte[] content)
        => FromMemory(displayName, content, content?.Length ?? 0);

    public static LogContentSource FromMemory(string displayName, byte[] content, int length)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(content);
        if ((uint)length > (uint)content.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return new LogContentSource(displayName, null, content, length);
    }

    public Stream OpenRead()
    {
        if (_memory is not null)
        {
            return new MemoryStream(_memory, 0, _memoryLength, writable: false, publiclyVisible: false);
        }

        return new FileStream(
            _filePath!,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            1 << 16,
            FileOptions.SequentialScan);
    }
}
