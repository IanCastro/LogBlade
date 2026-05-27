using System;
using System.Diagnostics;
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
    public string Query { get; set; } = string.Empty;
    public bool UseRegex { get; set; }
    public bool IgnoreCase { get; set; }
    public bool InvertMatch { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public IViewportReader? Reader { get; set; }
    public int PreloadedVisibleLines { get; set; }
    public double ProgressPercentage { get; set; }
    public long MatchedLineCount { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public bool IsFinal { get; set; }
    public bool IsAppendUpdate { get; set; }
    public bool IsStale { get; set; }
}

internal sealed class ViewerWindow
{
    private const int SearchAreaHeight = 60;
    private const int SearchInputRowHeight = 24;
    private const int SearchProgressRowHeight = 24;
    private const int SearchDebounceMs = 200;
    private const nuint SearchDebounceTimerId = 1;
    private const int SearchInnerPadding = 4;
    private const int SearchProgressGap = 4;
    private const int SearchToggleGap = 8;
    private const int RegexToggleWidth = 72;
    private const int IgnoreCaseToggleWidth = 112;
    private const int InvertMatchToggleWidth = 120;
    private const int SearchResizeHitSlopPx = 4;
    private const string SearchResizeGripClassName = "LogViewerSearchResizeGripWindow";

    private readonly string _path;
    private readonly string _titleSuffix;
    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _searchEdit;
    private IntPtr _searchResizeGrip;
    private IntPtr _regexCheckbox;
    private IntPtr _ignoreCaseCheckbox;
    private IntPtr _invertMatchCheckbox;
    private IntPtr _originalSearchEditProc;
    private FileSystemWatcher? _fileWatcher;
    private GCHandle _selfHandle;
    private int _lineHeight = 16;
    private int _charWidth = 8;
    private bool _firstRenderLogged;
    private bool _closing;
    private string _searchQuery = string.Empty;
    private bool _useRegex = true;
    private bool _ignoreCase;
    private bool _invertMatch;
    private DetectedEncodingInfo? _detectedEncoding;
    private ViewportPaneWindow? _mainPane;
    private ViewportPaneWindow? _filteredPane;
    private WindowLayout _layout;
    private readonly object _searchCancellationSync = new();
    private long _nextSearchRequestId;
    private long _latestSearchRequestId;
    private CancellationTokenSource? _activeSearchCancellation;
    private bool _searchInProgress;
    private bool _searchDisplayActive;
    private int _fileChangeMessagePending;
    private double _searchProgressPercentage;
    private long _searchMatchedLineCount;
    private string _searchErrorText = string.Empty;
    private bool _appendSearchPending;
    private bool _appendSearchInProgress;
    private bool _searchStale;
    private double? _customSearchResultsRatio;
    private bool _isSearchResultsResizing;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static readonly NativeMethods.WindowProc s_searchEditProc = SearchEditProc;
    private static readonly NativeMethods.WindowProc s_searchResizeGripProc = SearchResizeGripProc;
    private static bool s_searchResizeGripRegistered;

    private readonly record struct WindowLayout(
        NativeMethods.RECT ClientRect,
        NativeMethods.RECT ViewerRect,
        NativeMethods.RECT SearchAreaRect,
        NativeMethods.RECT SearchResultsRect,
        NativeMethods.RECT SearchEditRect,
        NativeMethods.RECT SearchRegexToggleRect,
        NativeMethods.RECT SearchIgnoreCaseToggleRect,
        NativeMethods.RECT SearchInvertMatchToggleRect,
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

    private static ViewerWindow? FromSearchEditHandle(IntPtr hwnd)
    {
        IntPtr parent = NativeMethods.GetParent(hwnd);
        return parent == IntPtr.Zero ? null : FromHandle(parent);
    }

    private static ViewerWindow? FromSearchResizeGripHandle(IntPtr hwnd)
    {
        IntPtr parent = NativeMethods.GetParent(hwnd);
        return parent == IntPtr.Zero ? null : FromHandle(parent);
    }

    private static IntPtr SearchEditProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        ViewerWindow? self = FromSearchEditHandle(hwnd);
        if (msg == NativeMethods.WM_KEYDOWN &&
            wParam.ToInt32() == NativeMethods.VK_A &&
            IsControlKeyDown() &&
            self is not null)
        {
            NativeMethods.SendMessageW(hwnd, NativeMethods.EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
            return IntPtr.Zero;
        }

        IntPtr originalProc = self?._originalSearchEditProc ?? IntPtr.Zero;
        return originalProc == IntPtr.Zero
            ? NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam)
            : NativeMethods.CallWindowProcW(originalProc, hwnd, msg, wParam, lParam);
    }

    private static IntPtr SearchResizeGripProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        ViewerWindow? self = FromSearchResizeGripHandle(hwnd);
        switch (msg)
        {
            case NativeMethods.WM_SETCURSOR:
                NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
                return new IntPtr(1);
            case NativeMethods.WM_LBUTTONDOWN:
                self?.BeginSearchResultsResizeFromGrip(lParam);
                return IntPtr.Zero;
            case NativeMethods.WM_MOUSEMOVE:
                self?.UpdateSearchResultsResizeFromGrip(lParam);
                return IntPtr.Zero;
            case NativeMethods.WM_LBUTTONUP:
                self?.EndSearchResultsResize();
                return IntPtr.Zero;
            case NativeMethods.WM_ERASEBKGND:
                return new IntPtr(1);
            case NativeMethods.WM_PAINT:
                NativeMethods.PAINTSTRUCT ps;
                IntPtr hdc = NativeMethods.BeginPaint(hwnd, out ps);
                if (hdc != IntPtr.Zero)
                {
                    NativeMethods.EndPaint(hwnd, ref ps);
                }

                return IntPtr.Zero;
            default:
                return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
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
                case NativeMethods.WM_SETCURSOR:
                    if (self.OnSetCursor())
                    {
                        return new IntPtr(1);
                    }

                    return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
                case NativeMethods.WM_MOUSEMOVE:
                    self.OnMouseMove(lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_LBUTTONDOWN:
                    if (self.OnLButtonDown(lParam))
                    {
                        return IntPtr.Zero;
                    }

                    return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
                case NativeMethods.WM_LBUTTONUP:
                    self.OnLButtonUp();
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
                case NativeMethods.WM_APP_FILE_CHANGED:
                    self.OnFileChanged();
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
            -13,
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

        _filteredPane = new ViewportPaneWindow(_font, _lineHeight, _charWidth, onStale: OnFilteredPaneStale, onRowActivated: OnFilteredRowActivated);
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
        _originalSearchEditProc = NativeMethods.SetWindowLongPtrW(
            _searchEdit,
            NativeMethods.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(s_searchEditProc));

        _regexCheckbox = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            "Regex",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTOCHECKBOX,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_regexCheckbox == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for regex checkbox.");
        }

        _ignoreCaseCheckbox = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            "Ignore case",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTOCHECKBOX,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_ignoreCaseCheckbox == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for ignore-case checkbox.");
        }

        _invertMatchCheckbox = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            "Invert match",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTOCHECKBOX,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_invertMatchCheckbox == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for invert-match checkbox.");
        }

        NativeMethods.SendMessageW(_regexCheckbox, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(_ignoreCaseCheckbox, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(_invertMatchCheckbox, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(_regexCheckbox, NativeMethods.BM_SETCHECK, new IntPtr(NativeMethods.BST_CHECKED), IntPtr.Zero);
        NativeMethods.SendMessageW(_ignoreCaseCheckbox, NativeMethods.BM_SETCHECK, new IntPtr(NativeMethods.BST_UNCHECKED), IntPtr.Zero);
        NativeMethods.SendMessageW(_invertMatchCheckbox, NativeMethods.BM_SETCHECK, new IntPtr(NativeMethods.BST_UNCHECKED), IntPtr.Zero);
        ReadSearchModeFromControls();
        CreateSearchResizeGrip(hInstance);

        RecalculateLayout();
        ApplyLayout();
    }

    private void CreateSearchResizeGrip(IntPtr hInstance)
    {
        EnsureSearchResizeGripClassRegistered(hInstance);
        _searchResizeGrip = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_TRANSPARENT,
            SearchResizeGripClassName,
            string.Empty,
            NativeMethods.WS_CHILD,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_searchResizeGrip == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for search resize grip.");
        }
    }

    private static void EnsureSearchResizeGripClassRegistered(IntPtr hInstance)
    {
        if (s_searchResizeGripRegistered)
        {
            return;
        }

        NativeMethods.WNDCLASSEXW wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = s_searchResizeGripProc,
            hInstance = hInstance,
            hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS),
            hbrBackground = IntPtr.Zero,
            lpszClassName = SearchResizeGripClassName
        };

        ushort atom = NativeMethods.RegisterClassExW(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("RegisterClassExW failed for search resize grip.");
        }

        s_searchResizeGripRegistered = true;
    }

    private void BeginBackgroundOpen()
    {
        if (_closing || _hwnd == IntPtr.Zero || _mainPane is null)
        {
            return;
        }

        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        _latestSearchRequestId = ++_nextSearchRequestId;
        CancelActiveSearch();
        StopFileWatcher();
        _detectedEncoding = null;
        _firstRenderLogged = false;
        _searchInProgress = false;
        _searchDisplayActive = false;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _searchErrorText = string.Empty;
        _appendSearchPending = false;
        _appendSearchInProgress = false;
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
            StartFileWatcher();
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

    private void StartFileWatcher()
    {
        StopFileWatcher();

        string? directory = Path.GetDirectoryName(_path);
        string fileName = Path.GetFileName(_path);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            watcher.Changed += OnWatchedFileChanged;
            watcher.Created += OnWatchedFileChanged;
            watcher.Renamed += OnWatchedFileRenamed;
            watcher.EnableRaisingEvents = true;
            _fileWatcher = watcher;
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error(
                "file.watch.failed",
                "failed",
                new LogField("path", _path),
                new LogField("reason", ex.Message));
        }
    }

    private void StopFileWatcher()
    {
        FileSystemWatcher? watcher = _fileWatcher;
        _fileWatcher = null;
        Interlocked.Exchange(ref _fileChangeMessagePending, 0);
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnWatchedFileChanged;
            watcher.Created -= OnWatchedFileChanged;
            watcher.Renamed -= OnWatchedFileRenamed;
            watcher.Dispose();
        }
        catch
        {
        }
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_closing || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (Interlocked.Exchange(ref _fileChangeMessagePending, 1) == 0 &&
            !NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_APP_FILE_CHANGED, IntPtr.Zero, IntPtr.Zero))
        {
            Interlocked.Exchange(ref _fileChangeMessagePending, 0);
        }
    }

    private void OnWatchedFileRenamed(object sender, RenamedEventArgs e)
    {
        OnWatchedFileChanged(sender, e);
    }

    private void OnFileChanged()
    {
        Interlocked.Exchange(ref _fileChangeMessagePending, 0);
        if (_closing)
        {
            return;
        }

        _mainPane?.QueueTailRefreshIfAtEnd();
        QueueAppendSearchIfNeeded();
    }

    private void OnSize()
    {
        RecalculateLayout();
        ApplyLayout();
        InvalidateHost();
    }

    private bool OnSetCursor()
    {
        if (_isSearchResultsResizing || IsSearchResizeHit(GetClientCursorY()))
        {
            NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
            return true;
        }

        return false;
    }

    private bool OnLButtonDown(IntPtr lParam)
    {
        int y = NativeMethods.HighWord(lParam);
        if (!IsSearchResizeHit(y))
        {
            return false;
        }

        _isSearchResultsResizing = true;
        NativeMethods.SetCapture(_hwnd);
        NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
        return true;
    }

    private void BeginSearchResultsResizeFromGrip(IntPtr lParam)
    {
        _isSearchResultsResizing = true;
        NativeMethods.SetCapture(_searchResizeGrip);
        NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
        ResizeSearchResults(GetGripMouseYInHost(lParam));
    }

    private void OnMouseMove(IntPtr lParam)
    {
        if (!_isSearchResultsResizing)
        {
            return;
        }

        ResizeSearchResults(NativeMethods.HighWord(lParam));
    }

    private void UpdateSearchResultsResizeFromGrip(IntPtr lParam)
    {
        if (!_isSearchResultsResizing)
        {
            return;
        }

        ResizeSearchResults(GetGripMouseYInHost(lParam));
    }

    private void ResizeSearchResults(int y)
    {
        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        int clientHeight = GetRectHeight(clientRect);
        int availableHeight = Math.Max(0, clientHeight - SearchAreaHeight);
        int requestedHeight = Math.Max(0, clientRect.bottom - SearchAreaHeight - y);
        _customSearchResultsRatio = availableHeight > 0
            ? Math.Clamp(requestedHeight / (double)availableHeight, 0d, 1d)
            : 0d;
        RecalculateLayout();
        ApplyLayout();
        InvalidateHost();
    }

    private void OnLButtonUp()
    {
        EndSearchResultsResize();
    }

    private void EndSearchResultsResize()
    {
        if (!_isSearchResultsResizing)
        {
            return;
        }

        _isSearchResultsResizing = false;
        NativeMethods.ReleaseCapture();
    }

    private int GetGripMouseYInHost(IntPtr lParam) =>
        NativeMethods.HighWord(lParam) + GetSearchResizeGripTop();

    private int GetSearchResizeGripTop() =>
        Math.Max(_layout.ClientRect.top, _layout.SearchAreaRect.top - SearchResizeHitSlopPx);

    private bool IsSearchResizeHit(int y)
    {
        if (string.IsNullOrEmpty(_searchQuery) || IsZeroRect(_layout.SearchAreaRect))
        {
            return false;
        }

        return Math.Abs(y - _layout.SearchAreaRect.top) <= SearchResizeHitSlopPx;
    }

    private int GetClientCursorY()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT point))
        {
            return int.MinValue;
        }

        return NativeMethods.ScreenToClient(_hwnd, ref point) ? point.y : int.MinValue;
    }

    private void OnCommand(IntPtr wParam, IntPtr lParam)
    {
        int notification = NativeMethods.HighWord(wParam);
        if (lParam == _searchEdit && notification == NativeMethods.EN_CHANGE)
        {
            _searchQuery = ReadWindowText(_searchEdit);
            RestartSearchAfterInputChange();
            return;
        }

        if ((lParam == _regexCheckbox || lParam == _ignoreCaseCheckbox || lParam == _invertMatchCheckbox) && notification == NativeMethods.BN_CLICKED)
        {
            ReadSearchModeFromControls();
            RestartSearchAfterInputChange();
        }
    }

    private void RestartSearchAfterInputChange()
    {
        _latestSearchRequestId = ++_nextSearchRequestId;
        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        CancelActiveSearch();
        _appendSearchPending = false;
        _appendSearchInProgress = false;

        if (string.IsNullOrEmpty(_searchQuery))
        {
            _searchStale = false;
            _searchInProgress = false;
            _searchDisplayActive = false;
            _searchProgressPercentage = 0d;
            _searchMatchedLineCount = 0;
            _searchErrorText = string.Empty;
            _filteredPane?.SetStatus(string.Empty);
            RecalculateLayout();
            ApplyLayout();
            InvalidateHost();
            return;
        }

        SearchOptions options = new(_searchQuery, _useRegex, _ignoreCase, _invertMatch);
        if (!TryValidateSearchOptions(options))
        {
            return;
        }

        _searchStale = false;
        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _searchErrorText = string.Empty;
        _filteredPane?.SetEmptyContentText("(no matches)");
        _filteredPane?.SetStatus("Searching...", preserveColumnWidths: true);
        RecalculateLayout();
        ApplyLayout();
        InvalidateHost();
        NativeMethods.SetTimer(_hwnd, SearchDebounceTimerId, SearchDebounceMs, IntPtr.Zero);
    }

    private bool TryValidateSearchOptions(SearchOptions options)
    {
        try
        {
            LogSearchBuilder.ValidateOptions(options);
            return true;
        }
        catch (ArgumentException ex) when (options.UseRegex)
        {
            _searchInProgress = false;
            _searchDisplayActive = true;
            _searchErrorText = "Regex error";
            _filteredPane?.SetStatus(string.Empty, disposeReader: false, preserveColumnWidths: true);
            RecalculateLayout();
            ApplyLayout();
            InvalidateHost();
            AppLog.Instance.Error(
                "search.failed",
                "failed",
                new LogField("requestId", _latestSearchRequestId.ToString()),
                new LogField("query", options.Query),
                new LogField("queryLength", options.Query.Length.ToString()),
                new LogField("useRegex", options.UseRegex.ToString()),
                new LogField("ignoreCase", options.IgnoreCase.ToString()),
                new LogField("invertMatch", options.InvertMatch.ToString()),
                new LogField("durationMs", "0"),
                new LogField("reason", ex.Message));
            return false;
        }
    }

    private void OnTimer(nuint timerId)
    {
        if (timerId != SearchDebounceTimerId)
        {
            return;
        }

        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        DispatchSearch(_latestSearchRequestId, new SearchOptions(_searchQuery, _useRegex, _ignoreCase, _invertMatch));
    }

    private void DispatchSearch(long requestId, SearchOptions options)
    {
        if (_closing || string.IsNullOrEmpty(options.Query) || _detectedEncoding is not DetectedEncodingInfo detected || _filteredPane is null)
        {
            return;
        }

        int visibleLines = _filteredPane.VisibleDataLineCount;
        string workerPath = _path;
        IntPtr hwnd = _hwnd;
        string query = options.Query;
        CancellationTokenSource searchCancellation = BeginSearchCancellation();
        CancellationToken cancellationToken = searchCancellation.Token;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Stopwatch workerStopwatch = Stopwatch.StartNew();
            long lastMatchedLineCount = 0;
            double lastProgressPercentage = 0d;
            AppLog.Instance.Info(
                "search.begin",
                "begin",
                new LogField("requestId", requestId.ToString()),
                new LogField("query", query),
                new LogField("queryLength", query.Length.ToString()),
                new LogField("useRegex", options.UseRegex.ToString()),
                new LogField("ignoreCase", options.IgnoreCase.ToString()),
                new LogField("invertMatch", options.InvertMatch.ToString()),
                new LogField("visibleLines", visibleLines.ToString()));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogSearchBuilder.BuildFilteredReaderIncremental(workerPath, detected.Encoding, detected.DataOffset, options, visibleLines, update =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastMatchedLineCount = update.MatchedLineCount;
                    lastProgressPercentage = update.ProgressPercentage;
                    PostSearchWorkerResult(new SearchWorkerResult
                    {
                        RequestId = requestId,
                        Query = query,
                        UseRegex = options.UseRegex,
                        IgnoreCase = options.IgnoreCase,
                        InvertMatch = options.InvertMatch,
                        Success = true,
                        Reader = update.Reader,
                        PreloadedVisibleLines = visibleLines,
                        ProgressPercentage = update.ProgressPercentage,
                        MatchedLineCount = update.MatchedLineCount,
                        ElapsedMilliseconds = update.ElapsedMilliseconds,
                        IsFinal = update.IsFinal,
                        IsAppendUpdate = false
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppLog.Instance.Info(
                    "search.cancelled",
                    "cancelled",
                    new LogField("requestId", requestId.ToString()),
                    new LogField("query", query),
                    new LogField("queryLength", query.Length.ToString()),
                    new LogField("useRegex", options.UseRegex.ToString()),
                    new LogField("ignoreCase", options.IgnoreCase.ToString()),
                    new LogField("invertMatch", options.InvertMatch.ToString()),
                    new LogField("durationMs", workerStopwatch.ElapsedMilliseconds.ToString()),
                    new LogField("matchedLineCount", lastMatchedLineCount.ToString()),
                    new LogField("progressPercentage", Math.Round(Math.Clamp(lastProgressPercentage, 0d, 100d)).ToString()));
            }
            catch (FilteredLineStaleException ex)
            {
                PostSearchWorkerResult(new SearchWorkerResult
                {
                    RequestId = requestId,
                    Query = query,
                    UseRegex = options.UseRegex,
                    IgnoreCase = options.IgnoreCase,
                    InvertMatch = options.InvertMatch,
                    Success = false,
                    IsStale = true,
                    Message = ex.Message,
                    ProgressPercentage = _searchProgressPercentage,
                    MatchedLineCount = _searchMatchedLineCount,
                    ElapsedMilliseconds = workerStopwatch.ElapsedMilliseconds,
                    IsFinal = true,
                    IsAppendUpdate = false
                });
            }
            catch (Exception ex)
            {
                PostSearchWorkerResult(new SearchWorkerResult
                {
                    RequestId = requestId,
                    Query = query,
                    UseRegex = options.UseRegex,
                    IgnoreCase = options.IgnoreCase,
                    InvertMatch = options.InvertMatch,
                    Success = false,
                    Message = ex.Message,
                    ProgressPercentage = _searchProgressPercentage,
                    MatchedLineCount = _searchMatchedLineCount,
                    ElapsedMilliseconds = workerStopwatch.ElapsedMilliseconds,
                    IsFinal = true,
                    IsAppendUpdate = false
                });
            }
            finally
            {
                DisposeSearchCancellation(searchCancellation);
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

    private void QueueAppendSearchIfNeeded()
    {
        if (_closing || string.IsNullOrEmpty(_searchQuery) || _filteredPane is null)
        {
            _appendSearchPending = false;
            return;
        }

        if (!TryGetCurrentFileSize(out long currentFileSize))
        {
            return;
        }

        FilteredVisualRowReader? currentReader = _filteredPane.Reader as FilteredVisualRowReader;
        long knownSearchFileSize = currentReader?.FileSize
            ?? (_mainPane?.Reader as VisualRowReader)?.FileSize
            ?? currentFileSize;
        if (currentFileSize < knownSearchFileSize)
        {
            MarkSearchStale();
            return;
        }

        if (_searchStale)
        {
            return;
        }

        if (_searchInProgress || _appendSearchInProgress)
        {
            _appendSearchPending = true;
            return;
        }

        if (currentReader is null)
        {
            return;
        }

        SearchOptions options = new(_searchQuery, _useRegex, _ignoreCase, _invertMatch);
        if (!AreSearchOptionsValid(options))
        {
            return;
        }

        if (currentFileSize <= currentReader.FileSize)
        {
            return;
        }

        DispatchAppendSearch(currentReader, currentFileSize, options);
    }

    private bool TryGetCurrentFileSize(out long currentFileSize)
    {
        try
        {
            currentFileSize = new FileInfo(_path).Length;
            return true;
        }
        catch (IOException)
        {
            currentFileSize = 0;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            currentFileSize = 0;
            return false;
        }
    }

    private static bool AreSearchOptionsValid(SearchOptions options)
    {
        try
        {
            LogSearchBuilder.ValidateOptions(options);
            return true;
        }
        catch (ArgumentException) when (options.UseRegex)
        {
            return false;
        }
    }

    private void DispatchAppendSearch(FilteredVisualRowReader currentReader, long newFileSize, SearchOptions options)
    {
        if (_filteredPane is null)
        {
            return;
        }

        long requestId = _latestSearchRequestId;
        int visibleLines = _filteredPane.VisibleDataLineCount;
        var readerSnapshot = (FilteredVisualRowReader)currentReader.CloneForWorker();
        long initialMatchedLineCount = readerSnapshot.MatchedLineCount;
        IntPtr hwnd = _hwnd;
        string query = options.Query;
        CancellationTokenSource searchCancellation = BeginSearchCancellation();
        CancellationToken cancellationToken = searchCancellation.Token;
        _appendSearchPending = false;
        _appendSearchInProgress = true;
        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchErrorText = string.Empty;
        InvalidateSearchBar();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            Stopwatch workerStopwatch = Stopwatch.StartNew();
            long lastMatchedLineCount = initialMatchedLineCount;
            double lastProgressPercentage = 0d;
            AppLog.Instance.Info(
                "search.append.begin",
                "begin",
                new LogField("requestId", requestId.ToString()),
                new LogField("query", query),
                new LogField("queryLength", query.Length.ToString()),
                new LogField("useRegex", options.UseRegex.ToString()),
                new LogField("ignoreCase", options.IgnoreCase.ToString()),
                new LogField("invertMatch", options.InvertMatch.ToString()),
                new LogField("oldFileSize", readerSnapshot.FileSize.ToString()),
                new LogField("newFileSize", newFileSize.ToString()));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogSearchBuilder.BuildAppendedFilteredReaderIncremental(readerSnapshot, options, newFileSize, visibleLines, update =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastMatchedLineCount = update.MatchedLineCount;
                    lastProgressPercentage = update.ProgressPercentage;
                    PostSearchWorkerResult(new SearchWorkerResult
                    {
                        RequestId = requestId,
                        Query = query,
                        UseRegex = options.UseRegex,
                        IgnoreCase = options.IgnoreCase,
                        InvertMatch = options.InvertMatch,
                        Success = true,
                        Reader = update.Reader,
                        PreloadedVisibleLines = visibleLines,
                        ProgressPercentage = update.ProgressPercentage,
                        MatchedLineCount = update.MatchedLineCount,
                        ElapsedMilliseconds = update.ElapsedMilliseconds,
                        IsFinal = update.IsFinal,
                        IsAppendUpdate = true
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppLog.Instance.Info(
                    "search.append.cancelled",
                    "cancelled",
                    new LogField("requestId", requestId.ToString()),
                    new LogField("query", query),
                    new LogField("queryLength", query.Length.ToString()),
                    new LogField("useRegex", options.UseRegex.ToString()),
                    new LogField("ignoreCase", options.IgnoreCase.ToString()),
                    new LogField("invertMatch", options.InvertMatch.ToString()),
                    new LogField("durationMs", workerStopwatch.ElapsedMilliseconds.ToString()),
                    new LogField("matchedLineCount", lastMatchedLineCount.ToString()),
                    new LogField("progressPercentage", Math.Round(Math.Clamp(lastProgressPercentage, 0d, 100d)).ToString()));
            }
            catch (FilteredLineStaleException ex)
            {
                PostSearchWorkerResult(new SearchWorkerResult
                {
                    RequestId = requestId,
                    Query = query,
                    UseRegex = options.UseRegex,
                    IgnoreCase = options.IgnoreCase,
                    InvertMatch = options.InvertMatch,
                    Success = false,
                    IsStale = true,
                    Message = ex.Message,
                    ProgressPercentage = _searchProgressPercentage,
                    MatchedLineCount = _searchMatchedLineCount,
                    ElapsedMilliseconds = workerStopwatch.ElapsedMilliseconds,
                    IsFinal = true,
                    IsAppendUpdate = true
                });
            }
            catch (Exception ex)
            {
                PostSearchWorkerResult(new SearchWorkerResult
                {
                    RequestId = requestId,
                    Query = query,
                    UseRegex = options.UseRegex,
                    IgnoreCase = options.IgnoreCase,
                    InvertMatch = options.InvertMatch,
                    Success = false,
                    Message = ex.Message,
                    ProgressPercentage = _searchProgressPercentage,
                    MatchedLineCount = _searchMatchedLineCount,
                    ElapsedMilliseconds = workerStopwatch.ElapsedMilliseconds,
                    IsFinal = true,
                    IsAppendUpdate = true
                });
            }
            finally
            {
                readerSnapshot.Dispose();
                DisposeSearchCancellation(searchCancellation);
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

    private void MarkSearchStale()
    {
        _latestSearchRequestId = ++_nextSearchRequestId;
        CancelActiveSearch();
        _appendSearchPending = false;
        _appendSearchInProgress = false;
        _searchInProgress = false;
        _searchStale = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _searchErrorText = "Search stale";
        _filteredPane?.SetEmptyContentText(string.Empty);
        _filteredPane?.SetStatus(string.Empty);
        RecalculateLayout();
        ApplyLayout();
        InvalidateSearchBar();
    }

    private static bool IsFilteredLineStaleMessage(string? message)
    {
        return message is not null &&
            message.StartsWith("Filtered line ", StringComparison.Ordinal);
    }

    private void OnFilteredPaneStale(ViewportPaneWindow pane)
    {
        if (!ReferenceEquals(pane, _filteredPane) || _closing)
        {
            return;
        }

        MarkSearchStale();
    }

    private void OnFilteredRowActivated(ViewportPaneWindow pane, long startOffset)
    {
        if (!ReferenceEquals(pane, _filteredPane) || _closing || _mainPane is null)
        {
            return;
        }

        _mainPane.JumpToFileOffset(startOffset);
    }

    private CancellationTokenSource BeginSearchCancellation()
    {
        var nextCancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation;
        lock (_searchCancellationSync)
        {
            previousCancellation = _activeSearchCancellation;
            _activeSearchCancellation = nextCancellation;
        }

        try
        {
            previousCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return nextCancellation;
    }

    private void CancelActiveSearch()
    {
        CancellationTokenSource? cancellation;
        lock (_searchCancellationSync)
        {
            cancellation = _activeSearchCancellation;
            _activeSearchCancellation = null;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisposeSearchCancellation(CancellationTokenSource cancellation)
    {
        lock (_searchCancellationSync)
        {
            if (ReferenceEquals(_activeSearchCancellation, cancellation))
            {
                _activeSearchCancellation = null;
            }
        }

        cancellation.Dispose();
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

        if (_closing)
        {
            if (result.IsAppendUpdate)
            {
                _appendSearchInProgress = false;
            }

            result.Reader?.Dispose();
            return;
        }

        if (result.RequestId != _latestSearchRequestId || string.IsNullOrEmpty(_searchQuery))
        {
            if (result.IsAppendUpdate)
            {
                _appendSearchInProgress = false;
            }

            if (result.IsFinal)
            {
                LogSearchDiscarded(result);
            }

            result.Reader?.Dispose();
            return;
        }

        _searchProgressPercentage = Math.Clamp(result.ProgressPercentage, 0d, 100d);
        _searchMatchedLineCount = Math.Max(0, result.MatchedLineCount);
        _searchInProgress = !result.IsFinal;
        _searchDisplayActive = true;
        _searchErrorText = string.Empty;

        if (_filteredPane is null)
        {
            result.Reader?.Dispose();
            return;
        }

        if (!result.Success)
        {
            if (result.IsAppendUpdate)
            {
                _appendSearchInProgress = false;
            }

            if (result.IsStale || IsFilteredLineStaleMessage(result.Message))
            {
                result.Reader?.Dispose();
                MarkSearchStale();
                return;
            }

            _searchInProgress = false;
            _searchDisplayActive = true;
            _searchErrorText = result.UseRegex ? "Regex error" : "Search error";
            result.Reader?.Dispose();
            _filteredPane.SetStatus(string.Empty, disposeReader: false, preserveColumnWidths: true);
            InvalidateSearchBar();
            AppLog.Instance.Error(
                "search.failed",
                "failed",
                new LogField("requestId", result.RequestId.ToString()),
                new LogField("query", result.Query),
                new LogField("queryLength", result.Query.Length.ToString()),
                new LogField("useRegex", result.UseRegex.ToString()),
                new LogField("ignoreCase", result.IgnoreCase.ToString()),
                new LogField("invertMatch", result.InvertMatch.ToString()),
                new LogField("durationMs", result.ElapsedMilliseconds.ToString()),
                new LogField("reason", result.Message ?? "unknown error"));
            return;
        }

        if (result.Reader is not null)
        {
            try
            {
                if (result.Reader is FilteredVisualRowReader nextFilteredReader)
                {
                    long desiredTopRow = 0;
                    bool shouldKeepAtEnd = false;
                    if (_filteredPane.Reader is FilteredVisualRowReader currentFilteredReader)
                    {
                        desiredTopRow = currentFilteredReader.TopRowOrdinal;
                        shouldKeepAtEnd = result.IsAppendUpdate && currentFilteredReader.IsAtEnd;
                    }

                    if (shouldKeepAtEnd)
                    {
                        nextFilteredReader.ReadFromPercentage(100d, _filteredPane.VisibleDataLineCount);
                    }
                    else
                    {
                        nextFilteredReader.ReadFromRowOrdinal(desiredTopRow, _filteredPane.VisibleDataLineCount);
                    }
                }

                _filteredPane.SetEmptyContentText("(no matches)");
                _filteredPane.SetReader(
                    result.Reader,
                    _filteredPane.VisibleDataLineCount,
                    preserveColumnWidths: true,
                    preserveSelection: true);
            }
            catch (FilteredLineStaleException)
            {
                result.Reader.Dispose();
                MarkSearchStale();
                return;
            }
        }

        if (result.IsFinal)
        {
            _searchInProgress = false;
            if (result.IsAppendUpdate)
            {
                _appendSearchInProgress = false;
            }

            _searchProgressPercentage = 100d;
            string eventName = result.IsAppendUpdate ? "search.append.complete" : "search.complete";
            AppLog.Instance.Info(
                eventName,
                "complete",
                new LogField("requestId", result.RequestId.ToString()),
                new LogField("query", result.Query),
                new LogField("queryLength", result.Query.Length.ToString()),
                new LogField("useRegex", result.UseRegex.ToString()),
                new LogField("ignoreCase", result.IgnoreCase.ToString()),
                new LogField("invertMatch", result.InvertMatch.ToString()),
                new LogField("durationMs", result.ElapsedMilliseconds.ToString()),
                new LogField("matchedLineCount", result.MatchedLineCount.ToString()),
                new LogField("progressPercentage", Math.Round(_searchProgressPercentage).ToString()));

            if (_appendSearchPending)
            {
                QueueAppendSearchIfNeeded();
            }
        }

        InvalidateSearchBar();
    }

    private static void LogSearchDiscarded(SearchWorkerResult result)
    {
        AppLog.Instance.Info(
            "search.discarded",
            "discarded",
            new LogField("requestId", result.RequestId.ToString()),
            new LogField("query", result.Query),
            new LogField("queryLength", result.Query.Length.ToString()),
            new LogField("useRegex", result.UseRegex.ToString()),
            new LogField("ignoreCase", result.IgnoreCase.ToString()),
            new LogField("invertMatch", result.InvertMatch.ToString()),
            new LogField("durationMs", result.ElapsedMilliseconds.ToString()),
            new LogField("matchedLineCount", result.MatchedLineCount.ToString()));
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
            int availableHeight = Math.Max(0, clientHeight - SearchAreaHeight);
            double preferredRatio = _customSearchResultsRatio ?? 0.35d;
            int preferredHeight = (int)Math.Round(availableHeight * preferredRatio);
            searchResultsHeight = ClampSearchResultsHeight(preferredHeight, availableHeight);
        }

        int viewerBottom = Math.Max(clientRect.top, clientRect.bottom - SearchAreaHeight - searchResultsHeight);
        int searchAreaTop = viewerBottom;
        int searchAreaBottom = Math.Min(clientRect.bottom, searchAreaTop + SearchAreaHeight);
        NativeMethods.RECT viewerRect = new()
        {
            left = clientRect.left,
            top = clientRect.top,
            right = clientRect.right,
            bottom = viewerBottom
        };
        NativeMethods.RECT searchAreaRect = new()
        {
            left = clientRect.left,
            top = searchAreaTop,
            right = clientRect.right,
            bottom = searchAreaBottom
        };
        NativeMethods.RECT searchResultsRect = searchResultsHeight > 0
            ? new NativeMethods.RECT
            {
                left = clientRect.left,
                top = searchAreaBottom,
                right = clientRect.right,
                bottom = clientRect.bottom
            }
            : CreateZeroRect();

        NativeMethods.RECT searchAreaInner = InsetRect(searchAreaRect, SearchInnerPadding);
        int inputBottom = Math.Min(searchAreaInner.bottom, searchAreaInner.top + SearchInputRowHeight);
        NativeMethods.RECT inputRowRect = new()
        {
            left = searchAreaInner.left,
            top = searchAreaInner.top,
            right = searchAreaInner.right,
            bottom = inputBottom
        };

        NativeMethods.RECT invertMatchRect = new()
        {
            left = Math.Max(inputRowRect.left, inputRowRect.right - InvertMatchToggleWidth),
            top = inputRowRect.top,
            right = inputRowRect.right,
            bottom = inputRowRect.bottom
        };
        int ignoreCaseRight = Math.Max(inputRowRect.left, invertMatchRect.left - SearchToggleGap);
        NativeMethods.RECT ignoreCaseRect = new()
        {
            left = Math.Max(inputRowRect.left, ignoreCaseRight - IgnoreCaseToggleWidth),
            top = inputRowRect.top,
            right = ignoreCaseRight,
            bottom = inputRowRect.bottom
        };
        int regexRight = Math.Max(inputRowRect.left, ignoreCaseRect.left - SearchToggleGap);
        NativeMethods.RECT regexRect = new()
        {
            left = Math.Max(inputRowRect.left, regexRight - RegexToggleWidth),
            top = inputRowRect.top,
            right = regexRight,
            bottom = inputRowRect.bottom
        };
        NativeMethods.RECT editRect = new()
        {
            left = inputRowRect.left,
            top = inputRowRect.top,
            right = Math.Max(inputRowRect.left, regexRect.left - SearchToggleGap),
            bottom = inputRowRect.bottom
        };

        int progressTop = Math.Min(searchAreaInner.bottom, inputBottom + SearchProgressGap);
        NativeMethods.RECT progressRect = progressTop < searchAreaInner.bottom
            ? new NativeMethods.RECT
            {
                left = searchAreaInner.left,
                top = progressTop,
                right = searchAreaInner.right,
                bottom = Math.Min(searchAreaInner.bottom, progressTop + SearchProgressRowHeight)
            }
            : CreateZeroRect();

        _layout = new WindowLayout(clientRect, viewerRect, searchAreaRect, searchResultsRect, editRect, regexRect, ignoreCaseRect, invertMatchRect, progressRect);
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
        NativeMethods.MoveWindow(
            _regexCheckbox,
            _layout.SearchRegexToggleRect.left,
            _layout.SearchRegexToggleRect.top,
            GetRectWidth(_layout.SearchRegexToggleRect),
            GetRectHeight(_layout.SearchRegexToggleRect),
            true);
        NativeMethods.MoveWindow(
            _ignoreCaseCheckbox,
            _layout.SearchIgnoreCaseToggleRect.left,
            _layout.SearchIgnoreCaseToggleRect.top,
            GetRectWidth(_layout.SearchIgnoreCaseToggleRect),
            GetRectHeight(_layout.SearchIgnoreCaseToggleRect),
            true);
        NativeMethods.MoveWindow(
            _invertMatchCheckbox,
            _layout.SearchInvertMatchToggleRect.left,
            _layout.SearchInvertMatchToggleRect.top,
            GetRectWidth(_layout.SearchInvertMatchToggleRect),
            GetRectHeight(_layout.SearchInvertMatchToggleRect),
            true);
        NativeMethods.ShowWindow(_searchEdit, NativeMethods.SW_SHOW);
        NativeMethods.ShowWindow(_regexCheckbox, NativeMethods.SW_SHOW);
        NativeMethods.ShowWindow(_ignoreCaseCheckbox, NativeMethods.SW_SHOW);
        NativeMethods.ShowWindow(_invertMatchCheckbox, NativeMethods.SW_SHOW);
        ApplySearchResizeGripLayout();
    }

    private void ApplySearchResizeGripLayout()
    {
        if (_searchResizeGrip == IntPtr.Zero)
        {
            return;
        }

        bool visible = !string.IsNullOrEmpty(_searchQuery) && !IsZeroRect(_layout.SearchAreaRect);
        uint visibilityFlag = visible ? NativeMethods.SWP_SHOWWINDOW : NativeMethods.SWP_HIDEWINDOW;
        int top = GetSearchResizeGripTop();
        int height = visible
            ? Math.Min(SearchResizeHitSlopPx * 2 + 1, Math.Max(0, _layout.ClientRect.bottom - top))
            : 1;
        NativeMethods.SetWindowPos(
            _searchResizeGrip,
            NativeMethods.HWND_TOP,
            _layout.ClientRect.left,
            top,
            GetRectWidth(_layout.ClientRect),
            Math.Max(1, height),
            NativeMethods.SWP_NOACTIVATE | visibilityFlag);
    }

    private void ScheduleSearch()
    {
        if (string.IsNullOrEmpty(_searchQuery))
        {
            return;
        }

        RestartSearchAfterInputChange();
    }

    private void DisposeResources()
    {
        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        StopFileWatcher();
        CancelActiveSearch();
        if (_isSearchResultsResizing)
        {
            _isSearchResultsResizing = false;
            NativeMethods.ReleaseCapture();
        }

        _mainPane?.Dispose();
        _filteredPane?.Dispose();

        if (_searchEdit != IntPtr.Zero)
        {
            if (_originalSearchEditProc != IntPtr.Zero)
            {
                NativeMethods.SetWindowLongPtrW(_searchEdit, NativeMethods.GWLP_WNDPROC, _originalSearchEditProc);
                _originalSearchEditProc = IntPtr.Zero;
            }

            NativeMethods.DestroyWindow(_searchEdit);
        }

        if (_searchResizeGrip != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_searchResizeGrip);
        }

        if (_regexCheckbox != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_regexCheckbox);
        }

        if (_ignoreCaseCheckbox != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_ignoreCaseCheckbox);
        }

        if (_invertMatchCheckbox != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_invertMatchCheckbox);
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
        _searchResizeGrip = IntPtr.Zero;
        _regexCheckbox = IntPtr.Zero;
        _ignoreCaseCheckbox = IntPtr.Zero;
        _invertMatchCheckbox = IntPtr.Zero;
        _originalSearchEditProc = IntPtr.Zero;
        _font = IntPtr.Zero;
    }

    private void PaintSearchProgress(IntPtr hdc)
    {
        if ((!_searchDisplayActive && string.IsNullOrEmpty(_searchErrorText)) || IsZeroRect(_layout.SearchProgressRect))
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
        if (innerWidth > 0 && string.IsNullOrEmpty(_searchErrorText))
        {
            int filledWidth = (int)Math.Round(innerWidth * Math.Clamp(_searchProgressPercentage, 0d, 100d) / 100d);
            if (filledWidth > 0)
            {
                NativeMethods.RECT progressFill = fillRect;
                progressFill.right = Math.Min(progressFill.right, progressFill.left + filledWidth);
                NativeMethods.FillRect(hdc, ref progressFill, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT));
            }
        }

        string progressText = string.IsNullOrEmpty(_searchErrorText) ? FormatSearchProgressText() : _searchErrorText;
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
        return $"{Math.Round(_searchProgressPercentage):0}% \u2022 {matches} {label}";
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

        NativeMethods.RECT rect = IsZeroRect(_layout.SearchAreaRect) ? _layout.ClientRect : _layout.SearchAreaRect;
        NativeMethods.InvalidateRect(_hwnd, ref rect, false);
    }

    private void ReadSearchModeFromControls()
    {
        _useRegex = IsButtonChecked(_regexCheckbox);
        _ignoreCase = IsButtonChecked(_ignoreCaseCheckbox);
        _invertMatch = IsButtonChecked(_invertMatchCheckbox);
    }

    private static bool IsControlKeyDown() =>
        (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & unchecked((short)0x8000)) != 0;

    private static bool IsButtonChecked(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.SendMessageW(hwnd, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32() == NativeMethods.BST_CHECKED;
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

    private static int ClampSearchResultsHeight(int requestedHeight, int availableHeight)
    {
        return Math.Clamp(requestedHeight, 0, Math.Max(0, availableHeight));
    }
}
