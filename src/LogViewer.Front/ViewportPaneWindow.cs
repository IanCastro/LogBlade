using System;
using System.Runtime.InteropServices;
using System.Threading;

internal enum ViewportRequestKind
{
    LoadAtPercentage,
    ScrollByLines,
    JumpHome,
    JumpEnd
}

internal readonly record struct ViewportRequest(long Id, ViewportRequestKind Kind, int DeltaLines, double RequestedPercentage, int VisibleLines);

internal sealed class ViewportWorkerResult
{
    public long RequestId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public IViewportReader? Reader { get; set; }
}

internal sealed class ViewportPaneWindow : IDisposable
{
    private const int WheelLinesPerNotch = 3;
    private const int ScrollRange = 1_000_000;
    private const int VerticalScrollVirtualRows = 4000;
    private const string WindowClassName = "LogViewerViewportPaneWindow";

    private static readonly object s_registrationSync = new();
    private static bool s_registered;
    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;

    private readonly IntPtr _font;
    private readonly int _lineHeight;
    private readonly int _charWidth;
    private readonly Action<ViewportPaneWindow>? _onUsefulPaint;

    private IntPtr _hwnd;
    private GCHandle _selfHandle;
    private int _visibleColumnCount = 1;
    private int _visibleLineCount = 1;
    private int _xOffsetChars;
    private bool _isVisible = true;
    private string _statusText = string.Empty;
    private string _emptyContentText = "(empty file)";
    private IViewportReader? _reader;
    private long _nextViewportRequestId;
    private long _latestViewportRequestId;
    private bool _viewportWorkerRunning;
    private ViewportRequest? _pendingViewportRequest;

    public ViewportPaneWindow(IntPtr font, int lineHeight, int charWidth, Action<ViewportPaneWindow>? onUsefulPaint = null)
    {
        _font = font;
        _lineHeight = Math.Max(1, lineHeight);
        _charWidth = Math.Max(1, charWidth);
        _onUsefulPaint = onUsefulPaint;
    }

    public IntPtr Hwnd => _hwnd;
    public IViewportReader? Reader => _reader;
    public int VisibleLineCount => Math.Max(1, _visibleLineCount);
    public bool IsVisible => _isVisible;

    public void Create(IntPtr parentHwnd, IntPtr hInstance)
    {
        EnsureWindowClassRegistered(hInstance);
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            WindowClassName,
            string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER | NativeMethods.WS_VSCROLL | NativeMethods.WS_HSCROLL,
            0,
            0,
            1,
            1,
            parentHwnd,
            IntPtr.Zero,
            hInstance,
            GCHandle.ToIntPtr(_selfHandle));

