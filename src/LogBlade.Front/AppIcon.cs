using System;

internal static class AppIcon
{
    private const int MainIconResourceId = 32512;

    private static readonly IntPtr s_bigIcon = LoadAppIcon(32);
    private static readonly IntPtr s_smallIcon = LoadAppIcon(16);

    public static IntPtr Big => s_bigIcon;

    public static IntPtr Small => s_smallIcon;

    public static void ApplyToWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SendMessageW(hwnd, NativeMethods.WM_SETICON, new IntPtr(NativeMethods.ICON_BIG), Big);
        NativeMethods.SendMessageW(hwnd, NativeMethods.WM_SETICON, new IntPtr(NativeMethods.ICON_SMALL), Small);
    }

    private static IntPtr LoadAppIcon(int size)
    {
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        IntPtr resource = new(MainIconResourceId);
        IntPtr icon = NativeMethods.LoadImageW(
            hInstance,
            resource,
            NativeMethods.IMAGE_ICON,
            size,
            size,
            NativeMethods.LR_DEFAULTCOLOR | NativeMethods.LR_SHARED);

        if (icon != IntPtr.Zero)
        {
            return icon;
        }

        icon = NativeMethods.LoadIconW(hInstance, resource);
        if (icon != IntPtr.Zero)
        {
            return icon;
        }

        return NativeMethods.LoadIconW(IntPtr.Zero, NativeMethods.IDI_APPLICATION);
    }
}
