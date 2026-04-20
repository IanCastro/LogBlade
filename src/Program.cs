using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: viewer.exe <path>");
            return 1;
        }

        string path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("File not found: " + path);
            return 2;
        }

        try
        {
            using LogFile logFile = LogFile.Open(path);
            var viewer = new ViewerWindow(logFile);
            viewer.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 3;
        }
    }
}

internal sealed class ViewerWindow
{
    private readonly LogFile _logFile;
    private readonly LineCache _cache = new(300);
    private IntPtr _hwnd;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private int _lineHeight = 16;
    private int _statusHeight = 24;
    private int _clientWidth;
    private int _clientHeight;
    private long _topLine = 1;
    private long _visibleLineCount = 1;
    private bool _needsLayout = true;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;

    public ViewerWindow(LogFile logFile)
    {
        _logFile = logFile;
    }

    public void Run()
    {
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        string className = "LogViewerNativeAotWindow";
        NativeMethods.WNDCLASSEXW wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = s_wndProc,
            hInstance = hInstance,
            hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_ARROW),
            hbrBackground = NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW),
            lpszClassName = className
        };

        ushort atom = NativeMethods.RegisterClassExW(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("RegisterClassExW failed.");
        }

        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            className,
            "Log Viewer - " + Path.GetFileName(_logFile.FilePath),
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_VSCROLL,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            1100,
            800,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            GCHandle.ToIntPtr(_selfHandle));

        if (_hwnd == IntPtr.Zero)
        {
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            throw new InvalidOperationException("CreateWindowExW failed.");
        }

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWDEFAULT);
        NativeMethods.UpdateWindow(_hwnd);

        NativeMethods.MSG msg;
        while (NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }
    }

    private static ViewerWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr ptr = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return ptr == IntPtr.Zero ? null : (ViewerWindow?)GCHandle.FromIntPtr(ptr).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            GCHandle handle = GCHandle.FromIntPtr(create.lpCreateParams);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, GCHandle.ToIntPtr(handle));
            return new IntPtr(1);
        }

        ViewerWindow? self = FromHandle(hwnd);
        if (self is null)
        {
            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        switch (msg)
        {
            case NativeMethods.WM_CREATE:
                self.OnCreate(hwnd);
                return IntPtr.Zero;
            case NativeMethods.WM_SIZE:
                self.OnSize();
                return IntPtr.Zero;
            case NativeMethods.WM_VSCROLL:
                self.OnVScroll(wParam);
                return IntPtr.Zero;
            case NativeMethods.WM_MOUSEWHEEL:
                self.OnMouseWheel(wParam);
                return IntPtr.Zero;
            case NativeMethods.WM_KEYDOWN:
                self.OnKeyDown((int)wParam);
                return IntPtr.Zero;
            case NativeMethods.WM_ERASEBKGND:
                return new IntPtr(1);
            case NativeMethods.WM_PAINT:
                self.OnPaint();
                return IntPtr.Zero;
            case NativeMethods.WM_DESTROY:
                self.DisposeResources();
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private void OnCreate(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _font = NativeMethods.CreateFontW(
            -16,
            0,
            0,
            0,
            NativeMethods.FW_NORMAL,
            0,
            0,
            0,
            NativeMethods.DEFAULT_CHARSET,
            NativeMethods.OUT_DEFAULT_PRECIS,
            NativeMethods.CLIP_DEFAULT_PRECIS,
            NativeMethods.CLEARTYPE_QUALITY,
            NativeMethods.FF_MODERN,
            "Consolas");

        if (_font == IntPtr.Zero)
        {
            _font = NativeMethods.GetStockObject(NativeMethods.SYSTEM_FIXED_FONT);
        }

        MeasureFont();
        UpdateScrollBar();
    }

    private void MeasureFont()
    {
        IntPtr hdc = NativeMethods.GetDC(_hwnd);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        IntPtr oldFont = NativeMethods.SelectObject(hdc, _font);
        NativeMethods.TEXTMETRICW tm;
        if (NativeMethods.GetTextMetricsW(hdc, out tm))
        {
            _lineHeight = tm.tmHeight + tm.tmExternalLeading;
            _statusHeight = _lineHeight + 8;
        }

        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.ReleaseDC(_hwnd, hdc);
        _needsLayout = true;
    }

    private void OnSize()
    {
        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT rect);
        _clientWidth = rect.right - rect.left;
        _clientHeight = rect.bottom - rect.top;
        _needsLayout = true;
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private void OnVScroll(IntPtr wParam)
    {
        int command = NativeMethods.LowWord(wParam);
        int trackPos = NativeMethods.HighWord(wParam);

        if (command is NativeMethods.SB_THUMBPOSITION or NativeMethods.SB_THUMBTRACK)
        {
            NativeMethods.SCROLLINFO si = new()
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
                fMask = NativeMethods.SIF_TRACKPOS
            };

            if (NativeMethods.GetScrollInfo(_hwnd, NativeMethods.SB_VERT, ref si))
            {
                trackPos = si.nTrackPos;
            }
        }

        Scroll(command, trackPos);
    }

    private void OnMouseWheel(IntPtr wParam)
    {
        short delta = (short)NativeMethods.HighWord(wParam);
        int lines = 3;
        int steps = (delta / NativeMethods.WHEEL_DELTA) * lines;
        if (steps != 0)
        {
            Scroll(steps < 0 ? NativeMethods.SB_LINEDOWN : NativeMethods.SB_LINEUP, 0, Math.Abs(steps));
        }
    }

    private void OnKeyDown(int key)
    {
        switch (key)
        {
            case NativeMethods.VK_UP:
                Scroll(NativeMethods.SB_LINEUP, 0);
                break;
            case NativeMethods.VK_DOWN:
                Scroll(NativeMethods.SB_LINEDOWN, 0);
                break;
            case NativeMethods.VK_PRIOR:
                Scroll(NativeMethods.SB_PAGEUP, 0);
                break;
            case NativeMethods.VK_NEXT:
                Scroll(NativeMethods.SB_PAGEDOWN, 0);
                break;
            case NativeMethods.VK_HOME:
                Scroll(NativeMethods.SB_TOP, 0);
                break;
            case NativeMethods.VK_END:
                Scroll(NativeMethods.SB_BOTTOM, 0);
                break;
        }
    }

    private void Scroll(int command, int trackPos, int? lineMultiplierOverride = null)
    {
        long oldTop = _topLine;
        long step = lineMultiplierOverride ?? 1;
        long page = Math.Max(1, _visibleLineCount);
        long maxTop = Math.Max(1, _logFile.LineCount > 0 ? _logFile.LineCount - page + 1 : 1);

        switch (command)
        {
            case NativeMethods.SB_LINEUP:
                _topLine -= step;
                break;
            case NativeMethods.SB_LINEDOWN:
                _topLine += step;
                break;
            case NativeMethods.SB_PAGEUP:
                _topLine -= page;
                break;
            case NativeMethods.SB_PAGEDOWN:
                _topLine += page;
                break;
            case NativeMethods.SB_TOP:
                _topLine = 1;
                break;
            case NativeMethods.SB_BOTTOM:
                _topLine = maxTop;
                break;
            case NativeMethods.SB_THUMBPOSITION:
            case NativeMethods.SB_THUMBTRACK:
                _topLine = Math.Max(1, Math.Min(trackPos, (int)maxTop));
                break;
        }

        if (_topLine < 1)
        {
            _topLine = 1;
        }

        if (_topLine > maxTop)
        {
            _topLine = maxTop;
        }

        if (_topLine != oldTop)
        {
            UpdateScrollBar();
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
        }
    }

    private void UpdateScrollBar()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        long contentHeight = Math.Max(0, _clientHeight - _statusHeight);
        _visibleLineCount = Math.Max(1, contentHeight / Math.Max(1, _lineHeight));
        long maxTop = Math.Max(1, _logFile.LineCount > 0 ? _logFile.LineCount - _visibleLineCount + 1 : 1);
        if (_topLine > maxTop)
        {
            _topLine = maxTop;
        }

        NativeMethods.SCROLLINFO si = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 1,
            nMax = (int)Math.Max(1, Math.Min(int.MaxValue, _logFile.LineCount > 0 ? _logFile.LineCount : 1)),
            nPage = (uint)Math.Max(1, Math.Min(int.MaxValue, _visibleLineCount)),
            nPos = (int)Math.Min(int.MaxValue, _topLine)
        };

        NativeMethods.SetScrollInfo(_hwnd, NativeMethods.SB_VERT, ref si, true);
    }

    private void OnPaint()
    {
        NativeMethods.PAINTSTRUCT ps;
        IntPtr hdc = NativeMethods.BeginPaint(_hwnd, out ps);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        IntPtr oldFont = NativeMethods.SelectObject(hdc, _font);
        NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
        NativeMethods.SetTextColor(hdc, NativeMethods.RGB(0, 0, 0));

        NativeMethods.RECT client;
        NativeMethods.GetClientRect(_hwnd, out client);
        int width = client.right - client.left;
        int height = client.bottom - client.top;
        int contentHeight = Math.Max(0, height - _statusHeight);
        int linesThatFit = Math.Max(0, contentHeight / Math.Max(1, _lineHeight));
        long start = _topLine;
        long end = _logFile.LineCount == 0 ? 0 : Math.Min(_logFile.LineCount, start + linesThatFit - 1);

        NativeMethods.FillRect(hdc, ref client, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW));

        int y = 4;
        if (_logFile.LineCount == 0)
        {
            string empty = "(empty file)";
            NativeMethods.TextOutW(hdc, 4, y, empty, empty.Length);
        }
        else
        {
            for (long line = start; line <= end; line++)
            {
                string text = _cache.GetOrAdd(line, () => _logFile.ReadLine(line));
                NativeMethods.TextOutW(hdc, 4, y, text, text.Length);
                y += _lineHeight;
            }
        }

        NativeMethods.RECT statusRect = new()
        {
            left = 0,
            top = Math.Max(0, height - _statusHeight),
            right = width,
            bottom = height
        };
        NativeMethods.FillRect(hdc, ref statusRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DFACE));

        string status = _logFile.GetStatusText(_cache.Count, _topLine, _visibleLineCount);
        NativeMethods.DrawTextW(hdc, status, -1, ref statusRect, NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE | NativeMethods.DT_VCENTER | NativeMethods.DT_END_ELLIPSIS | NativeMethods.DT_NOPREFIX);

        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.EndPaint(_hwnd, ref ps);
    }

    private void DisposeResources()
    {
        if (_font != IntPtr.Zero && _font != NativeMethods.GetStockObject(NativeMethods.SYSTEM_FIXED_FONT))
        {
            NativeMethods.DeleteObject(_font);
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _font = IntPtr.Zero;
    }
}

internal sealed class LineCache
{
    private readonly int _capacity;
    private readonly Dictionary<long, string> _values = new();
    private readonly LinkedList<long> _order = new();
    private readonly Dictionary<long, LinkedListNode<long>> _nodes = new();

    public LineCache(int capacity)
    {
        _capacity = capacity;
    }

    public int Count => _values.Count;

    public string GetOrAdd(long lineNumber, Func<string> factory)
    {
        if (_values.TryGetValue(lineNumber, out string? existing))
        {
            Promote(lineNumber);
            return existing;
        }

        string value = factory();
        _values[lineNumber] = value;
        LinkedListNode<long> node = _order.AddFirst(lineNumber);
        _nodes[lineNumber] = node;

        while (_values.Count > _capacity)
        {
            LinkedListNode<long>? last = _order.Last;
            if (last is null)
            {
                break;
            }

            _order.RemoveLast();
            _nodes.Remove(last.Value);
            _values.Remove(last.Value);
        }

        return value;
    }

    private void Promote(long lineNumber)
    {
        if (!_nodes.TryGetValue(lineNumber, out LinkedListNode<long>? node))
        {
            return;
        }

        _order.Remove(node);
        _order.AddFirst(node);
    }
}
