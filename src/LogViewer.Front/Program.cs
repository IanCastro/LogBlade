using System;
using System.IO;

internal static class Program
{
    internal const string AppTitle = "Log Viewer";

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppLog.Instance.Info("startup.begin", "begin", new LogField("argsCount", args.Length.ToString()));

        if (args.Length != 1)
        {
            AppLog.Instance.Error("startup.args_invalid", "invalid_arguments", new LogField("argsCount", args.Length.ToString()));
            ShowStartupError("Usage: viewer.exe <path>");
            return 1;
        }

        string path = Path.GetFullPath(args[0]);

        try
        {
            var viewer = new ViewerWindow(path);
            viewer.Run();
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Instance.Fatal(
                "runtime.fatal",
                "main_exception",
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name),
                new LogField("message", ex.Message));
            ShowStartupError("Unexpected runtime fatal: " + ex.Message);
            return 3;
        }
        finally
        {
            AppLog.Instance.Flush();
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception? ex = e.ExceptionObject as Exception;
        AppLog.Instance.Fatal(
            "runtime.fatal",
            "unhandled_exception",
            new LogField("type", ex?.GetType().FullName ?? e.ExceptionObject.GetType().FullName ?? "unknown"),
            new LogField("message", ex?.Message ?? e.ExceptionObject.ToString()),
            new LogField("terminating", e.IsTerminating.ToString()));
        AppLog.Instance.Flush();
    }

    private static void ShowStartupError(string message)
    {
        Console.Error.WriteLine(message);
        NativeMethods.MessageBoxW(IntPtr.Zero, message, AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
    }
}
