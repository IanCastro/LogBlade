using System;
using System.Runtime.InteropServices;

internal static class NativeMethods
{
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const int CS_DBLCLKS = 0x0008;
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_TABSTOP = 0x00010000;
    public const int WS_BORDER = 0x00800000;
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_GROUP = 0x00020000;
    public const int WS_HSCROLL = 0x00100000;
    public const int WS_VSCROLL = 0x00200000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int ES_AUTOHSCROLL = 0x0080;
    public const int ES_AUTOVSCROLL = 0x0040;
    public const int ES_MULTILINE = 0x0004;
    public const int ES_READONLY = 0x0800;
    public const uint EM_SETREADONLY = 0x00CF;
    public const int ES_WANTRETURN = 0x1000;
    public const int BS_AUTOCHECKBOX = 0x00000003;
    public const int BS_AUTORADIOBUTTON = 0x00000009;
    public const int BS_PUSHBUTTON = 0x00000000;
    public const int CBS_DROPDOWNLIST = 0x0003;
    public const int CBS_HASSTRINGS = 0x0200;
    public const int LBS_NOTIFY = 0x0001;
    public const int LBS_OWNERDRAWFIXED = 0x0010;
    public const int LBS_HASSTRINGS = 0x0040;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_SHOWDEFAULT = 10;
    public const int SIZE_RESTORED = 0;
    public const int SIZE_MINIMIZED = 1;
    public const int SIZE_MAXIMIZED = 2;
    public const int SM_CXDRAG = 68;
    public const int SM_CYDRAG = 69;
    public const int SRCCOPY = 0x00CC0020;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const int GWLP_WNDPROC = -4;
    public const int GWLP_USERDATA = -21;
    public const int WM_CREATE = 0x0001;
    public const int WM_DESTROY = 0x0002;
    public const int WM_CLOSE = 0x0010;
    public const int WM_COMMAND = 0x0111;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_DRAWITEM = 0x002B;
    public const int WM_CTLCOLORSTATIC = 0x0138;
    public const int WM_SIZE = 0x0005;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_PAINT = 0x000F;
    public const int WM_SETFOCUS = 0x0007;
    public const int WM_KILLFOCUS = 0x0008;
    public const int WM_SETFONT = 0x0030;
    public const int WM_SETICON = 0x0080;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_TIMER = 0x0113;
    public const int WM_HSCROLL = 0x0114;
    public const int WM_VSCROLL = 0x0115;
    public const int WM_SETCURSOR = 0x0020;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_ERASEBKGND = 0x0014;
    public const int WM_NCCREATE = 0x0081;
    public const int WM_NCDESTROY = 0x0082;
    public const int SC_MINIMIZE = 0xF020;
    public const int BM_GETCHECK = 0x00F0;
    public const int BM_SETCHECK = 0x00F1;
    public const int CBN_SELCHANGE = 1;
    public const int CB_ADDSTRING = 0x0143;
    public const int CB_RESETCONTENT = 0x014B;
    public const int CB_GETCURSEL = 0x0147;
    public const int CB_SETCURSEL = 0x014E;
    public const int CB_ERR = -1;
    public const int LB_ADDSTRING = 0x0180;
    public const int LB_RESETCONTENT = 0x0184;
    public const int LB_SETCURSEL = 0x0186;
    public const int LB_GETCURSEL = 0x0188;
    public const int LB_SETITEMHEIGHT = 0x01A0;
    public const int LB_ERR = -1;
    public const int EM_SETSEL = 0x00B1;
    public const int WM_APP = 0x8000;
    public const int WM_APP_BEGIN_OPEN = WM_APP + 1;
    public const int WM_APP_OPEN_COMPLETE = WM_APP + 2;
    public const int WM_APP_VIEWPORT_COMPLETE = WM_APP + 3;
    public const int WM_APP_SEARCH_COMPLETE = WM_APP + 4;
    public const int WM_APP_FILE_CHANGED = WM_APP + 5;
    public const int WM_APP_PASTE_COMPLETE = WM_APP + 6;
    public const int SB_LINEUP = 0;
    public const int SB_LINEDOWN = 1;
    public const int SB_PAGEUP = 2;
    public const int SB_PAGEDOWN = 3;
    public const int SB_THUMBPOSITION = 4;
    public const int SB_THUMBTRACK = 5;
    public const int SB_TOP = 6;
    public const int SB_BOTTOM = 7;
    public const int SB_LINELEFT = 0;
    public const int SB_LINERIGHT = 1;
    public const int SB_PAGELEFT = 2;
    public const int SB_PAGERIGHT = 3;
    public const int SB_LEFT = 6;
    public const int SB_RIGHT = 7;
    public const int SB_HORZ = 0;
    public const int SB_VERT = 1;
    public const int SIF_RANGE = 0x0001;
    public const int SIF_PAGE = 0x0002;
    public const int SIF_POS = 0x0004;
    public const int SIF_TRACKPOS = 0x0010;
    public const int TRANSPARENT = 1;
    public const int COLOR_WINDOW = 5;
    public const int COLOR_WINDOWTEXT = 8;
    public const int COLOR_HIGHLIGHT = 13;
    public const int COLOR_HIGHLIGHTTEXT = 14;
    public const int COLOR_3DFACE = 15;
    public const int COLOR_BTNSHADOW = 16;
    public const int COLOR_3DLIGHT = 22;
    public const int DEFAULT_CHARSET = 1;
    public const int OUT_DEFAULT_PRECIS = 0;
    public const int OUT_OUTLINE_PRECIS = 8;
    public const int CLIP_DEFAULT_PRECIS = 0;
    public const int CLEARTYPE_QUALITY = 5;
    public const int FF_MODERN = 0x30;
    public const int FW_NORMAL = 400;
    public const int FW_BOLD = 700;
    public const int SYSTEM_FIXED_FONT = 16;
    public const int DT_LEFT = 0x0000;
    public const int DT_VCENTER = 0x0004;
    public const int DT_SINGLELINE = 0x0020;
    public const int DT_END_ELLIPSIS = 0x8000;
    public const int DT_NOPREFIX = 0x0800;
    public const int WHEEL_DELTA = 120;
    public const int MB_OK = 0x00000000;
    public const int MB_YESNO = 0x00000004;
    public const int MB_ICONERROR = 0x00000010;
    public const int MB_ICONQUESTION = 0x00000020;
    public const int IDYES = 6;
    public const int EN_CHANGE = 0x0300;
    public const int LBN_SELCHANGE = 1;
    public const int LBN_DBLCLK = 2;
    public const int BN_CLICKED = 0;
    public const int BST_UNCHECKED = 0;
    public const int BST_CHECKED = 1;
    public const int ODS_SELECTED = 0x0001;
    public const int ODS_FOCUS = 0x0010;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;
    public const int VK_PRIOR = 0x21;
    public const int VK_NEXT = 0x22;
    public const int VK_HOME = 0x24;
    public const int VK_END = 0x23;
    public const int VK_A = 0x41;
    public const int VK_C = 0x43;
    public const int VK_O = 0x4F;
    public const int VK_V = 0x56;
    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;
    public const int CF_UNICODETEXT = 13;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int IMAGE_ICON = 1;
    public const int LR_DEFAULTCOLOR = 0x00000000;
    public const int LR_SHARED = 0x00008000;
    public const int CC_RGBINIT = 0x00000001;
    public const int CC_FULLOPEN = 0x00000002;
    public const int OFN_FILEMUSTEXIST = 0x00001000;
    public const int OFN_PATHMUSTEXIST = 0x00000800;
    public const int OFN_EXPLORER = 0x00080000;
    public const int OFN_NOCHANGEDIR = 0x00000008;
    public const uint GMEM_MOVEABLE = 0x0002;
    public static readonly IntPtr IDC_ARROW = new(32512);
    public static readonly IntPtr IDC_SIZENS = new(32645);
    public static readonly IntPtr IDC_SIZEWE = new(32644);
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public const int DEFAULT_GUI_FONT = 17;
    public static readonly IntPtr IDI_APPLICATION = new(32512);

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
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CHOOSECOLORW
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public int Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWITEMSTRUCT
    {
        public uint CtlType;
        public uint CtlID;
        public uint itemID;
        public uint itemAction;
        public uint itemState;
        public IntPtr hwndItem;
        public IntPtr hDC;
        public RECT rcItem;
        public nuint itemData;
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

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ChooseColorW(ref CHOOSECOLORW lpcc);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetOpenFileNameW(ref OPENFILENAMEW lpofn);

    [DllImport("comdlg32.dll")]
    public static extern int CommDlgExtendedError();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsDialogMessageW(IntPtr hDlg, ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW([In] ref MSG lpmsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadImageW(IntPtr hinst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetSysColorBrush(int nIndex);

    [DllImport("user32.dll")]
    public static extern int GetSysColor(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, [In] ref RECT lpRect, bool bErase);

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

    [DllImport("user32.dll")]
    public static extern int FrameRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DrawFocusRect(IntPtr hDC, ref RECT lprc);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    public static extern int SetTextColor(IntPtr hdc, int color);

    [DllImport("gdi32.dll")]
    public static extern int SaveDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RestoreDC(IntPtr hdc, int nSavedDC);

    [DllImport("gdi32.dll")]
    public static extern int IntersectClipRect(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

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
    public static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int i);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetScrollInfo(IntPtr hwnd, int nBar, ref SCROLLINFO lpsi, bool redraw);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetScrollInfo(IntPtr hwnd, int nBar, ref SCROLLINFO lpsi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, int uType);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nuint SetTimer(IntPtr hWnd, nuint nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(IntPtr hWnd, nuint uIDEvent);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("user32.dll")]
    public static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool GetProcessMemoryInfo(IntPtr process, out PROCESS_MEMORY_COUNTERS_EX counters, int cb);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    public static int LowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    public static int HighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    public static int RGB(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
