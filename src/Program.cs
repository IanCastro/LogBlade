using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

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

internal enum ViewportRequestKind
{
    LoadAtOffset,
    ScrollByLines,
    JumpHome,
    JumpEnd
}

internal readonly record struct ViewportRequest(long Id, ViewportRequestKind Kind, int DeltaLines, long RequestedOffset, int VisibleLines);

internal sealed class ViewerWindow
{
    private const int SegmentChars = 4096;
    private const int WheelLinesPerNotch = 3;
    private const int ScrollRange = 1_000_000;
    private readonly string _path;
    private readonly string _titleSuffix;
    private IntPtr _hwnd;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private int _lineHeight = 16;
    private int _charWidth = 8;
    private int _clientWidth;
    private int _clientHeight;
    private int _visibleColumnCount = 1;
    private int _xOffsetChars;
    private long _visibleLineCount = 1;
    private bool _firstRenderLogged;
    private bool _closing;
    private string _statusText = "Loading file...";
    private LogFile? _logFile;
    private long _nextViewportRequestId;
    private long _latestViewportRequestId;
    private bool _viewportWorkerRunning;
    private ViewportRequest? _pendingViewportRequest;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;

    public ViewerWindow(string path)
    {
        _path = path;
        _titleSuffix = Path.GetFileName(path);
    }

    public void Run()
    {
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        string className = "LogViewerNativeAotWindow";
        AppLog.Instance.Info("window.create.begin", "begin", new LogField("class", className));
        string windowTitle = ComposeWindowTitle();
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
            AppLog.Instance.Error(
                "window.create.failed",
                "failed",
                new LogField("class", className),
                new LogField("stage", "register_class_failed"),
                new LogField("reason", "RegisterClassExW failed"));
            throw new InvalidOperationException("RegisterClassExW failed.");
        }

        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            className,
            windowTitle,
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_VSCROLL | NativeMethods.WS_HSCROLL,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            1100,
            760,
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

