using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal sealed class OpenWorkerResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public DetectedEncodingInfo? DetectedEncoding { get; set; }
    public VisualRowReader? Reader { get; set; }
    public int PreloadedVisibleLines { get; set; }
}

internal sealed class SearchWorkerResult
{
    public long RequestId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public IViewportReader? Reader { get; set; }
    public int PreloadedVisibleLines { get; set; }
    public double ProgressPercentage { get; set; }
    public long MatchedLineCount { get; set; }
    public bool IsFinal { get; set; }
}

internal sealed class ViewerWindow
{
    private const int SearchBarHeight = 32;
    private const int SearchResultsMinHeight = 160;
    private const int SearchResultsMaxHeight = 420;
    private const int SearchDebounceMs = 200;
    private const nuint SearchDebounceTimerId = 1;
    private const int SearchInnerPadding = 4;
    private const int SearchProgressMinWidth = 180;
    private const int SearchProgressMaxWidth = 280;
    private const int SearchProgressGap = 8;

    private readonly string _path;
    private readonly string _titleSuffix;
    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _searchEdit;
    private GCHandle _selfHandle;
    private int _lineHeight = 16;
    private int _charWidth = 8;
    private bool _firstRenderLogged;
    private bool _closing;
    private string _searchQuery = string.Empty;
    private DetectedEncodingInfo? _detectedEncoding;
    private ViewportPaneWindow? _mainPane;
    private ViewportPaneWindow? _filteredPane;
    private WindowLayout _layout;
    private long _nextSearchRequestId;
    private long _latestSearchRequestId;
    private bool _searchInProgress;
    private bool _searchDisplayActive;
    private double _searchProgressPercentage;
    private long _searchMatchedLineCount;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;

    private readonly record struct WindowLayout(
        NativeMethods.RECT ClientRect,
        NativeMethods.RECT ViewerRect,
        NativeMethods.RECT SearchBarRect,
        NativeMethods.RECT SearchResultsRect,
        NativeMethods.RECT SearchEditRect,
        NativeMethods.RECT SearchProgressRect);

    public ViewerWindow(string path)
    {
        _path = path;
        _titleSuffix = Path.GetFileName(path);
    }

