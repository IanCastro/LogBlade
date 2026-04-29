using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;

internal readonly record struct LogField(string Key, string? Value);

internal static class AppLog
{
    public static readonly SessionLog Instance = SessionLog.Create("mvp-csharp-nativeaot-win32");
}

internal sealed class SessionLog : IDisposable
{
    private const int RetainedLogCount = 20;

    private readonly object _sync = new();
    private readonly string _app;
    private readonly int _pid;
    private readonly string _session;
    private readonly StreamWriter? _writer;

    private SessionLog(string app, int pid, string session, StreamWriter? writer)
    {
        _app = app;
        _pid = pid;
        _session = session;
        _writer = writer;
    }

    public static SessionLog Create(string app)
    {
        int pid = Environment.ProcessId;
        string session = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + pid.ToString(CultureInfo.InvariantCulture);
        StreamWriter? writer = null;

        foreach (string? basePath in EnumerateBasePaths())
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                continue;
            }

            string logDir = Path.Combine(basePath, "LogReaderMvp", app, "logs");
            try
            {
                Directory.CreateDirectory(logDir);
                TryCleanupOldLogs(logDir);

                string logPath = Path.Combine(logDir, session + ".log");
                var stream = new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                break;
            }
            catch
            {
            }
        }

        return new SessionLog(app, pid, session, writer);
    }

    public void Info(string eventName, string message, params LogField[] fields) => Write("INFO", eventName, message, fields);

    public void Error(string eventName, string message, params LogField[] fields) => Write("ERROR", eventName, message, fields);

    public void Fatal(string eventName, string message, params LogField[] fields) => Write("FATAL", eventName, message, fields);

    public void Flush()
    {
        try
        {
            lock (_sync)
            {
                _writer?.Flush();
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            lock (_sync)
            {
                _writer?.Dispose();
            }
        }
        catch
        {
        }
    }

    private void Write(string level, string eventName, string message, ReadOnlySpan<LogField> fields)
    {
        if (_writer is null)
        {
            return;
        }

        try
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var line = new StringBuilder();
            line.Append(timestamp);
            line.Append(' ');
            line.Append(level);
            line.Append(' ');
            line.Append(_app);
            line.Append(' ');
            line.Append(_pid);
            line.Append(' ');
            line.Append(_session);
            line.Append(' ');
            line.Append(eventName);
            line.Append(' ');
            line.Append(message);

            foreach (LogField field in fields)
            {
                line.Append(' ');
                line.Append(field.Key);
                line.Append('=');
                line.Append(Quote(field.Value));
            }

            lock (_sync)
            {
                _writer.WriteLine(line.ToString());
            }
        }
        catch
        {
        }
    }

    private static IEnumerable<string?> EnumerateBasePaths()
    {
        yield return Environment.GetEnvironmentVariable("LOCALAPPDATA");
        yield return Path.GetTempPath();
    }

    private static void TryCleanupOldLogs(string logDir)
    {
        try
        {
            string[] files = Directory.GetFiles(logDir, "*.log");
            Array.Sort(files, (a, b) =>
            {
                DateTime aTime = File.GetLastWriteTimeUtc(a);
                DateTime bTime = File.GetLastWriteTimeUtc(b);
                int compare = bTime.CompareTo(aTime);
                return compare != 0 ? compare : StringComparer.OrdinalIgnoreCase.Compare(b, a);
            });

            for (int i = RetainedLogCount; i < files.Length; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return "\""
            + value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
            + "\"";
    }
}