            AppLog.Instance.Error(
                "window.create.failed",
                "failed",
                new LogField("class", className),
                new LogField("stage", "create_window_failed"),
                new LogField("reason", "CreateWindowExW failed"));
            throw new InvalidOperationException("CreateWindowExW failed.");
        }

        NativeMethods.SetWindowTextW(_hwnd, windowTitle);
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWDEFAULT);
        NativeMethods.UpdateWindow(_hwnd);
        NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_APP_BEGIN_OPEN, IntPtr.Zero, IntPtr.Zero);

        NativeMethods.MSG msg;
        int getMessageResult;
        while ((getMessageResult = NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0)) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (getMessageResult < 0)
        {
            throw new InvalidOperationException("GetMessageW failed.");
        }

        AppLog.Instance.Info("shutdown", "normal_exit");
    }

    private string ComposeWindowTitle() => "C# MVP - " + _titleSuffix;

    private static ViewerWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr ptr = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return ptr == IntPtr.Zero ? null : (ViewerWindow?)GCHandle.FromIntPtr(ptr).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
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
                case NativeMethods.WM_HSCROLL:
                    self.OnHScroll(wParam);
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
                case NativeMethods.WM_APP_BEGIN_OPEN:
                    self.BeginBackgroundOpen();
                    return IntPtr.Zero;
                case NativeMethods.WM_APP_OPEN_COMPLETE:
                    self.OnOpenComplete(lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_APP_VIEWPORT_COMPLETE:
                    self.OnViewportComplete(lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_DESTROY:
                    self._closing = true;
                    self.DisposeResources();
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;
                default:
                    return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            AppLog.Instance.Fatal(
                "runtime.fatal",
                "window_proc_exception",
                new LogField("messageId", msg.ToString()),
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name),
                new LogField("message", ex.Message));
            AppLog.Instance.Flush();
            Environment.FailFast("Unexpected runtime fatal.", ex);
            return IntPtr.Zero;
        }
    }

    private void OnCreate(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _font = NativeMethods.CreateFontW(
            -18,
            0,
            0,
            0,
            NativeMethods.FW_NORMAL,
            0,
            0,
            0,
            NativeMethods.DEFAULT_CHARSET,
            NativeMethods.OUT_OUTLINE_PRECIS,
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

    private void BeginBackgroundOpen()
    {
        if (_closing || _hwnd == IntPtr.Zero)
        {
            return;
        }

        ResetViewportAsyncState();

        if (!File.Exists(_path))
        {
            AppLog.Instance.Info("file.open.begin", "begin", new LogField("path", _path));
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("path", _path),
                new LogField("stage", "missing_file"),
                new LogField("reason", "File not found"));

            _statusText = "File not found.";
            UpdateScrollBar();
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
            NativeMethods.MessageBoxW(_hwnd, "File not found: " + _path, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            return;
        }

        _logFile?.Dispose();
        _logFile = null;
        _firstRenderLogged = false;
        _statusText = "Loading file...";
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);

        string workerPath = _path;
        IntPtr hwnd = _hwnd;
        int visibleLines = (int)Math.Max(1, _visibleLineCount);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = new OpenWorkerResult();
            try
            {
                result.LogFile = LogFile.Open(workerPath);
                if (!result.LogFile.EnsureViewport(visibleLines))
                {
                    throw new InvalidOperationException("Failed to load the initial viewport.");
                }
                result.Success = true;
                result.PreloadedVisibleLines = visibleLines;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            GCHandle handle = GCHandle.Alloc(result);
            if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_OPEN_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
            {
                if (result.LogFile is not null)
                {
                    result.LogFile.Dispose();
                }
                handle.Free();
            }
        });
    }

    private void ResetViewportAsyncState()
    {
        _nextViewportRequestId = 0;
        _latestViewportRequestId = 0;
        _viewportWorkerRunning = false;
        _pendingViewportRequest = null;
    }

    private void QueueViewportRequest(ViewportRequestKind kind, int deltaLines = 0, long requestedOffset = 0, int? visibleLines = null)
    {
        if (_closing || _hwnd == IntPtr.Zero || _logFile is null)
        {
            return;
        }

        int effectiveVisible = Math.Max(1, visibleLines ?? (int)Math.Max(1, _visibleLineCount));
        var request = new ViewportRequest(
            Id: ++_nextViewportRequestId,
            Kind: kind,
            DeltaLines: deltaLines,
            RequestedOffset: requestedOffset,
            VisibleLines: effectiveVisible);

        _latestViewportRequestId = request.Id;
        _pendingViewportRequest = request;
        if (!_viewportWorkerRunning)
        {
            DispatchViewportRequest();
        }
    }

    private void DispatchViewportRequest()
    {
        if (_closing || _logFile is null || _pendingViewportRequest is null || _viewportWorkerRunning)
        {
            return;
        }

        ViewportRequest request = _pendingViewportRequest.Value;
        _pendingViewportRequest = null;
        _viewportWorkerRunning = true;

        LogFile workerLogFile = _logFile.CloneForWorker();
        IntPtr hwnd = _hwnd;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = new ViewportWorkerResult { RequestId = request.Id };
            try
            {
                switch (request.Kind)
                {
                    case ViewportRequestKind.LoadAtOffset:
                        workerLogFile.ScrollToApproximateOffset(request.RequestedOffset, request.VisibleLines);
                        break;
                    case ViewportRequestKind.ScrollByLines:
                        workerLogFile.ScrollByLinesForWorker(request.DeltaLines, request.VisibleLines);
                        break;
                    case ViewportRequestKind.JumpHome:
                        workerLogFile.ScrollHome(request.VisibleLines);
                        break;
                    case ViewportRequestKind.JumpEnd:
                        workerLogFile.ScrollEnd(request.VisibleLines);
                        break;
                }

                result.Success = true;
                result.LogFile = workerLogFile;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            GCHandle handle = GCHandle.Alloc(result);
            if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_VIEWPORT_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
            {
                if (result.LogFile is not null)
                {
                    result.LogFile.Dispose();
                }
                handle.Free();
            }
        });
    }

    private void OnOpenComplete(IntPtr lParam)
    {
        GCHandle handle = GCHandle.FromIntPtr(lParam);
        var result = (OpenWorkerResult?)handle.Target;
        handle.Free();
        if (result is null)
        {
            return;
        }

        if (_closing)
        {
            result.LogFile?.Dispose();
            return;
        }

        if (result.Success && result.LogFile is not null)
        {
            _logFile?.Dispose();
            _logFile = result.LogFile;
            _statusText = string.Empty;
            _firstRenderLogged = false;
            UpdateScrollBar();
            if ((int)Math.Max(1, _visibleLineCount) != result.PreloadedVisibleLines)
            {
                QueueViewportRequest(
                    ViewportRequestKind.LoadAtOffset,
                    requestedOffset: _logFile.TopOffset,
                    visibleLines: (int)Math.Max(1, _visibleLineCount));
            }
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
            return;
        }

        result.LogFile?.Dispose();
        _logFile?.Dispose();
        _logFile = null;
        _statusText = "Failed to open file.";
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
        NativeMethods.MessageBoxW(_hwnd, "Failed to open file: " + (result.Message ?? "unknown error"), Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
    }

    private void OnViewportComplete(IntPtr lParam)
    {
        GCHandle handle = GCHandle.FromIntPtr(lParam);
        var result = (ViewportWorkerResult?)handle.Target;
        handle.Free();
        if (result is null)
        {
            return;
        }

        _viewportWorkerRunning = false;

        if (!_closing && result.RequestId == _latestViewportRequestId && result.Success && result.LogFile is not null)
        {
            _logFile?.Dispose();
            _logFile = result.LogFile;
            UpdateScrollBar();
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
        }
        else
        {
            result.LogFile?.Dispose();
            if (!_closing && result.RequestId == _latestViewportRequestId && !result.Success)
            {
                AppLog.Instance.Error("viewport.load.failed", "failed", new LogField("reason", result.Message ?? "unknown error"));
            }
        }

        if (!_closing && _pendingViewportRequest is not null)
        {
            DispatchViewportRequest();
        }
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
            _charWidth = Math.Max(1, tm.tmAveCharWidth);
        }

        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.ReleaseDC(_hwnd, hdc);
    }

    private void OnSize()
    {
        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT rect);
        _clientWidth = Math.Max(0, rect.right - rect.left);
        _clientHeight = rect.bottom - rect.top;
        long previousVisibleLineCount = _visibleLineCount;
        UpdateScrollBar();
        if (_logFile is not null &&
            _logFile.HasContent &&
            !_closing &&
            _firstRenderLogged &&
            _visibleLineCount != previousVisibleLineCount)
        {
            QueueViewportRequest(
                ViewportRequestKind.LoadAtOffset,
                requestedOffset: _logFile.TopOffset,
                visibleLines: (int)Math.Max(1, _visibleLineCount));
        }
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private void OnHScroll(IntPtr wParam)
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

            if (NativeMethods.GetScrollInfo(_hwnd, NativeMethods.SB_HORZ, ref si))
            {
                trackPos = si.nTrackPos;
            }
        }

        int maxOffset = Math.Max(0, SegmentChars - _visibleColumnCount);
        int nextOffset = _xOffsetChars;

        switch (command)
        {
            case NativeMethods.SB_LINELEFT:
                nextOffset--;
                break;
            case NativeMethods.SB_LINERIGHT:
                nextOffset++;
                break;
            case NativeMethods.SB_PAGELEFT:
                nextOffset -= _visibleColumnCount;
                break;
            case NativeMethods.SB_PAGERIGHT:
                nextOffset += _visibleColumnCount;
                break;
            case NativeMethods.SB_LEFT:
                nextOffset = 0;
                break;
            case NativeMethods.SB_RIGHT:
                nextOffset = maxOffset;
                break;
            case NativeMethods.SB_THUMBPOSITION:
            case NativeMethods.SB_THUMBTRACK:
                nextOffset = trackPos;
                break;
            default:
                return;
        }

        SetHorizontalOffset(nextOffset);
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
        int steps = (delta / NativeMethods.WHEEL_DELTA) * WheelLinesPerNotch;
        if (steps != 0)
        {
            ScrollByLines(-steps);
        }
    }

    private void OnKeyDown(int key)
    {
        switch (key)
        {
            case NativeMethods.VK_UP:
                ScrollByLines(-1);
                break;
            case NativeMethods.VK_DOWN:
                ScrollByLines(1);
                break;
            case NativeMethods.VK_PRIOR:
                ScrollByLines(-(int)Math.Max(1, _visibleLineCount));
                break;
            case NativeMethods.VK_NEXT:
                ScrollByLines((int)Math.Max(1, _visibleLineCount));
                break;
            case NativeMethods.VK_HOME:
                SetHorizontalOffset(0);
                Scroll(NativeMethods.SB_TOP, 0);
                break;
            case NativeMethods.VK_END:
                Scroll(NativeMethods.SB_BOTTOM, 0);
                break;
        }
    }

    private void ScrollByLines(int deltaLines)
    {
        if (_logFile is null)
        {
            return;
        }
        QueueViewportRequest(ViewportRequestKind.ScrollByLines, deltaLines, visibleLines: (int)Math.Max(1, _visibleLineCount));
    }

    private void Scroll(int command, int trackPos)
    {
        if (_logFile is null)
        {
            return;
        }

        int visible = (int)Math.Max(1, _visibleLineCount);

        switch (command)
        {
            case NativeMethods.SB_LINEUP:
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, -1, visibleLines: visible);
                break;
            case NativeMethods.SB_LINEDOWN:
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, 1, visibleLines: visible);
                break;
            case NativeMethods.SB_PAGEUP:
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, -visible, visibleLines: visible);
                break;
            case NativeMethods.SB_PAGEDOWN:
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, visible, visibleLines: visible);
                break;
            case NativeMethods.SB_TOP:
                QueueViewportRequest(ViewportRequestKind.JumpHome, visibleLines: visible);
                break;
            case NativeMethods.SB_BOTTOM:
                QueueViewportRequest(ViewportRequestKind.JumpEnd, visibleLines: visible);
                break;
            case NativeMethods.SB_THUMBPOSITION:
            case NativeMethods.SB_THUMBTRACK:
            {
                long contentBytes = Math.Max(1, _logFile.FileSize - _logFile.DataOffset);
                long rel = (Math.Max(0, trackPos) * contentBytes) / ScrollRange;
                QueueViewportRequest(ViewportRequestKind.LoadAtOffset, requestedOffset: _logFile.DataOffset + rel, visibleLines: visible);
                break;
            }
        }
    }

    private void UpdateScrollBar()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _visibleLineCount = Math.Max(1, _clientHeight / Math.Max(1, _lineHeight));
        _visibleColumnCount = Math.Max(1, _clientWidth / Math.Max(1, _charWidth));
        int horizontalMax = Math.Max(0, SegmentChars - _visibleColumnCount);
        _xOffsetChars = Math.Clamp(_xOffsetChars, 0, horizontalMax);

        NativeMethods.SCROLLINFO horizontal = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 0,
            nMax = SegmentChars,
            nPage = (uint)Math.Max(1, Math.Min(SegmentChars, _visibleColumnCount)),
            nPos = _xOffsetChars
        };
        NativeMethods.SetScrollInfo(_hwnd, NativeMethods.SB_HORZ, ref horizontal, true);

        if (_logFile is null || !_logFile.HasContent)
        {
            NativeMethods.SCROLLINFO empty = new()
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
                fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
                nMin = 0,
                nMax = 1,
                nPage = 1,
                nPos = 0
            };
            NativeMethods.SetScrollInfo(_hwnd, NativeMethods.SB_VERT, ref empty, true);
            return;
        }

        long contentBytes = _logFile.FileSize - _logFile.DataOffset;
        long topBytes = _logFile.TopOffset - _logFile.DataOffset;
        long pageBytes = Math.Max(1, _logFile.ViewportBytes);

        NativeMethods.SCROLLINFO si = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 0,
            nMax = ScrollRange,
            nPage = (uint)Math.Max(1, Math.Min(ScrollRange, (pageBytes * ScrollRange) / Math.Max(1, contentBytes))),
            nPos = (int)Math.Min(ScrollRange, (topBytes * ScrollRange) / Math.Max(1, contentBytes))
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

        NativeMethods.FillRect(hdc, ref client, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW));

        int y = 0;
        int visibleNonEmptyLines = 0;
        if (_logFile is null)
        {
            NativeMethods.TextOutW(hdc, 0, y, _statusText, _statusText.Length);
        }
        else if (!_logFile.HasContent)
        {
            string empty = "(empty file)";
            NativeMethods.TextOutW(hdc, 0, y, empty, empty.Length);
        }
        else
        {
            foreach (LogFile.ViewportRow line in _logFile.ViewportRows)
            {
                string visibleText = SliceVisibleText(line.Text);
                if (visibleText.Length > 0)
                {
                    NativeMethods.TextOutW(hdc, 0, y, visibleText, visibleText.Length);
                }

                if (!string.IsNullOrEmpty(visibleText))
                {
                    visibleNonEmptyLines++;
                }

                y += _lineHeight;
            }
        }

        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.EndPaint(_hwnd, ref ps);
        bool useful = _logFile is not null && (!_logFile.HasContent || visibleNonEmptyLines > 0);
        if (useful)
        {
            NativeMethods.DwmFlush();
        }
        TryLogFirstRenderComplete(useful);
    }

    private void TryLogFirstRenderComplete(bool useful)
    {
        if (_firstRenderLogged || !useful || _logFile is null)
        {
            return;
        }

        _firstRenderLogged = true;
        AppLog.Instance.Info(
            "window.first_render.complete",
            "complete",
            new LogField("path", _logFile.FilePath),
            new LogField("encoding", _logFile.EncodingName),
            new LogField("dataOffset", _logFile.DataOffset.ToString()),
            new LogField("fileSize", _logFile.FileSize.ToString()),
            new LogField("topOffset", _logFile.TopOffset.ToString()),
            new LogField("visibleLines", Math.Max(1, _visibleLineCount).ToString()),
            new LogField("viewportBytes", _logFile.ViewportBytes.ToString()));
    }

    private void SetHorizontalOffset(int requestedOffset)
    {
        int maxOffset = Math.Max(0, SegmentChars - _visibleColumnCount);
        int nextOffset = Math.Clamp(requestedOffset, 0, maxOffset);
        if (nextOffset == _xOffsetChars)
        {
            return;
        }

        _xOffsetChars = nextOffset;
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private string SliceVisibleText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int start = Math.Clamp(_xOffsetChars, 0, text.Length);
        int available = text.Length - start;
        if (available <= 0)
        {
            return string.Empty;
        }

        int count = Math.Min(_visibleColumnCount, available);
        return text.Substring(start, count);
    }

    private void DisposeResources()
    {
        _logFile?.Dispose();
        _logFile = null;

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

internal sealed class OpenWorkerResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public LogFile? LogFile { get; set; }
    public int PreloadedVisibleLines { get; set; }
}

internal sealed class ViewportWorkerResult
{
    public long RequestId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public LogFile? LogFile { get; set; }
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

    public void Clear()
    {
        _values.Clear();
        _order.Clear();
        _nodes.Clear();
    }

    public bool TryGet(long lineNumber, out string text)
    {
        if (_values.TryGetValue(lineNumber, out string? existing))
        {
            Promote(lineNumber);
            text = existing;
            return true;
        }

        text = string.Empty;
        return false;
    }

    public void Set(long lineNumber, string value)
    {
        if (_values.ContainsKey(lineNumber))
        {
            _values[lineNumber] = value;
            Promote(lineNumber);
            return;
        }

        _values[lineNumber] = value;
        LinkedListNode<long> node = _order.AddFirst(lineNumber);
        _nodes[lineNumber] = node;
        TrimToCapacity();
    }

    public string GetOrAdd(long lineNumber, Func<string> factory)
    {
        if (TryGet(lineNumber, out string existing))
        {
            return existing;
        }

        string value = factory();
        Set(lineNumber, value);
        return value;
    }

    private void TrimToCapacity()
    {
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
