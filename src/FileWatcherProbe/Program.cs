using System;
using System.Globalization;
using System.IO;
using System.Threading;

internal static class Program
{
    private static readonly object s_consoleSync = new();
    private static int s_sequence;

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: FileWatcherProbe.exe <file-path>");
            return 1;
        }

        string targetPath = Path.GetFullPath(args[0]);
        string? directory = Path.GetDirectoryName(targetPath);
        string fileName = Path.GetFileName(targetPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            Console.Error.WriteLine("Invalid file path: " + targetPath);
            return 1;
        }

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine("Directory does not exist: " + directory);
            return 1;
        }

        Console.WriteLine("Watching file:");
        Console.WriteLine(targetPath);
        Console.WriteLine("Press Enter or Ctrl+C to stop.");
        Console.WriteLine();

        PrintMeasurement("Initial", targetPath, eventPath: targetPath, oldPath: null);

        using ManualResetEventSlim stop = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        using FileSystemWatcher watcher = new(directory, fileName)
        {
            IncludeSubdirectories = false,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.Size |
                NotifyFilters.LastWrite |
                NotifyFilters.CreationTime |
                NotifyFilters.Attributes
        };

        watcher.Changed += (_, e) => PrintMeasurement(e.ChangeType.ToString(), targetPath, e.FullPath, oldPath: null);
        watcher.Created += (_, e) => PrintMeasurement(e.ChangeType.ToString(), targetPath, e.FullPath, oldPath: null);
        watcher.Deleted += (_, e) => PrintMeasurement(e.ChangeType.ToString(), targetPath, e.FullPath, oldPath: null);
        watcher.Renamed += (_, e) => PrintMeasurement(e.ChangeType.ToString(), targetPath, e.FullPath, e.OldFullPath);
        watcher.Error += (_, e) => PrintWatcherError(e.GetException());
        watcher.EnableRaisingEvents = true;

        Thread inputThread = new(() =>
        {
            try
            {
                _ = Console.ReadLine();
            }
            catch
            {
            }

            stop.Set();
        })
        {
            IsBackground = true
        };
        inputThread.Start();

        stop.Wait();
        return 0;
    }

    private static void PrintMeasurement(string eventName, string targetPath, string eventPath, string? oldPath)
    {
        int sequence = Interlocked.Increment(ref s_sequence);
        DateTimeOffset timestamp = DateTimeOffset.Now;
        FileMeasurement measurement = MeasureFile(targetPath);

        lock (s_consoleSync)
        {
            Console.Write(timestamp.ToString("O", CultureInfo.InvariantCulture));
            Console.Write(" #");
            Console.Write(sequence.ToString(CultureInfo.InvariantCulture));
            Console.Write(" event=");
            Console.Write(eventName);
            Console.Write(" targetExists=");
            Console.Write(measurement.Exists ? "true" : "false");
            Console.Write(" targetSize=");
            Console.Write(measurement.SizeText);
            Console.Write(" eventPath=");
            Console.Write(Quote(eventPath));

            if (!string.IsNullOrEmpty(oldPath))
            {
                Console.Write(" oldPath=");
                Console.Write(Quote(oldPath));
            }

            if (!string.IsNullOrEmpty(measurement.Error))
            {
                Console.Write(" error=");
                Console.Write(Quote(measurement.Error));
            }

            Console.WriteLine();
        }
    }

    private static FileMeasurement MeasureFile(string targetPath)
    {
        try
        {
            FileInfo info = new(targetPath);
            if (!info.Exists)
            {
                return new FileMeasurement(false, "missing", null);
            }

            return new FileMeasurement(true, info.Length.ToString(CultureInfo.InvariantCulture), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileMeasurement(false, "unavailable", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void PrintWatcherError(Exception? exception)
    {
        lock (s_consoleSync)
        {
            Console.Write(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            Console.Write(" watcherError=");
            Console.WriteLine(Quote(exception?.Message ?? "unknown"));
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private readonly record struct FileMeasurement(bool Exists, string SizeText, string? Error);
}