    public void Run()
    {
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        const string className = "LogViewerNativeAotWindow";
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
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
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
                case NativeMethods.WM_COMMAND:
                    self.OnCommand(wParam, lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_TIMER:
                    self.OnTimer((nuint)wParam);
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
                case NativeMethods.WM_APP_SEARCH_COMPLETE:
                    self.OnSearchUpdate(lParam);
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

        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        _mainPane = new ViewportPaneWindow(_font, _lineHeight, _charWidth, OnMainPaneUsefulPaint);
        _mainPane.Create(_hwnd, hInstance);
        _mainPane.SetStatus("Loading file...");

        _filteredPane = new ViewportPaneWindow(_font, _lineHeight, _charWidth);
        _filteredPane.Create(_hwnd, hInstance);
        _filteredPane.SetEmptyContentText("(no matches)");
        _filteredPane.SetStatus(string.Empty);

        _searchEdit = NativeMethods.CreateWindowExW(
            0,
            "EDIT",
            string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER | NativeMethods.ES_AUTOHSCROLL,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_searchEdit == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for search edit.");
        }

        NativeMethods.SendMessageW(_searchEdit, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        RecalculateLayout();
        ApplyLayout();
    }

    private void BeginBackgroundOpen()
    {
        if (_closing || _hwnd == IntPtr.Zero || _mainPane is null)
        {
            return;
        }

        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        _detectedEncoding = null;
        _firstRenderLogged = false;
        _searchInProgress = false;
        _searchDisplayActive = false;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _mainPane.SetStatus("Loading file...");
        _filteredPane?.SetStatus(string.Empty);
        InvalidateHost();

        if (!File.Exists(_path))
        {
            AppLog.Instance.Info("file.open.begin", "begin", new LogField("path", _path));
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("path", _path),
                new LogField("stage", "missing_file"),
                new LogField("reason", "File not found"));

            _mainPane.SetStatus("File not found.");
            NativeMethods.MessageBoxW(_hwnd, "File not found: " + _path, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            return;
        }

        string workerPath = _path;
        IntPtr hwnd = _hwnd;
        int visibleLines = _mainPane.VisibleLineCount;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = new OpenWorkerResult();
            try
            {
                AppLog.Instance.Info("file.open.begin", "begin", new LogField("path", workerPath));
                result.DetectedEncoding = LogEncodingDetector.DetectEncoding(workerPath);
                result.Reader = new VisualRowReader(workerPath, result.DetectedEncoding.Value.Encoding, result.DetectedEncoding.Value.DataOffset);
                result.Reader.ReadFromPercentage(0d, visibleLines);
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
                result.Reader?.Dispose();
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
            result.Reader?.Dispose();
            return;
        }

        if (result.Success && result.Reader is not null && _mainPane is not null)
        {
            _detectedEncoding = result.DetectedEncoding;
            _mainPane.SetReader(result.Reader, result.PreloadedVisibleLines);
            if (result.DetectedEncoding is DetectedEncodingInfo detected)
            {
                AppLog.Instance.Info(
                    "encoding.detected",
                    "detected",
                    new LogField("path", result.Reader.FilePath),
                    new LogField("encoding", result.Reader.EncodingName),
                    new LogField("dataOffset", detected.DataOffset.ToString()));
            }

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                ScheduleSearch();
            }
            else
            {
                _mainPane.Focus();
            }

            return;
        }

        result.Reader?.Dispose();
        _mainPane?.SetStatus("Failed to open file.");
        NativeMethods.MessageBoxW(_hwnd, "Failed to open file: " + (result.Message ?? "unknown error"), Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
    }

    private void OnSize()
    {
        RecalculateLayout();
        ApplyLayout();
        InvalidateHost();
    }

    private void OnCommand(IntPtr wParam, IntPtr lParam)
    {
        if (lParam != _searchEdit || NativeMethods.HighWord(wParam) != NativeMethods.EN_CHANGE)
        {
            return;
        }

        _searchQuery = ReadWindowText(_searchEdit);
        _latestSearchRequestId = ++_nextSearchRequestId;
        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);

        if (string.IsNullOrEmpty(_searchQuery))
        {
            _searchInProgress = false;
            _searchDisplayActive = false;
            _searchProgressPercentage = 0d;
            _searchMatchedLineCount = 0;
            _filteredPane?.SetStatus(string.Empty);
            RecalculateLayout();
            ApplyLayout();
            InvalidateHost();
            return;
        }

        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _filteredPane?.SetEmptyContentText("(no matches)");
        _filteredPane?.SetStatus("Searching...");
        RecalculateLayout();
        ApplyLayout();
        InvalidateHost();
        NativeMethods.SetTimer(_hwnd, SearchDebounceTimerId, SearchDebounceMs, IntPtr.Zero);
    }

    private void OnTimer(nuint timerId)
    {
        if (timerId != SearchDebounceTimerId)
        {
            return;
        }

        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        DispatchSearch(_latestSearchRequestId, _searchQuery);
    }

    private void DispatchSearch(long requestId, string query)
    {
        if (_closing || string.IsNullOrEmpty(query) || _detectedEncoding is not DetectedEncodingInfo detected || _filteredPane is null)
        {
            return;
        }

        int visibleLines = _filteredPane.VisibleLineCount;
        string workerPath = _path;
        IntPtr hwnd = _hwnd;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                LogSearchBuilder.BuildFilteredReaderIncremental(workerPath, detected.Encoding, detected.DataOffset, query, visibleLines, update =>
                {
                    PostSearchWorkerResult(new SearchWorkerResult
                    {
                        RequestId = requestId,
                        Success = true,
                        Reader = update.Reader,
                        PreloadedVisibleLines = visibleLines,
                        ProgressPercentage = update.ProgressPercentage,
                        MatchedLineCount = update.MatchedLineCount,
                        IsFinal = update.IsFinal
                    });
                });
            }
            catch (Exception ex)
            {
                PostSearchWorkerResult(new SearchWorkerResult
                {
                    RequestId = requestId,
                    Success = false,
                    Message = ex.Message,
                    ProgressPercentage = _searchProgressPercentage,
                    MatchedLineCount = _searchMatchedLineCount,
                    IsFinal = true
                });
            }

            void PostSearchWorkerResult(SearchWorkerResult result)
            {
                GCHandle handle = GCHandle.Alloc(result);
                if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_SEARCH_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
                {
                    result.Reader?.Dispose();
                    handle.Free();
                }
            }
        });
    }

    private void OnSearchUpdate(IntPtr lParam)
    {
        GCHandle handle = GCHandle.FromIntPtr(lParam);
        var result = (SearchWorkerResult?)handle.Target;
        handle.Free();
        if (result is null)
        {
            return;
        }

        if (_closing || result.RequestId != _latestSearchRequestId || string.IsNullOrEmpty(_searchQuery))
        {
            result.Reader?.Dispose();
            return;
        }

        _searchProgressPercentage = Math.Clamp(result.ProgressPercentage, 0d, 100d);
        _searchMatchedLineCount = Math.Max(0, result.MatchedLineCount);
        _searchInProgress = !result.IsFinal;
        _searchDisplayActive = true;

        if (_filteredPane is null)
        {
            result.Reader?.Dispose();
            return;
        }

        if (!result.Success)
        {
            _searchInProgress = false;
            _searchProgressPercentage = 0d;
            _searchMatchedLineCount = 0;
            _searchDisplayActive = false;
            result.Reader?.Dispose();
            _filteredPane.SetStatus("Search failed.");
            InvalidateHost();
            AppLog.Instance.Error("search.failed", "failed", new LogField("reason", result.Message ?? "unknown error"));
            return;
        }

        if (result.Reader is not null)
        {
            if (result.Reader is FilteredVisualRowReader nextFilteredReader)
            {
                long desiredTopRow = 0;
                if (_filteredPane.Reader is FilteredVisualRowReader currentFilteredReader)
                {
                    desiredTopRow = currentFilteredReader.TopRowOrdinal;
                }

                nextFilteredReader.ReadFromRowOrdinal(desiredTopRow, _filteredPane.VisibleLineCount);
            }

            _filteredPane.SetEmptyContentText("(no matches)");
            _filteredPane.SetReader(result.Reader, _filteredPane.VisibleLineCount);
        }

        if (result.IsFinal)
        {
            _searchInProgress = false;
            _searchProgressPercentage = 100d;
        }

        InvalidateSearchBar();
    }

    private void OnPaint()
    {
        NativeMethods.PAINTSTRUCT ps;
        IntPtr hdc = NativeMethods.BeginPaint(_hwnd, out ps);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        NativeMethods.FillRect(hdc, ref clientRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DFACE));
        PaintSearchProgress(hdc);
        NativeMethods.EndPaint(_hwnd, ref ps);
    }

    private void OnMainPaneUsefulPaint(ViewportPaneWindow pane)
    {
        if (_firstRenderLogged || pane.Reader is null)
        {
            return;
        }

        _firstRenderLogged = true;
        AppLog.Instance.Info(
            "window.first_render.complete",
            "complete",
            new LogField("path", pane.Reader.FilePath),
            new LogField("encoding", pane.Reader.EncodingName),
            new LogField("dataOffset", pane.Reader.DataOffset.ToString()),
            new LogField("fileSize", pane.Reader.FileSize.ToString()),
            new LogField("topOffset", pane.Reader.TopOffset.ToString()),
            new LogField("visibleLines", pane.VisibleLineCount.ToString()),
            new LogField("viewportBytes", pane.Reader.ViewportBytes.ToString()));
    }

    private void MeasureFont()
    {
        IntPtr hdc = NativeMethods.GetDC(_hwnd);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        IntPtr oldFont = NativeMethods.SelectObject(hdc, _font);
        if (NativeMethods.GetTextMetricsW(hdc, out NativeMethods.TEXTMETRICW tm))
        {
            _lineHeight = tm.tmHeight + tm.tmExternalLeading;
            _charWidth = Math.Max(1, tm.tmAveCharWidth);
        }

        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.ReleaseDC(_hwnd, hdc);
    }

    private void RecalculateLayout()
    {
        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        int clientWidth = GetRectWidth(clientRect);
        int clientHeight = GetRectHeight(clientRect);
        int searchResultsHeight = 0;
        if (!string.IsNullOrEmpty(_searchQuery))
        {
            searchResultsHeight = Math.Clamp((int)Math.Round(clientHeight * 0.35d), SearchResultsMinHeight, SearchResultsMaxHeight);
            searchResultsHeight = Math.Min(searchResultsHeight, Math.Max(0, clientHeight - SearchBarHeight));
        }

        int viewerBottom = Math.Max(clientRect.top, clientRect.bottom - SearchBarHeight - searchResultsHeight);
        int searchBarTop = viewerBottom;
        int searchBarBottom = Math.Min(clientRect.bottom, searchBarTop + SearchBarHeight);
        NativeMethods.RECT viewerRect = new()
        {
            left = clientRect.left,
            top = clientRect.top,
            right = clientRect.right,
            bottom = viewerBottom
        };
        NativeMethods.RECT searchBarRect = new()
        {
            left = clientRect.left,
            top = searchBarTop,
            right = clientRect.right,
            bottom = searchBarBottom
        };
        NativeMethods.RECT searchResultsRect = searchResultsHeight > 0
            ? new NativeMethods.RECT
            {
                left = clientRect.left,
                top = searchBarBottom,
                right = clientRect.right,
                bottom = clientRect.bottom
            }
            : CreateZeroRect();

        NativeMethods.RECT searchBarInner = InsetRect(searchBarRect, SearchInnerPadding);
        int progressWidth = Math.Clamp(clientWidth / 6, SearchProgressMinWidth, SearchProgressMaxWidth);
        progressWidth = Math.Min(progressWidth, Math.Max(0, GetRectWidth(searchBarInner) - 80));
        NativeMethods.RECT progressRect = progressWidth > 0
            ? new NativeMethods.RECT
            {
                left = Math.Max(searchBarInner.left, searchBarInner.right - progressWidth),
                top = searchBarInner.top,
                right = searchBarInner.right,
                bottom = searchBarInner.bottom
            }
            : CreateZeroRect();
        NativeMethods.RECT editRect = progressWidth > 0
            ? new NativeMethods.RECT
            {
                left = searchBarInner.left,
                top = searchBarInner.top,
                right = Math.Max(searchBarInner.left, progressRect.left - SearchProgressGap),
                bottom = searchBarInner.bottom
            }
            : searchBarInner;

        _layout = new WindowLayout(clientRect, viewerRect, searchBarRect, searchResultsRect, editRect, progressRect);
    }

    private void ApplyLayout()
    {
        _mainPane?.SetBounds(_layout.ViewerRect, true);
        _filteredPane?.SetBounds(_layout.SearchResultsRect, !IsZeroRect(_layout.SearchResultsRect));

        NativeMethods.MoveWindow(
            _searchEdit,
            _layout.SearchEditRect.left,
            _layout.SearchEditRect.top,
            GetRectWidth(_layout.SearchEditRect),
            GetRectHeight(_layout.SearchEditRect),
            true);
        NativeMethods.ShowWindow(_searchEdit, NativeMethods.SW_SHOW);
    }

    private void ScheduleSearch()
    {
        if (string.IsNullOrEmpty(_searchQuery))
        {
            return;
        }

        _filteredPane?.SetStatus("Searching...");
        RecalculateLayout();
        ApplyLayout();
        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        InvalidateHost();
        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        NativeMethods.SetTimer(_hwnd, SearchDebounceTimerId, SearchDebounceMs, IntPtr.Zero);
    }

    private void DisposeResources()
    {
        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        _mainPane?.Dispose();
        _filteredPane?.Dispose();

        if (_searchEdit != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_searchEdit);
        }

        if (_font != IntPtr.Zero && _font != NativeMethods.GetStockObject(NativeMethods.SYSTEM_FIXED_FONT))
        {
            NativeMethods.DeleteObject(_font);
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _searchEdit = IntPtr.Zero;
        _font = IntPtr.Zero;
    }

    private void PaintSearchProgress(IntPtr hdc)
    {
        if (!_searchDisplayActive || IsZeroRect(_layout.SearchProgressRect))
        {
            return;
        }

        NativeMethods.RECT frameRect = _layout.SearchProgressRect;
        NativeMethods.RECT fillRect = frameRect;
        NativeMethods.FrameRect(hdc, ref frameRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_BTNSHADOW));

        fillRect.left += 1;
        fillRect.top += 1;
        fillRect.right = Math.Max(fillRect.left, fillRect.right - 1);
        fillRect.bottom = Math.Max(fillRect.top, fillRect.bottom - 1);
        NativeMethods.FillRect(hdc, ref fillRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW));

        int innerWidth = GetRectWidth(fillRect);
        if (innerWidth > 0)
        {
            int filledWidth = (int)Math.Round(innerWidth * Math.Clamp(_searchProgressPercentage, 0d, 100d) / 100d);
            if (filledWidth > 0)
            {
                NativeMethods.RECT progressFill = fillRect;
                progressFill.right = Math.Min(progressFill.right, progressFill.left + filledWidth);
                NativeMethods.FillRect(hdc, ref progressFill, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT));
            }
        }

        string progressText = FormatSearchProgressText();
        NativeMethods.SetTextColor(hdc, NativeMethods.RGB(0, 0, 0));
        NativeMethods.DrawTextW(
            hdc,
            progressText,
            progressText.Length,
            ref fillRect,
            NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_END_ELLIPSIS | NativeMethods.DT_NOPREFIX);
    }

    private string FormatSearchProgressText()
    {
        long matches = Math.Max(0, _searchMatchedLineCount);
        string label = matches == 1 ? "linha" : "linhas";
        return $"{Math.Round(_searchProgressPercentage):0}% • {matches} {label}";
    }

    private void InvalidateHost()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void InvalidateSearchBar()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.RECT rect = IsZeroRect(_layout.SearchBarRect) ? _layout.ClientRect : _layout.SearchBarRect;
        NativeMethods.InvalidateRect(_hwnd, ref rect, false);
    }

    private static string ReadWindowText(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowTextW(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static NativeMethods.RECT CreateZeroRect() => new();

    private static NativeMethods.RECT InsetRect(NativeMethods.RECT rect, int inset)
    {
        return new NativeMethods.RECT
        {
            left = Math.Min(rect.right, rect.left + inset),
            top = Math.Min(rect.bottom, rect.top + inset),
            right = Math.Max(rect.left, rect.right - inset),
            bottom = Math.Max(rect.top, rect.bottom - inset)
        };
    }

    private static bool IsZeroRect(NativeMethods.RECT rect) => GetRectWidth(rect) <= 0 || GetRectHeight(rect) <= 0;

    private static int GetRectWidth(NativeMethods.RECT rect) => Math.Max(0, rect.right - rect.left);

    private static int GetRectHeight(NativeMethods.RECT rect) => Math.Max(0, rect.bottom - rect.top);
}
