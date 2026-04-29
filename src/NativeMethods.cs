using System;
using System.Runtime.InteropServices;

internal static class NativeMethods
{
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int WS_VSCROLL = 0x00200000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int SW_SHOWDEFAULT = 10;
    public const int GWLP_USERDATA = -21;
    public const int WM_CREATE = 0x0001;
    public const int WM_DESTROY = 0x0002;
    public const int WM_SIZE = 0x0005;
    public const int WM_PAINT = 0x000F;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_VSCROLL = 0x0115;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_ERASEBKGND = 0x0014;
    public const int WM_NCCREATE = 0x0081;
    public const int WM_APP = 0x8000;
    public const int WM_APP_BEGIN_OPEN = WM_APP + 1;
    public const int WM_APP_OPEN_COMPLETE = WM_APP + 2;
    public const int SB_LINEUP = 0;
    public const int SB_LINEDOWN = 1;
    public const int SB_PAGEUP = 2;
    public const int SB_PAGEDOWN = 3;
    public const int SB_THUMBPOSITION = 4;
    public const int SB_THUMBTRACK = 5;
    public const int SB_TOP = 6;
    public const int SB_BOTTOM = 7;
    public const int SB_VERT = 1;
    public const int SIF_RANGE = 0x0001;
    public const int SIF_PAGE = 0x0002;
    public const int SIF_POS = 0x0004;
    public const int SIF_TRACKPOS = 0x0010;
    public const int TRANSPARENT = 1;
    public const int COLOR_WINDOW = 5;
    public const int COLOR_3DFACE = 15;
    public const int DEFAULT_CHARSET = 1;
    public const int OUT_DEFAULT_PRECIS = 0;
    public const int OUT_OUTLINE_PRECIS = 8;
    public const int CLIP_DEFAULT_PRECIS = 0;
    public const int CLEARTYPE_QUALITY = 5;
    public const int FF_MODERN = 0x30;
    public const int FW_NORMAL = 400;
    public const int SYSTEM_FIXED_FONT = 16;
    public const int DT_LEFT = 0x0000;
    public const int DT_VCENTER = 0x0004;
    public const int DT_SINGLELINE = 0x0020;
    public const int DT_END_ELLIPSIS = 0x8000;
    public const int DT_NOPREFIX = 0x0800;
    public const int WHEEL_DELTA = 120;
    public const int MB_OK = 0x00000000;
    public const int MB_ICONERROR = 0x00000010;
    public const int VK_UP = 0x26;
    public const int VK_DOWN = 0x28;
    public const int VK_PRIOR = 0x21;
    public const int VK_NEXT = 0x22;
    public const int VK_HOME = 0x24;
    public const int VK_END = 0x23;
    public static readonly IntPtr IDC_ARROW = new(32512);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WindowProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREATESTRUCTW
    {
        public IntPtr lpCreateParams;
        public IntPtr hInstance;
        public IntPtr hMenu;
        public IntPtr hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public int style;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszClass;
        public uint dwExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public char tmFirstChar;
        public char tmLastChar;
        public char tmDefaultChar;
        public char tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SCROLLINFO
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW([In] ref MSG lpmsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll")]
    public static extern IntPtr GetSysColorBrush(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern int TextOutW(IntPtr hdc, int x, int y, string lpString, int c);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, int format);

    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    public static extern int SetTextColor(IntPtr hdc, int color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(
        int cHeight,
        int cWidth,
        int cEscapement,
        int cOrientation,
        int cWeight,
        uint bItalic,
        uint bUnderline,
        uint bStrikeOut,
        int iCharSet,
        int iOutPrecision,
        int iClipPrecision,
        int iQuality,
        int iPitchAndFamily,
        string? pszFaceName);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool GetTextMetricsW(IntPtr hdc, out TEXTMETRICW lptm);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int i);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetScrollInfo(IntPtr hwnd, int nBar, ref SCROLLINFO lpsi, bool redraw);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetScrollInfo(IntPtr hwnd, int nBar, ref SCROLLINFO lpsi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, int uType);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool GetProcessMemoryInfo(IntPtr process, out PROCESS_MEMORY_COUNTERS_EX counters, int cb);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    public static int LowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    public static int HighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    public static int RGB(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