        if (_hwnd == IntPtr.Zero)
        {
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            throw new InvalidOperationException("CreateWindowExW failed for viewport pane.");
        }
    }

    public void SetBounds(NativeMethods.RECT rect, bool visible)
    {
        _isVisible = visible && GetRectWidth(rect) > 0 && GetRectHeight(rect) > 0;
        NativeMethods.MoveWindow(_hwnd, rect.left, rect.top, GetRectWidth(rect), GetRectHeight(rect), true);
        NativeMethods.ShowWindow(_hwnd, _isVisible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        if (_isVisible)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
        }
    }

    public void SetEmptyContentText(string text)
    {
        _emptyContentText = text;
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    public void SetStatus(string statusText, bool disposeReader = true)
    {
        if (disposeReader)
        {
            _reader?.Dispose();
            _reader = null;
        }

        _statusText = statusText;
        ResetViewportAsyncState();
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    public void SetReader(IViewportReader reader, int preloadedVisibleLines)
    {
        _reader?.Dispose();
        _reader = reader;
        _statusText = string.Empty;
        ResetViewportAsyncState();
        UpdateScrollBar();
        if (VisibleLineCount != Math.Max(1, preloadedVisibleLines))
        {
            QueueViewportRequest(
                ViewportRequestKind.LoadAtPercentage,
                requestedPercentage: _reader.ScrollPercentage,
                visibleLines: VisibleLineCount);
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    public void Focus()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetFocus(_hwnd);
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _reader = null;
    }

    private static void EnsureWindowClassRegistered(IntPtr hInstance)
    {
        lock (s_registrationSync)
        {
            if (s_registered)
            {
                return;
            }

            NativeMethods.WNDCLASSEXW wc = new()
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
                lpfnWndProc = s_wndProc,
                hInstance = hInstance,
                hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_ARROW),
                hbrBackground = NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW),
                lpszClassName = WindowClassName
            };

            ushort atom = NativeMethods.RegisterClassExW(ref wc);
            if (atom == 0)
            {
                throw new InvalidOperationException("RegisterClassExW failed for viewport pane.");
            }

            s_registered = true;
        }
    }

    private void ResetViewportAsyncState()
    {
        _nextViewportRequestId = 0;
        _latestViewportRequestId = 0;
        _viewportWorkerRunning = false;
        _pendingViewportRequest = null;
    }

    private void QueueViewportRequest(ViewportRequestKind kind, int deltaLines = 0, double requestedPercentage = 0d, int? visibleLines = null)
    {
        if (_reader is null || _hwnd == IntPtr.Zero)
        {
            return;
        }

        int effectiveVisible = Math.Max(1, visibleLines ?? VisibleLineCount);
        var request = new ViewportRequest(
            Id: ++_nextViewportRequestId,
            Kind: kind,
            DeltaLines: deltaLines,
            RequestedPercentage: requestedPercentage,
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
        if (_reader is null || _pendingViewportRequest is null || _viewportWorkerRunning)
        {
            return;
        }

        ViewportRequest request = _pendingViewportRequest.Value;
        _pendingViewportRequest = null;
        _viewportWorkerRunning = true;

        IViewportReader workerReader = _reader.CloneForWorker();
        IntPtr hwnd = _hwnd;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = new ViewportWorkerResult { RequestId = request.Id };
            try
            {
                switch (request.Kind)
                {
                    case ViewportRequestKind.LoadAtPercentage:
                        workerReader.ReadFromPercentage(request.RequestedPercentage, request.VisibleLines);
                        break;
                    case ViewportRequestKind.ScrollByLines:
                        if (request.DeltaLines >= 0)
                        {
                            workerReader.ReadNext(request.DeltaLines);
                        }
                        else
                        {
                            workerReader.ReadPrevious(-request.DeltaLines);
                        }
                        break;
                    case ViewportRequestKind.JumpHome:
                        workerReader.ReadFromPercentage(0d, request.VisibleLines);
                        break;
                    case ViewportRequestKind.JumpEnd:
                        workerReader.ReadFromPercentage(100d, request.VisibleLines);
                        break;
                }

                result.Success = true;
                result.Reader = workerReader;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                workerReader.Dispose();
            }

            GCHandle handle = GCHandle.Alloc(result);
            if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_VIEWPORT_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
            {
                result.Reader?.Dispose();
                handle.Free();
            }
        });
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

        if (result.RequestId == _latestViewportRequestId && result.Success && result.Reader is not null)
        {
            _reader?.Dispose();
            _reader = result.Reader;
            UpdateScrollBar();
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
        }
        else
        {
            result.Reader?.Dispose();
        }

        if (_pendingViewportRequest is not null)
        {
            DispatchViewportRequest();
        }
    }

    private void OnSize()
    {
        int previousVisibleLineCount = _visibleLineCount;
        UpdateScrollBar();
        if (_reader is not null &&
            _reader.HasContent &&
            _visibleLineCount != previousVisibleLineCount)
        {
            QueueViewportRequest(
                ViewportRequestKind.LoadAtPercentage,
                requestedPercentage: _reader.ScrollPercentage,
                visibleLines: VisibleLineCount);
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

        int maxOffset = Math.Max(0, VisualRowReader.VisibleSegmentChars - _visibleColumnCount);
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
                ScrollByLines(-VisibleLineCount);
                break;
            case NativeMethods.VK_NEXT:
                ScrollByLines(VisibleLineCount);
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
        NativeMethods.RECT clientRect;
        NativeMethods.GetClientRect(_hwnd, out clientRect);
        NativeMethods.FillRect(hdc, ref clientRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW));

        int visibleNonEmptyLines = PaintContent(hdc);
        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.EndPaint(_hwnd, ref ps);

        bool useful = _reader is not null && (!_reader.HasContent || visibleNonEmptyLines > 0);
        if (useful)
        {
            NativeMethods.DwmFlush();
            _onUsefulPaint?.Invoke(this);
        }
    }

    private int PaintContent(IntPtr hdc)
    {
        int x = 0;
        int y = 0;
        int visibleNonEmptyLines = 0;

        if (_reader is null)
        {
            if (!string.IsNullOrEmpty(_statusText))
            {
                NativeMethods.TextOutW(hdc, x, y, _statusText, _statusText.Length);
            }

            return visibleNonEmptyLines;
        }

        if (!_reader.HasContent)
        {
            string emptyText = _emptyContentText;
            NativeMethods.TextOutW(hdc, x, y, emptyText, emptyText.Length);
            return visibleNonEmptyLines;
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        foreach (string row in _reader.CurrentRows)
        {
            string visibleText = SliceVisibleText(row);
            if (visibleText.Length > 0)
            {
                NativeMethods.TextOutW(hdc, x, y, visibleText, visibleText.Length);
                visibleNonEmptyLines++;
            }

            y += _lineHeight;
            if (y >= clientRect.bottom)
            {
                break;
            }
        }

        return visibleNonEmptyLines;
    }

    private void ScrollByLines(int deltaLines)
    {
        if (_reader is null)
        {
            return;
        }

        QueueViewportRequest(ViewportRequestKind.ScrollByLines, deltaLines, visibleLines: VisibleLineCount);
    }

    private void Scroll(int command, int trackPos)
    {
        if (_reader is null)
        {
            return;
        }

        int visible = VisibleLineCount;
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
                double percentage = (Math.Clamp(trackPos, 0, ScrollRange) * 100d) / ScrollRange;
                QueueViewportRequest(ViewportRequestKind.LoadAtPercentage, requestedPercentage: percentage, visibleLines: visible);
                break;
        }
    }

    private void SetHorizontalOffset(int requestedOffset)
    {
        int maxOffset = Math.Max(0, VisualRowReader.VisibleSegmentChars - _visibleColumnCount);
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

    private void UpdateScrollBar()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        _visibleLineCount = Math.Max(1, GetRectHeight(clientRect) / _lineHeight);
        _visibleColumnCount = Math.Max(1, GetRectWidth(clientRect) / _charWidth);

        int horizontalMax = Math.Max(0, VisualRowReader.VisibleSegmentChars - _visibleColumnCount);
        _xOffsetChars = Math.Clamp(_xOffsetChars, 0, horizontalMax);
        NativeMethods.SCROLLINFO horizontal = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 0,
            nMax = VisualRowReader.VisibleSegmentChars,
            nPage = (uint)Math.Max(1, Math.Min(VisualRowReader.VisibleSegmentChars, _visibleColumnCount)),
            nPos = _xOffsetChars
        };
        NativeMethods.SetScrollInfo(_hwnd, NativeMethods.SB_HORZ, ref horizontal, true);

        if (_reader is null || !_reader.HasContent)
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

        long verticalPage = Math.Max(1, Math.Min(ScrollRange, ((long)_visibleLineCount * ScrollRange) / VerticalScrollVirtualRows));
        NativeMethods.SCROLLINFO vertical = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 0,
            nMax = ScrollRange,
            nPage = (uint)verticalPage,
            nPos = (int)Math.Min(ScrollRange, (_reader.ScrollPercentage / 100d) * ScrollRange)
        };
        NativeMethods.SetScrollInfo(_hwnd, NativeMethods.SB_VERT, ref vertical, true);
    }

    private static ViewportPaneWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr ptr = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return ptr == IntPtr.Zero ? null : (ViewportPaneWindow?)GCHandle.FromIntPtr(ptr).Target;
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

        ViewportPaneWindow? self = FromHandle(hwnd);
        if (self is null)
        {
            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        if (msg == NativeMethods.WM_NCDESTROY)
        {
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, IntPtr.Zero);
            if (self._selfHandle.IsAllocated)
            {
                self._selfHandle.Free();
            }

            self._hwnd = IntPtr.Zero;
            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        switch (msg)
        {
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
            case NativeMethods.WM_LBUTTONDOWN:
                self.Focus();
                return IntPtr.Zero;
            case NativeMethods.WM_ERASEBKGND:
                return new IntPtr(1);
            case NativeMethods.WM_PAINT:
                self.OnPaint();
                return IntPtr.Zero;
            case NativeMethods.WM_APP_VIEWPORT_COMPLETE:
                self.OnViewportComplete(lParam);
                return IntPtr.Zero;
            default:
                return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private static int GetRectWidth(NativeMethods.RECT rect) => Math.Max(0, rect.right - rect.left);

    private static int GetRectHeight(NativeMethods.RECT rect) => Math.Max(0, rect.bottom - rect.top);
}
