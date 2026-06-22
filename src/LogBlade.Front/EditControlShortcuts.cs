using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal static class EditControlShortcuts
{
    private static readonly NativeMethods.WindowProc s_editProc = EditProc;
    private static readonly IntPtr s_editProcPtr = Marshal.GetFunctionPointerForDelegate(s_editProc);
    private static readonly Dictionary<IntPtr, IntPtr> s_originalProcs = new();

    public static void AttachSelectAll(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (s_originalProcs)
        {
            if (s_originalProcs.ContainsKey(hwnd))
            {
                return;
            }

            IntPtr originalProc = NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_WNDPROC, s_editProcPtr);
            s_originalProcs[hwnd] = originalProc;
        }
    }

    private static IntPtr EditProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr originalProc = GetOriginalProc(hwnd);
        if (msg == NativeMethods.WM_KEYDOWN &&
            wParam.ToInt32() == NativeMethods.VK_A &&
            IsControlKeyDown())
        {
            NativeMethods.SendMessageW(hwnd, NativeMethods.EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_NCDESTROY)
        {
            RestoreOriginalProc(hwnd, originalProc);
        }

        return originalProc == IntPtr.Zero
            ? NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam)
            : NativeMethods.CallWindowProcW(originalProc, hwnd, msg, wParam, lParam);
    }

    private static IntPtr GetOriginalProc(IntPtr hwnd)
    {
        lock (s_originalProcs)
        {
            return s_originalProcs.TryGetValue(hwnd, out IntPtr originalProc)
                ? originalProc
                : IntPtr.Zero;
        }
    }

    private static void RestoreOriginalProc(IntPtr hwnd, IntPtr originalProc)
    {
        lock (s_originalProcs)
        {
            s_originalProcs.Remove(hwnd);
        }

        if (originalProc != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_WNDPROC, originalProc);
        }
    }

    private static bool IsControlKeyDown() =>
        (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & unchecked((short)0x8000)) != 0;
}
