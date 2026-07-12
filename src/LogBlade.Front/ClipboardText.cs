using System;
using System.Runtime.InteropServices;

internal static class ClipboardText
{
    public static bool TryGetText(IntPtr owner, out string text)
    {
        text = string.Empty;
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT) ||
            !NativeMethods.OpenClipboard(owner))
        {
            return false;
        }

        try
        {
            IntPtr handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr data = NativeMethods.GlobalLock(handle);
            if (data == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(data) ?? string.Empty;
                return text.Length > 0;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public static bool TrySetText(IntPtr owner, string text)
    {
        if (string.IsNullOrEmpty(text) || !NativeMethods.OpenClipboard(owner))
        {
            return false;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            NativeMethods.EmptyClipboard();
            char[] chars = (text + "\0").ToCharArray();
            nuint byteCount = (nuint)(chars.Length * sizeof(char));
            handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, new UIntPtr(byteCount));
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr target = NativeMethods.GlobalLock(handle);
            if (target == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
                handle = IntPtr.Zero;
                return false;
            }

            Marshal.Copy(chars, 0, target, chars.Length);
            NativeMethods.GlobalUnlock(handle);

            if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, handle) == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
                handle = IntPtr.Zero;
                return false;
            }

            handle = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
            }

            NativeMethods.CloseClipboard();
        }
    }
}
