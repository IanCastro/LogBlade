using System;
using System.Collections.Generic;

internal static class AuxiliaryWindowRegistry
{
    private static readonly List<IntPtr> s_openWindows = new();
    private static readonly HashSet<IntPtr> s_hiddenByMainWindow = new();
    private static bool s_mainWindowMinimized;

    public static void Register(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || s_openWindows.Contains(hwnd))
        {
            return;
        }

        s_openWindows.Add(hwnd);
        if (s_mainWindowMinimized)
        {
            s_hiddenByMainWindow.Add(hwnd);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        }
    }

    public static void Unregister(IntPtr hwnd)
    {
        s_openWindows.Remove(hwnd);
        s_hiddenByMainWindow.Remove(hwnd);
    }

    public static void SetMainWindowMinimized(bool minimized)
    {
        if (s_mainWindowMinimized == minimized)
        {
            return;
        }

        s_mainWindowMinimized = minimized;
        if (minimized)
        {
            s_hiddenByMainWindow.Clear();
            for (int i = s_openWindows.Count - 1; i >= 0; i--)
            {
                IntPtr hwnd = s_openWindows[i];
                s_hiddenByMainWindow.Add(hwnd);
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            }

            return;
        }

        for (int i = 0; i < s_openWindows.Count; i++)
        {
            IntPtr hwnd = s_openWindows[i];
            if (s_hiddenByMainWindow.Contains(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            }
        }

        s_hiddenByMainWindow.Clear();
    }
}
