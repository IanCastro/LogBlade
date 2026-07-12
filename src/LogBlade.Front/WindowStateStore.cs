using System;
using System.IO;
using System.Text;
using System.Text.Json;

internal sealed class WindowStateSettings
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string WindowState { get; set; } = WindowStateStore.NormalState;
}

internal static class WindowStateStore
{
    internal const string NormalState = "Normal";
    internal const string MaximizedState = "Maximized";
    private const int MinRestoredWidth = 320;
    private const int MinRestoredHeight = 240;

    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LogBlade",
        "window-state.json");

    public static WindowStateSettings? Load()
        => Load(StorePath, RectIntersectsAnyMonitor);

    public static void Save(WindowStateSettings settings)
        => Save(StorePath, settings);

    internal static WindowStateSettings? Load(string path, Func<NativeMethods.RECT, bool> rectValidator)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            WindowStateSettings? settings = JsonSerializer.Deserialize(json, LogBladeJsonSerializerContext.Default.WindowStateSettings);
            return IsValidForRestore(settings, rectValidator) ? settings : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static void Save(string path, WindowStateSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string json = JsonSerializer.Serialize(settings, LogBladeJsonSerializerContext.Default.WindowStateSettings);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static bool IsValidForRestore(WindowStateSettings? settings, Func<NativeMethods.RECT, bool> rectValidator)
    {
        if (settings is null ||
            settings.Width < MinRestoredWidth ||
            settings.Height < MinRestoredHeight ||
            (settings.WindowState != NormalState && settings.WindowState != MaximizedState))
        {
            return false;
        }

        long right = (long)settings.Left + settings.Width;
        long bottom = (long)settings.Top + settings.Height;
        if (right > int.MaxValue || bottom > int.MaxValue)
        {
            return false;
        }

        NativeMethods.RECT rect = new()
        {
            left = settings.Left,
            top = settings.Top,
            right = (int)right,
            bottom = (int)bottom
        };

        if (rect.right <= rect.left || rect.bottom <= rect.top)
        {
            return false;
        }

        return rectValidator(rect);
    }

    private static bool RectIntersectsAnyMonitor(NativeMethods.RECT rect)
        => NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero;
}
