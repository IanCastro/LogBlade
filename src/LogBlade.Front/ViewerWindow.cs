using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal sealed class OpenWorkerResult
{
    public long RequestId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public DetectedEncodingInfo? DetectedEncoding { get; set; }
    public LogRecordSource? Reader { get; set; }
    public int PreloadedVisibleLines { get; set; }
}

internal sealed class SearchWorkerResult
{
    public long RequestId { get; set; }
    public string Query { get; set; } = string.Empty;
    public int SearchLevelCount { get; set; }
    public bool UseRegex { get; set; }
    public bool IgnoreCase { get; set; }
    public bool InvertMatch { get; set; }
    public SearchOptions[]? Options { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public FilteredLogRecordSource?[]? Readers { get; set; }
    public int[]? PreloadedVisibleLines { get; set; }
    public double ProgressPercentage { get; set; }
    public long MatchedLineCount { get; set; }
    public long[]? StageMatchedLineCounts { get; set; }
    public int SearchStartLevel { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public bool IsFinal { get; set; }
    public bool IsAppendUpdate { get; set; }
    public bool IsStale { get; set; }
    public bool IsPaused { get; set; }
    public long ProcessedOffset { get; set; }
    public long TargetFileSize { get; set; }
}

internal sealed class ViewerWindow
{
    private enum LayoutRedrawMode
    {
        Deferred,
        Immediate
    }

    private const int TopBarHeight = 34;
    private const int TopBarPadding = 4;
    private const int OpenButtonWidth = 72;
    private const int PasteButtonWidth = 64;
    private const int ParserComboWidth = 360;
    private const int HighlightingButtonWidth = 110;
    private const int SearchInputRowHeight = 24;
    private const int SearchProgressRowHeight = 24;
    private const int SearchDebounceMs = 200;
    private const nuint SearchDebounceTimerId = 1;
    private const int SearchInnerPadding = 4;
    private const int SearchLevelRowGap = 4;
    private const int SearchProgressGap = 4;
    private const int SearchToggleGap = 8;
    private const int RegexToggleWidth = 72;
    private const int IgnoreCaseToggleWidth = 112;
    private const int InvertMatchToggleWidth = 120;
    private const int ParserInputVisibilityButtonWidth = 24;
    private const string ParserInputVisibilityGlyph = "\uE890";
    private const int SearchResizeHitSlopPx = 4;
    private const string SearchResizeGripClassName = "LogBladeSearchResizeGripWindow";
    private const string NoParserText = "Choose a parser...";
    private const string ConfigureParserText = "Configure parsers...";
    private const int DefaultWindowWidth = 1100;
    private const int DefaultWindowHeight = 760;

    private LogContentSource _contentSource;
    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _boldFont;
    private IntPtr _italicFont;
    private IntPtr _boldItalicFont;
    private IntPtr _openButton;
    private IntPtr _pasteButton;
    private IntPtr _parserCombo;
    private IntPtr _highlightingButton;
    private IntPtr _parserInputVisibilityFont;
    private IntPtr _tooltipWindow;
    private IntPtr _searchResizeGrip;
    private FileSystemWatcher? _fileWatcher;
    private GCHandle _selfHandle;
    private int _lineHeight = 16;
    private int _charWidth = 8;
    private bool _firstRenderLogged;
    private bool _closing;
    private bool _configurationWindowOpen;
    private bool _resourcesDisposed;
    private bool _windowStateSaved;
    private bool _updatingParserCombo;
    private bool _updatingSearchControls;
    private long _knownContentFileSize;
    private long _nextOpenRequestId;
    private long _latestOpenRequestId;
    private List<DisplayParserRule> _parserRules = new();
    private DisplayParserRule? _previewParserRule;
    private bool _parserPreviewActive;
    private List<HighlightRule> _highlightRules = new();
    private IReadOnlyList<CompiledHighlightRule> _compiledHighlightRules = Array.Empty<CompiledHighlightRule>();
    private int _selectedParserRuleIndex = -1;
    private readonly List<SearchLevelState> _searchLevels = new();
    private readonly List<bool> _parserInputVisibility = new();
    private int _activeSearchLevelCount;
    private int _parserFilterLevelCount;
    private bool _resetParserInputVisibilityOnNextApply;
    private DetectedEncodingInfo? _detectedEncoding;
    private ViewportPaneWindow? _mainPane;
    private WindowLayout _layout;
    private readonly object _searchCancellationSync = new();
    private long _nextSearchRequestId;
    private long _latestSearchRequestId;
    private CancellationTokenSource? _activeSearchCancellation;
    private readonly CancellationTokenSource _pasteTransferCancellation = new();
    private long _activeSearchWorkerRequestId;
    private int _activeSearchWorkerStartLevel;
    private bool _searchInProgress;
    private bool _searchDisplayActive;
    private int _fileChangeMessagePending;
    private double _searchProgressPercentage;
    private long _searchMatchedLineCount;
    private string _searchErrorText = string.Empty;
    private bool _appendSearchPending;
    private bool _appendSearchInProgress;
    private bool _searchStale;
    private PausedSearchCheckpoint? _pausedSearchCheckpoint;
    private long _waitingForPausedSearchRequestId;
    private int _pendingSearchStartLevel;
    private PendingLowerSearchChange? _pendingLowerSearchChange;
    private double? _customSearchAreaRatio;
    private bool _isSearchAreaResizing;
    private int _searchAreaResizeStartY;
    private int _searchAreaResizeStartHeight;
    private bool _isSearchResultsResizing;
    private int _resizingSearchResultIndex = -1;
    private int _searchResultResizeStartY;
    private int _searchResultResizeAvailableHeight;
    private double _searchResultResizeStartRatio;
    private double[]? _searchResultResizeStartRatios;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static readonly NativeMethods.WindowProc s_searchEditProc = SearchEditProc;
    private static readonly NativeMethods.WindowProc s_searchResizeGripProc = SearchResizeGripProc;
    private static bool s_searchResizeGripRegistered;

    private sealed class SearchLevelState
    {
        public IntPtr SearchEdit;
        public IntPtr ParserInputVisibilityButton;
        public IntPtr ParserInputTooltipText;
        public IntPtr RegexCheckbox;
        public IntPtr IgnoreCaseCheckbox;
        public IntPtr InvertMatchCheckbox;
        public IntPtr OriginalSearchEditProc;
        public ViewportPaneWindow? ResultsPane;
        public string Query = string.Empty;
        public bool UseRegex = true;
        public bool IgnoreCase;
        public bool InvertMatch;
        public bool IsParserFilter;
        public bool ShowParserInput;
        public double? CustomResultRatio;
    }

    private sealed class PausedSearchCheckpoint
    {
        public long ProcessedOffset;
        public long TargetFileSize;
        public SearchOptions[] Options = Array.Empty<SearchOptions>();
        public long MatchedLineCount;
        public double ProgressPercentage;
    }

    private sealed class PendingLowerSearchChange
    {
        public long SourceRequestId;
        public int StartLevel;
        public SearchOptions[] Options = Array.Empty<SearchOptions>();
    }

    private readonly record struct SearchLevelSnapshot(string Query, bool UseRegex, bool IgnoreCase, bool InvertMatch);

    private readonly record struct ActiveSearchLevelSnapshot(int SourceIndex, SearchLevelSnapshot Snapshot);

    private readonly record struct SearchLevelLayout(
        NativeMethods.RECT ParserInputVisibilityButtonRect,
        NativeMethods.RECT SearchEditRect,
        NativeMethods.RECT SearchRegexToggleRect,
        NativeMethods.RECT SearchIgnoreCaseToggleRect,
        NativeMethods.RECT SearchInvertMatchToggleRect,
        NativeMethods.RECT SearchResultsRect);

    private readonly record struct WindowLayout(
        NativeMethods.RECT ClientRect,
        NativeMethods.RECT TopBarRect,
        NativeMethods.RECT OpenButtonRect,
        NativeMethods.RECT PasteButtonRect,
        NativeMethods.RECT ParserComboRect,
        NativeMethods.RECT HighlightingButtonRect,
        NativeMethods.RECT ViewerRect,
        NativeMethods.RECT SearchAreaRect,
        SearchLevelLayout[] SearchLevelLayouts,
        NativeMethods.RECT SearchProgressRect);

    public ViewerWindow(string path)
        : this(LogContentSource.FromFile(path))
    {
    }

    public ViewerWindow(LogContentSource contentSource)
    {
        _contentSource = contentSource;
        _knownContentFileSize = contentSource.IsFile ? 0 : contentSource.Length;
    }

    public void Run()
    {
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        const string className = "LogBladeNativeAotWindow";
        AppLog.Instance.Info("window.create.begin", "begin", new LogField("class", className));

        string windowTitle = ComposeWindowTitle();
        NativeMethods.WNDCLASSEXW wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = s_wndProc,
            hInstance = hInstance,
            hIcon = AppIcon.Big,
            hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_ARROW),
            hbrBackground = NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW),
            lpszClassName = className,
            hIconSm = AppIcon.Small
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

        WindowStateSettings? savedWindowState = WindowStateStore.Load();
        int initialX = savedWindowState?.Left ?? NativeMethods.CW_USEDEFAULT;
        int initialY = savedWindowState?.Top ?? NativeMethods.CW_USEDEFAULT;
        int initialWidth = savedWindowState?.Width ?? DefaultWindowWidth;
        int initialHeight = savedWindowState?.Height ?? DefaultWindowHeight;
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            className,
            windowTitle,
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
            initialX,
            initialY,
            initialWidth,
            initialHeight,
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
        AppIcon.ApplyToWindow(_hwnd);
        int showCommand = savedWindowState is null
            ? NativeMethods.SW_SHOWDEFAULT
            : savedWindowState.WindowState == WindowStateStore.MaximizedState
                ? NativeMethods.SW_SHOWMAXIMIZED
                : NativeMethods.SW_SHOWNORMAL;
        NativeMethods.ShowWindow(_hwnd, showCommand);
        NativeMethods.UpdateWindow(_hwnd);
        NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_APP_BEGIN_OPEN, IntPtr.Zero, IntPtr.Zero);

        NativeMethods.MSG msg;
        int getMessageResult;
        while ((getMessageResult = NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0)) > 0)
        {
            if (TryHandleGlobalShortcut(msg))
            {
                continue;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (getMessageResult < 0)
        {
            throw new InvalidOperationException("GetMessageW failed.");
        }

        AppLog.Instance.Info("shutdown", "normal_exit");
    }

    private string ComposeWindowTitle()
    {
        if (_contentSource.FilePath is not string path)
        {
            return Program.AppTitle + " - " + _contentSource.DisplayName;
        }

        string fileName = Path.GetFileName(path);
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        return fileName + " - " + Program.AppTitle + " - " + directory;
    }

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
        SearchLevelState? level = self?.FindSearchLevelByEdit(hwnd);
        if (msg == NativeMethods.WM_KEYDOWN &&
            wParam.ToInt32() == NativeMethods.VK_A &&
            IsControlKeyDown() &&
            self is not null)
        {
            NativeMethods.SendMessageW(hwnd, NativeMethods.EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
            return IntPtr.Zero;
        }

        IntPtr originalProc = level?.OriginalSearchEditProc ?? IntPtr.Zero;
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
                    self.OnSize(wParam.ToInt32());
                    return IntPtr.Zero;
                case NativeMethods.WM_COMMAND:
                    self.OnCommand(wParam, lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_DRAWITEM:
                    if (self.OnDrawItem(lParam))
                    {
                        return new IntPtr(1);
                    }

                    return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
                case NativeMethods.WM_TIMER:
                    self.OnTimer((nuint)wParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_KEYDOWN:
                    if (self.OnKeyDown(wParam.ToInt32()))
                    {
                        return IntPtr.Zero;
                    }

                    return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
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
                case NativeMethods.WM_APP_PASTE_COMPLETE:
                    self.OnPastedTextLaunchComplete(lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_CLOSE:
                    if (self._configurationWindowOpen)
                    {
                        return IntPtr.Zero;
                    }

                    self.SaveWindowState();
                    return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
                case NativeMethods.WM_DESTROY:
                    self._closing = true;
                    self.SaveWindowState();
                    self.DisposeResources();
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;
                case NativeMethods.WM_NCDESTROY:
                    self.DisposeResources();
                    NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, IntPtr.Zero);
                    if (self._selfHandle.IsAllocated)
                    {
                        self._selfHandle.Free();
                    }

                    self._hwnd = IntPtr.Zero;
                    return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
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
        _font = CreateContentFont(NativeMethods.FW_NORMAL, italic: false);

        if (_font == IntPtr.Zero)
        {
            _font = NativeMethods.GetStockObject(NativeMethods.SYSTEM_FIXED_FONT);
        }

        _boldFont = CreateContentFont(NativeMethods.FW_BOLD, italic: false);
        _italicFont = CreateContentFont(NativeMethods.FW_NORMAL, italic: true);
        _boldItalicFont = CreateContentFont(NativeMethods.FW_BOLD, italic: true);
        _parserInputVisibilityFont = CreateParserInputVisibilityFont();
        if (_parserInputVisibilityFont == IntPtr.Zero)
        {
            _parserInputVisibilityFont = _font;
        }

        MeasureFont();

        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        CreateParserControls(hInstance);
        CreateTooltipWindow(hInstance);
        ReloadParserRules(selectRuleName: null);
        ReloadHighlightRules();

        _mainPane = new ViewportPaneWindow(
            _font,
            _lineHeight,
            _charWidth,
            boldFont: _boldFont,
            italicFont: _italicFont,
            boldItalicFont: _boldItalicFont,
            onUsefulPaint: OnMainPaneUsefulPaint,
            onPasteRequested: OpenClipboardText,
            onHostVerticalResizeHit: OnMainPaneResizeHit,
            onHostVerticalResizeBegin: OnMainPaneResizeBegin);
        _mainPane.Create(_hwnd, hInstance);
        _mainPane.SetHighlightRules(_compiledHighlightRules);
        _mainPane.SetStatus("Loading file...");

        CreateSearchLevel(hInstance);
        CreateSearchResizeGrip(hInstance);

        UpdateLayout();
    }

    private void CreateParserControls(IntPtr hInstance)
    {
        _openButton = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            "Open...",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_PUSHBUTTON,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
        if (_openButton == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for open button.");
        }

        _pasteButton = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            "Paste",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_PUSHBUTTON,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
        if (_pasteButton == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for paste button.");
        }

        _parserCombo = NativeMethods.CreateWindowExW(
            0,
            "COMBOBOX",
            string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_VSCROLL | NativeMethods.CBS_DROPDOWNLIST | NativeMethods.CBS_OWNERDRAWFIXED | NativeMethods.CBS_HASSTRINGS,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_parserCombo == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for parser combo.");
        }

        NativeMethods.SendMessageW(_openButton, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(_pasteButton, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(_parserCombo, NativeMethods.WM_SETFONT, _font, new IntPtr(1));

        _highlightingButton = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            "Highlighting",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_PUSHBUTTON,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
        if (_highlightingButton == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for highlighting button.");
        }

        NativeMethods.SendMessageW(_highlightingButton, NativeMethods.WM_SETFONT, _font, new IntPtr(1));

    }

    private void CreateTooltipWindow(IntPtr hInstance)
    {
        NativeMethods.INITCOMMONCONTROLSEX controls = new()
        {
            dwSize = (uint)Marshal.SizeOf<NativeMethods.INITCOMMONCONTROLSEX>(),
            dwICC = NativeMethods.ICC_WIN95_CLASSES
        };
        NativeMethods.InitCommonControlsEx(ref controls);
        _tooltipWindow = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_TOPMOST,
            NativeMethods.TOOLTIPS_CLASS,
            string.Empty,
            NativeMethods.WS_POPUP | NativeMethods.TTS_ALWAYSTIP | NativeMethods.TTS_NOPREFIX,
            0,
            0,
            0,
            0,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
    }

    private void AddParserInputTooltip(SearchLevelState level)
    {
        SetParserInputTooltip(level, NativeMethods.TTM_ADDTOOLW);
    }

    private void UpdateParserInputTooltip(SearchLevelState level)
    {
        SetParserInputTooltip(level, NativeMethods.TTM_UPDATETIPTEXTW);
    }

    private void SetParserInputTooltip(SearchLevelState level, uint message)
    {
        if (_tooltipWindow == IntPtr.Zero || level.ParserInputVisibilityButton == IntPtr.Zero)
        {
            return;
        }

        IntPtr nextText = Marshal.StringToHGlobalUni(
            level.ShowParserInput ? "Hide input pane" : "Show input pane");
        NativeMethods.TOOLINFOW toolInfo = CreateParserInputToolInfo(level, nextText);
        IntPtr toolInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.TOOLINFOW>());
        try
        {
            Marshal.StructureToPtr(toolInfo, toolInfoPointer, fDeleteOld: false);
            NativeMethods.SendMessageW(_tooltipWindow, message, IntPtr.Zero, toolInfoPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(toolInfoPointer);
        }

        if (level.ParserInputTooltipText != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(level.ParserInputTooltipText);
        }

        level.ParserInputTooltipText = nextText;
    }

    private void RemoveParserInputTooltip(SearchLevelState level)
    {
        if (_tooltipWindow != IntPtr.Zero && level.ParserInputVisibilityButton != IntPtr.Zero)
        {
            NativeMethods.TOOLINFOW toolInfo = CreateParserInputToolInfo(
                level,
                level.ParserInputTooltipText);
            IntPtr toolInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.TOOLINFOW>());
            try
            {
                Marshal.StructureToPtr(toolInfo, toolInfoPointer, fDeleteOld: false);
                NativeMethods.SendMessageW(
                    _tooltipWindow,
                    NativeMethods.TTM_DELTOOLW,
                    IntPtr.Zero,
                    toolInfoPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(toolInfoPointer);
            }
        }

        if (level.ParserInputTooltipText != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(level.ParserInputTooltipText);
            level.ParserInputTooltipText = IntPtr.Zero;
        }
    }

    private NativeMethods.TOOLINFOW CreateParserInputToolInfo(SearchLevelState level, IntPtr text) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.TOOLINFOW>(),
        uFlags = NativeMethods.TTF_IDISHWND | NativeMethods.TTF_SUBCLASS,
        hwnd = _hwnd,
        uId = (nuint)level.ParserInputVisibilityButton,
        lpszText = text
    };

    private void ReloadHighlightRules()
    {
        _highlightRules = HighlightRuleStore.Load();
        _compiledHighlightRules = HighlightRuleCompiler.Compile(_highlightRules);
        ApplyHighlightRulesToPanes();
    }

    private void ApplyHighlightRulesToPanes()
    {
        _mainPane?.SetHighlightRules(_compiledHighlightRules);
        for (int i = 0; i < _searchLevels.Count; i++)
        {
            _searchLevels[i].ResultsPane?.SetHighlightRules(_compiledHighlightRules);
        }
    }

    private void OpenHighlightRuleManager()
    {
        if (_configurationWindowOpen)
        {
            return;
        }

        HighlightRuleManagerWindow manager = new(_highlightRules, ApplyHighlightRulePreview);
        bool saved;
        _configurationWindowOpen = true;
        try
        {
            saved = manager.ShowModal(_hwnd);
        }
        finally
        {
            _configurationWindowOpen = false;
        }

        if (saved)
        {
            ReloadHighlightRules();
            return;
        }

        _compiledHighlightRules = HighlightRuleCompiler.Compile(_highlightRules);
        ApplyHighlightRulesToPanes();
    }

    private void ApplyHighlightRulePreview(IReadOnlyList<HighlightRule> rules)
    {
        _compiledHighlightRules = HighlightRuleCompiler.Compile(rules);
        ApplyHighlightRulesToPanes();
    }

    private void ReloadParserRules(string? selectRuleName)
    {
        if (_parserCombo == IntPtr.Zero)
        {
            return;
        }

        _parserRules = DisplayParserRuleStore.Load();
        _updatingParserCombo = true;
        NativeMethods.SendMessageW(_parserCombo, NativeMethods.CB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.SendMessageW(_parserCombo, NativeMethods.CB_ADDSTRING, IntPtr.Zero, NoParserText);
        for (int i = 0; i < _parserRules.Count; i++)
        {
            NativeMethods.SendMessageW(_parserCombo, NativeMethods.CB_ADDSTRING, IntPtr.Zero, _parserRules[i].Name);
        }

        NativeMethods.SendMessageW(_parserCombo, NativeMethods.CB_ADDSTRING, IntPtr.Zero, ConfigureParserText);
        _selectedParserRuleIndex = FindParserRuleIndex(selectRuleName);
        SetParserComboSelection(_selectedParserRuleIndex);
        _updatingParserCombo = false;
    }

    private void SetParserComboSelection(int ruleIndex)
    {
        int comboIndex = ruleIndex >= 0 ? ruleIndex + 1 : 0;
        SetParserComboIndex(comboIndex);
    }

    private void SetParserComboIndex(int comboIndex)
    {
        NativeMethods.SendMessageW(_parserCombo, NativeMethods.CB_SETCURSEL, new IntPtr(comboIndex), IntPtr.Zero);
    }

    private int FindParserRuleIndex(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        for (int i = 0; i < _parserRules.Count; i++)
        {
            if (string.Equals(_parserRules[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private DisplayParserRule? GetSelectedParserRule()
    {
        return _selectedParserRuleIndex >= 0 && _selectedParserRuleIndex < _parserRules.Count
            ? _parserRules[_selectedParserRuleIndex].Clone()
            : null;
    }

    private DisplayParserRule? GetEffectiveParserRule()
    {
        return _parserPreviewActive
            ? _previewParserRule?.Clone()
            : GetSelectedParserRule();
    }

    private DisplayParserRule? GetEffectiveMainParserRule() =>
        DisplayParserEvaluator.CreateRuleBeforeFirstFilter(GetEffectiveParserRule());

    private void OnParserSelectionChanged()
    {
        int selected = NativeMethods.SendMessageW(_parserCombo, NativeMethods.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
        int configureIndex = _parserRules.Count + 1;
        if (selected == configureIndex)
        {
            if (_configurationWindowOpen)
            {
                SetParserComboSelection(_selectedParserRuleIndex);
                return;
            }

            OpenRuleManager();
            return;
        }

        _selectedParserRuleIndex = selected > 0 && selected <= _parserRules.Count ? selected - 1 : -1;
        _resetParserInputVisibilityOnNextApply = true;
        ApplyParserRuleChange();
    }

    private void OpenRuleManager()
    {
        if (_configurationWindowOpen)
        {
            return;
        }

        string? activeRuleName = GetSelectedParserRule()?.Name;
        RuleManagerWindow manager = new(
            _parserRules,
            activeRuleName,
            CreateDefaultParserRuleSample(),
            ApplyParserPreview,
            ResetParserInputVisibilityForRuleSelection);
        string? selectedRuleName;
        SetParserComboIndex(_parserRules.Count + 1);
        NativeMethods.EnableWindow(_parserCombo, false);
        NativeMethods.EnableWindow(_highlightingButton, false);
        _configurationWindowOpen = true;
        try
        {
            selectedRuleName = manager.ShowModal(_hwnd);
        }
        finally
        {
            _configurationWindowOpen = false;
            NativeMethods.EnableWindow(_parserCombo, true);
            NativeMethods.EnableWindow(_highlightingButton, true);
            _parserPreviewActive = false;
            _previewParserRule = null;
        }

        if (!string.Equals(activeRuleName, selectedRuleName, StringComparison.OrdinalIgnoreCase))
        {
            _resetParserInputVisibilityOnNextApply = true;
        }

        ReloadParserRules(selectedRuleName);
        ApplyParserRuleChange();
    }

    private void ApplyParserPreview(DisplayParserRule? rule)
    {
        DisplayParserRule? validRule = null;
        if (rule is not null)
        {
            try
            {
                DisplayParserEvaluator.ValidateRule(rule);
                validRule = rule.Clone();
            }
            catch (ArgumentException)
            {
                validRule = null;
            }
        }

        _parserPreviewActive = true;
        _previewParserRule = validRule;
        ApplyParserRuleChange();
    }

    private void ResetParserInputVisibilityForRuleSelection()
    {
        _resetParserInputVisibilityOnNextApply = true;
    }

    private bool OnKeyDown(int key)
    {
        if (key == NativeMethods.VK_O && IsControlKeyDown())
        {
            OpenFileInCurrentWindow();
            return true;
        }

        if (key == NativeMethods.VK_V && IsControlKeyDown())
        {
            OpenClipboardText();
            return true;
        }

        return false;
    }

    private bool TryHandleGlobalShortcut(NativeMethods.MSG message)
    {
        if (message.message != NativeMethods.WM_KEYDOWN ||
            message.wParam.ToInt32() != NativeMethods.VK_S ||
            !IsControlKeyDown())
        {
            return false;
        }

        SaveCurrentOutput();
        return true;
    }

    private void SaveCurrentOutput()
    {
        if (_detectedEncoding is null || _mainPane?.Reader is not ProjectedViewport mainViewport)
        {
            ShowSaveError("Wait for the file to finish loading before saving.");
            return;
        }

        bool saveSearch = HasActiveSearch;
        ILogRecordSource source;
        string defaultExtension;
        string defaultName;
        string dialogTitle;
        string filter;
        if (saveSearch)
        {
            if (IsSearchSaveBlocked())
            {
                ShowSaveError("Wait for the search to finish before saving its results.");
                return;
            }

            int resultIndex = GetActiveSearchResultCount() - 1;
            if (resultIndex < 0 ||
                _searchLevels[resultIndex].ResultsPane?.Reader is not ProjectedViewport { Source: FilteredLogRecordSource filteredSource })
            {
                ShowSaveError("Search results are not ready to be saved.");
                return;
            }

            source = filteredSource;
            defaultExtension = "tsv";
            defaultName = GetOutputBaseName() + "-search-results.tsv";
            dialogTitle = "Save search results";
            filter = "Tab-separated values (*.tsv)\0*.tsv\0All files\0*.*\0\0";
        }
        else
        {
            source = mainViewport.Source;
            defaultExtension = "log";
            defaultName = GetOutputBaseName() + "-parsed.log";
            dialogTitle = "Save parsed log";
            filter = "Log files (*.log)\0*.log\0All files\0*.*\0\0";
        }

        if (!TryShowSaveOutputDialog(filter, dialogTitle, defaultExtension, defaultName, out string? outputPath) ||
            outputPath is null)
        {
            return;
        }

        try
        {
            using ILogRecordSource snapshot = source.CloneForWorker();
            if (saveSearch)
            {
                LogOutputExporter.SaveSearchResults(snapshot, outputPath);
            }
            else
            {
                LogOutputExporter.SaveParsedLog(snapshot, outputPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowSaveError("Failed to save output: " + ex.Message);
        }
    }

    private bool IsSearchSaveBlocked() =>
        _searchInProgress ||
        _appendSearchPending ||
        _appendSearchInProgress ||
        _waitingForPausedSearchRequestId != 0 ||
        _searchStale ||
        string.Equals(_searchErrorText, "Search stale", StringComparison.Ordinal);

    private string GetOutputBaseName()
    {
        string value = _contentSource.FilePath is string path
            ? Path.GetFileNameWithoutExtension(path)
            : _contentSource.DisplayName;
        char[] invalid = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            sanitized.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i]);
        }

        string result = sanitized.ToString().Trim().TrimEnd('.');
        return result.Length > 0 ? result : "logblade";
    }

    private bool TryShowSaveOutputDialog(
        string filter,
        string title,
        string defaultExtension,
        string defaultName,
        out string? selectedPath)
    {
        selectedPath = null;
        const int fileBufferChars = 32768;
        IntPtr filterBuffer = IntPtr.Zero;
        IntPtr fileBuffer = IntPtr.Zero;
        IntPtr titleBuffer = IntPtr.Zero;
        IntPtr defaultExtensionBuffer = IntPtr.Zero;
        try
        {
            filterBuffer = Marshal.StringToHGlobalUni(filter);
            titleBuffer = Marshal.StringToHGlobalUni(title);
            defaultExtensionBuffer = Marshal.StringToHGlobalUni(defaultExtension);
            fileBuffer = Marshal.AllocHGlobal(fileBufferChars * sizeof(char));
            Marshal.Copy(new byte[fileBufferChars * sizeof(char)], 0, fileBuffer, fileBufferChars * sizeof(char));
            char[] defaultNameChars = defaultName.ToCharArray();
            Marshal.Copy(defaultNameChars, 0, fileBuffer, defaultNameChars.Length);

            NativeMethods.OPENFILENAMEW ofn = new()
            {
                lStructSize = Marshal.SizeOf<NativeMethods.OPENFILENAMEW>(),
                hwndOwner = _hwnd,
                lpstrFilter = filterBuffer,
                lpstrFile = fileBuffer,
                nMaxFile = fileBufferChars,
                lpstrTitle = titleBuffer,
                lpstrDefExt = defaultExtensionBuffer,
                Flags = NativeMethods.OFN_EXPLORER | NativeMethods.OFN_PATHMUSTEXIST |
                    NativeMethods.OFN_NOCHANGEDIR | NativeMethods.OFN_OVERWRITEPROMPT
            };

            if (!NativeMethods.GetSaveFileNameW(ref ofn))
            {
                int dialogError = NativeMethods.CommDlgExtendedError();
                if (dialogError != 0)
                {
                    ShowSaveError("Failed to open save dialog. Error: 0x" + dialogError.ToString("X"));
                }

                return false;
            }

            selectedPath = Marshal.PtrToStringUni(fileBuffer);
            return !string.IsNullOrEmpty(selectedPath);
        }
        finally
        {
            if (filterBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterBuffer);
            }

            if (fileBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileBuffer);
            }

            if (titleBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(titleBuffer);
            }

            if (defaultExtensionBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(defaultExtensionBuffer);
            }
        }
    }

    private void ShowSaveError(string message) =>
        NativeMethods.MessageBoxW(_hwnd, message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);

    private void OpenFileInCurrentWindow()
    {
        if (!TryShowOpenFileDialog(out string? selectedPath) || selectedPath is null)
        {
            return;
        }

        try
        {
            LoadContentSource(LogContentSource.FromFile(selectedPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            NativeMethods.MessageBoxW(_hwnd, "Failed to open file: " + ex.Message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
        }
    }

    private bool TryShowOpenFileDialog(out string? selectedPath)
    {
        selectedPath = null;
        const int fileBufferChars = 32768;
        IntPtr filterBuffer = IntPtr.Zero;
        IntPtr fileBuffer = IntPtr.Zero;
        IntPtr titleBuffer = IntPtr.Zero;
        try
        {
            filterBuffer = Marshal.StringToHGlobalUni("Log files\0*.log;*.txt;*.*\0All files\0*.*\0\0");
            titleBuffer = Marshal.StringToHGlobalUni("Open log file");
            fileBuffer = Marshal.AllocHGlobal(fileBufferChars * sizeof(char));
            Marshal.Copy(new byte[fileBufferChars * sizeof(char)], 0, fileBuffer, fileBufferChars * sizeof(char));

            NativeMethods.OPENFILENAMEW ofn = new()
            {
                lStructSize = Marshal.SizeOf<NativeMethods.OPENFILENAMEW>(),
                hwndOwner = _hwnd,
                lpstrFilter = filterBuffer,
                lpstrFile = fileBuffer,
                nMaxFile = fileBufferChars,
                lpstrTitle = titleBuffer,
                Flags = NativeMethods.OFN_EXPLORER | NativeMethods.OFN_FILEMUSTEXIST | NativeMethods.OFN_PATHMUSTEXIST | NativeMethods.OFN_NOCHANGEDIR
            };

            if (!NativeMethods.GetOpenFileNameW(ref ofn))
            {
                int dialogError = NativeMethods.CommDlgExtendedError();
                if (dialogError != 0)
                {
                    AppLog.Instance.Error(
                        "file.dialog.failed",
                        "failed",
                        new LogField("error", "0x" + dialogError.ToString("X")));
                    NativeMethods.MessageBoxW(_hwnd, "Failed to open file dialog. Error: 0x" + dialogError.ToString("X"), Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
                }

                return false;
            }

            selectedPath = Marshal.PtrToStringUni(fileBuffer);
            return !string.IsNullOrEmpty(selectedPath);
        }
        finally
        {
            if (filterBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterBuffer);
            }

            if (fileBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileBuffer);
            }

            if (titleBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(titleBuffer);
            }
        }
    }

    private void OpenClipboardText()
    {
        if (!TryGetClipboardText(out string text) || text.Length == 0)
        {
            NativeMethods.MessageBoxW(_hwnd, "Clipboard does not contain text.", Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            return;
        }

        if (ShouldLoadPastedTextInCurrentWindow())
        {
            LoadContentSource(LogContentSource.FromMemory("Pasted text", CreatePastedTextBuffer(text)));
            return;
        }

        OpenClipboardTextInNewWindow(text);
    }

    private bool ShouldLoadPastedTextInCurrentWindow()
        => !_contentSource.IsFile &&
            string.Equals(_contentSource.DisplayName, "Untitled", StringComparison.Ordinal) &&
            _contentSource.Length == 0;

    private static byte[] CreatePastedTextBuffer(string text)
    {
        byte[] preamble = Encoding.UTF8.GetPreamble();
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] content = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, content, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, content, preamble.Length, body.Length);
        return content;
    }

    private void OpenClipboardTextInNewWindow(string text)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            NativeMethods.MessageBoxW(_hwnd, "Current executable path is not available.", Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            return;
        }

        IntPtr hwnd = _hwnd;
        CancellationToken cancellationToken = _pasteTransferCancellation.Token;
        _ = Task.Run(async () =>
        {
            PastedTextLaunchResult result = await PastedTextWindowLauncher.LaunchAsync(
                executable,
                Program.PastedStdinArgument,
                text,
                cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                AppLog.Instance.Info(
                    "paste.open_window",
                    "opened",
                    new LogField("storage", "memory"),
                    new LogField("characterCount", text.Length.ToString()),
                    new LogField("pid", result.ProcessId.ToString()));
                return;
            }

            if (result.Cancelled)
            {
                AppLog.Instance.Info(
                    "paste.open_window",
                    "cancelled",
                    new LogField("characterCount", text.Length.ToString()),
                    new LogField("pid", result.ProcessId.ToString()));
                return;
            }

            AppLog.Instance.Error(
                "paste.open_window.failed",
                "failed",
                new LogField("type", result.ErrorType ?? "unknown"),
                new LogField("message", result.ErrorMessage ?? "unknown error"));
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            GCHandle handle = GCHandle.Alloc(result);
            if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_PASTE_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
            {
                handle.Free();
            }
        });
    }

    private bool TryGetClipboardText(out string text)
    {
        return ClipboardText.TryGetText(_hwnd, out text);
    }

    private void OnPastedTextLaunchComplete(IntPtr lParam)
    {
        GCHandle handle = GCHandle.FromIntPtr(lParam);
        object? target = handle.Target;
        handle.Free();
        if (_closing || target is not PastedTextLaunchResult failure || failure.Success || failure.Cancelled)
        {
            return;
        }

        NativeMethods.MessageBoxW(
            _hwnd,
            "Failed to open pasted text: " + (failure.ErrorMessage ?? "unknown error"),
            Program.AppTitle,
            NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
    }

    private string CreateDefaultParserRuleSample()
    {
        if (_detectedEncoding is not DetectedEncodingInfo detected)
        {
            return string.Empty;
        }

        try
        {
            using Stream fs = _contentSource.OpenRead();
            if (detected.DataOffset > 0)
            {
                fs.Seek(Math.Min(detected.DataOffset, fs.Length), SeekOrigin.Begin);
            }

            using StreamReader reader = new(fs, detected.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16);
            List<string> lines = new(capacity: 3);
            while (lines.Count < 3)
            {
                string? line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                lines.Add(line);
            }

            return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            return string.Empty;
        }
    }

    private void ApplyParserRuleChange()
    {
        DisplayParserRule? parserRule = GetEffectiveParserRule();
        if (parserRule is not null)
        {
            try
            {
                DisplayParserEvaluator.ValidateRule(parserRule);
            }
            catch (ArgumentException)
            {
                parserRule = null;
            }
        }

        if (!ReloadMainReaderWithSelectedParser())
        {
            return;
        }

        ApplyParserFilterSearchLevels(parserRule);
        RestartSearchAfterInputChange(dispatchImmediately: true);
    }

    private bool ReloadMainReaderWithSelectedParser()
    {
        if (_mainPane is null || _mainPane.Reader is null || _detectedEncoding is not DetectedEncodingInfo detected)
        {
            return true;
        }

        double scrollPercentage = _mainPane.Reader.ScrollPercentage;
        int visibleLines = _mainPane.VisibleLineCount;
        try
        {
            DisplayParserRule? parserRule = GetEffectiveMainParserRule();
            if (parserRule is not null)
            {
                DisplayParserEvaluator.ValidateRule(parserRule);
            }

            LogRecordSource source = new(_contentSource, detected.Encoding, detected.DataOffset, parserRule);
            var reader = new ProjectedViewport(source, wrapLongLines: true);
            reader.ReadFromPercentage(scrollPercentage, visibleLines);
            _mainPane.SetReader(reader, visibleLines, preserveSelection: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            NativeMethods.MessageBoxW(_hwnd, "Failed to apply parser: " + ex.Message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            return false;
        }
    }

    private SearchLevelState CreateSearchLevel(IntPtr hInstance)
    {
        var level = new SearchLevelState();
        level.ParserInputVisibilityButton = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            ParserInputVisibilityGlyph,
            NativeMethods.WS_CHILD | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.BS_PUSHLIKE,
            0,
            0,
            1,
            1,
            _hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
        if (level.ParserInputVisibilityButton == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for parser input visibility button.");
        }

        NativeMethods.SendMessageW(
            level.ParserInputVisibilityButton,
            NativeMethods.WM_SETFONT,
            _parserInputVisibilityFont,
            new IntPtr(1));
        AddParserInputTooltip(level);

        level.SearchEdit = NativeMethods.CreateWindowExW(
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

        if (level.SearchEdit == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for search edit.");
        }

        NativeMethods.SendMessageW(level.SearchEdit, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        level.OriginalSearchEditProc = NativeMethods.SetWindowLongPtrW(
            level.SearchEdit,
            NativeMethods.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(s_searchEditProc));

        level.RegexCheckbox = NativeMethods.CreateWindowExW(
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

        if (level.RegexCheckbox == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for regex checkbox.");
        }

        level.IgnoreCaseCheckbox = NativeMethods.CreateWindowExW(
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

        if (level.IgnoreCaseCheckbox == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for ignore-case checkbox.");
        }

        level.InvertMatchCheckbox = NativeMethods.CreateWindowExW(
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

        if (level.InvertMatchCheckbox == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for invert-match checkbox.");
        }

        NativeMethods.SendMessageW(level.RegexCheckbox, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(level.IgnoreCaseCheckbox, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(level.InvertMatchCheckbox, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        NativeMethods.SendMessageW(level.RegexCheckbox, NativeMethods.BM_SETCHECK, new IntPtr(NativeMethods.BST_CHECKED), IntPtr.Zero);
        NativeMethods.SendMessageW(level.IgnoreCaseCheckbox, NativeMethods.BM_SETCHECK, new IntPtr(NativeMethods.BST_UNCHECKED), IntPtr.Zero);
        NativeMethods.SendMessageW(level.InvertMatchCheckbox, NativeMethods.BM_SETCHECK, new IntPtr(NativeMethods.BST_UNCHECKED), IntPtr.Zero);
        level.ResultsPane = new ViewportPaneWindow(
            _font,
            _lineHeight,
            _charWidth,
            boldFont: _boldFont,
            italicFont: _italicFont,
            boldItalicFont: _boldItalicFont,
            onStale: OnFilteredPaneStale,
            onRowActivated: OnFilteredRowActivated,
            onPasteRequested: OpenClipboardText,
            onHostVerticalResizeHit: OnResultsPaneResizeHit,
            onHostVerticalResizeBegin: OnResultsPaneResizeBegin);
        level.ResultsPane.Create(_hwnd, hInstance);
        level.ResultsPane.SetHighlightRules(_compiledHighlightRules);
        level.ResultsPane.SetStatus(string.Empty);
        _searchLevels.Add(level);
        return level;
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

    private void LoadContentSource(LogContentSource contentSource)
    {
        _contentSource = contentSource;
        _knownContentFileSize = contentSource.IsFile ? 0 : contentSource.Length;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowTextW(_hwnd, ComposeWindowTitle());
        }

        BeginBackgroundOpen();
    }

    private void BeginBackgroundOpen()
    {
        if (_closing || _hwnd == IntPtr.Zero || _mainPane is null)
        {
            return;
        }

        long openRequestId = ++_nextOpenRequestId;
        _latestOpenRequestId = openRequestId;
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
        _searchStale = false;
        ClearPausedSearchCheckpoint();
        ClearPendingLowerSearchChange();
        _mainPane.SetStatus("Loading file...");
        SetActiveSearchResultStatus(string.Empty);
        InvalidateHost();

        if (!_contentSource.Exists)
        {
            AppLog.Instance.Info("file.open.begin", "begin", new LogField("source", _contentSource.DisplayName));
            AppLog.Instance.Error(
                "file.open.failed",
                "failed",
                new LogField("source", _contentSource.DisplayName),
                new LogField("stage", "missing_file"),
                new LogField("reason", "Source not found"));

            _mainPane.SetStatus("File not found.");
            NativeMethods.MessageBoxW(_hwnd, "Source not found: " + _contentSource.DisplayName, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            return;
        }

        LogContentSource workerSource = _contentSource;
        IntPtr hwnd = _hwnd;
        int visibleLines = _mainPane.VisibleLineCount;
        DisplayParserRule? workerParserRule = GetEffectiveMainParserRule();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = new OpenWorkerResult();
            result.RequestId = openRequestId;
            try
            {
                AppLog.Instance.Info("file.open.begin", "begin", new LogField("source", workerSource.DisplayName));
                result.DetectedEncoding = LogEncodingDetector.DetectEncoding(workerSource);
                result.Reader = new LogRecordSource(workerSource, result.DetectedEncoding.Value.Encoding, result.DetectedEncoding.Value.DataOffset, workerParserRule);
                result.Reader.ReadFromPercentage(0d, visibleLines + 1);
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

        if (result.RequestId != _latestOpenRequestId)
        {
            result.Reader?.Dispose();
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
            RememberObservedContentFileSize(result.Reader.ConfirmedFileSize);
            var projected = new ProjectedViewport(result.Reader, wrapLongLines: true);
            projected.UseCurrentSourceRecords(result.PreloadedVisibleLines);
            _mainPane.SetReader(projected, result.PreloadedVisibleLines);
            StartFileWatcher();
            if (result.DetectedEncoding is DetectedEncodingInfo detected)
            {
                AppLog.Instance.Info(
                    "encoding.detected",
                    "detected",
                    new LogField("source", result.Reader.SourceName),
                    new LogField("encoding", result.Reader.EncodingName),
                    new LogField("dataOffset", detected.DataOffset.ToString()));
            }

            if (HasActiveSearch)
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

        if (_contentSource.FilePath is not string path)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
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
                new LogField("path", path),
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
        QueueSearchReloadAfterSameSizeChange();
    }

    private void OnSize(int sizeType)
    {
        if (sizeType == NativeMethods.SIZE_MINIMIZED)
        {
            AuxiliaryWindowRegistry.SetMainWindowMinimized(minimized: true);
            return;
        }

        if (sizeType == NativeMethods.SIZE_RESTORED || sizeType == NativeMethods.SIZE_MAXIMIZED)
        {
            AuxiliaryWindowRegistry.SetMainWindowMinimized(minimized: false);
        }

        UpdateLayout();
    }

    private void SaveWindowState()
    {
        if (_windowStateSaved || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _windowStateSaved = true;
        NativeMethods.WINDOWPLACEMENT placement = new()
        {
            length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
        };

        if (!NativeMethods.GetWindowPlacement(_hwnd, ref placement))
        {
            return;
        }

        NativeMethods.RECT rect = placement.rcNormalPosition;
        WindowStateSettings settings = new()
        {
            Left = rect.left,
            Top = rect.top,
            Width = rect.right - rect.left,
            Height = rect.bottom - rect.top,
            WindowState = placement.showCmd == NativeMethods.SW_SHOWMAXIMIZED
                ? WindowStateStore.MaximizedState
                : WindowStateStore.NormalState
        };

        if (!WindowStateStore.IsValidForRestore(settings, _ => true))
        {
            return;
        }

        try
        {
            WindowStateStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Instance.Error(
                "window.state.save_failed",
                "failed",
                new LogField("path", WindowStateStore.StorePath),
                new LogField("type", ex.GetType().FullName ?? ex.GetType().Name),
                new LogField("reason", ex.Message));
        }
    }

    private bool OnSetCursor()
    {
        if (_isSearchAreaResizing || _isSearchResultsResizing || IsSearchResizeHit(GetClientCursorY()))
        {
            NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
            return true;
        }

        return false;
    }

    private bool OnLButtonDown(IntPtr lParam)
    {
        int y = NativeMethods.HighWord(lParam);
        if (!TryGetSearchResizeHit(y, out bool resizeSearchArea, out int index))
        {
            return false;
        }

        BeginSearchResize(resizeSearchArea, index, y, _hwnd);
        return true;
    }

    private void BeginSearchResultsResizeFromGrip(IntPtr lParam)
    {
        int y = GetGripMouseYInHost(lParam);
        if (TryGetSearchResizeHit(y, out bool resizeSearchArea, out int index))
        {
            BeginSearchResize(resizeSearchArea, index, y, _searchResizeGrip);
        }
    }

    private void BeginSearchResize(bool resizeSearchArea, int index, int y, IntPtr captureHwnd)
    {
        if (resizeSearchArea)
        {
            BeginSearchAreaResize(y, captureHwnd);
            return;
        }

        BeginSearchResultsResize(index, y, captureHwnd);
    }

    private void BeginSearchAreaResize(int y, IntPtr captureHwnd)
    {
        if (!HasActiveSearch)
        {
            return;
        }

        _isSearchAreaResizing = true;
        _searchAreaResizeStartY = y;
        _searchAreaResizeStartHeight = GetRectHeight(_layout.SearchAreaRect);
        NativeMethods.SetCapture(captureHwnd);
        NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
    }

    private void BeginSearchResultsResize(int index, int y, IntPtr captureHwnd)
    {
        if (GetVisibleActiveSearchResultCount() <= 1)
        {
            BeginSearchAreaResize(y, captureHwnd);
            return;
        }

        if (index < 0 || index >= _activeSearchLevelCount || index >= _searchLevels.Count)
        {
            return;
        }

        _isSearchResultsResizing = true;
        _resizingSearchResultIndex = index;
        _searchResultResizeStartY = y;
        _searchResultResizeAvailableHeight = GetAvailableSearchResultHeight(GetRectHeight(_layout.SearchAreaRect));
        _searchResultResizeStartRatios = GetNormalizedVisibleSearchResultRatios();
        _searchResultResizeStartRatio = index < _searchResultResizeStartRatios.Length
            ? _searchResultResizeStartRatios[index]
            : 0d;
        NativeMethods.SetCapture(captureHwnd);
        NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
    }

    private void OnMouseMove(IntPtr lParam)
    {
        if (_isSearchAreaResizing)
        {
            ResizeSearchArea(NativeMethods.HighWord(lParam));
            return;
        }

        if (!_isSearchResultsResizing)
        {
            return;
        }

        ResizeSearchResults(NativeMethods.HighWord(lParam));
    }

    private void UpdateSearchResultsResizeFromGrip(IntPtr lParam)
    {
        if (_isSearchAreaResizing)
        {
            ResizeSearchArea(GetGripMouseYInHost(lParam));
            return;
        }

        if (!_isSearchResultsResizing)
        {
            return;
        }

        ResizeSearchResults(GetGripMouseYInHost(lParam));
    }

    private void ResizeSearchArea(int y)
    {
        if (!HasActiveSearch)
        {
            return;
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        int clientHeight = GetRectHeight(clientRect);
        int requestedHeight = _searchAreaResizeStartHeight + (_searchAreaResizeStartY - y);
        int searchAreaHeight = ClampSearchAreaHeight(requestedHeight, clientHeight);
        _customSearchAreaRatio = clientHeight > 0
            ? Math.Clamp(searchAreaHeight / (double)clientHeight, 0d, 1d)
            : GetDefaultSearchAreaRatio(GetActiveSearchResultCount());
        UpdateLayout(LayoutRedrawMode.Immediate);
    }

    private void ResizeSearchResults(int y)
    {
        if (_resizingSearchResultIndex < 0 ||
            _resizingSearchResultIndex >= _activeSearchLevelCount ||
            _resizingSearchResultIndex >= _searchLevels.Count)
        {
            return;
        }

        if (_searchResultResizeStartRatios is null)
        {
            return;
        }

        double deltaRatio = _searchResultResizeAvailableHeight > 0
            ? (_searchResultResizeStartY - y) / (double)_searchResultResizeAvailableHeight
            : 0d;
        ApplySearchResultRatioResize(
            _resizingSearchResultIndex,
            _searchResultResizeStartRatio + deltaRatio,
            _searchResultResizeStartRatios);
        UpdateLayout(LayoutRedrawMode.Immediate);
    }

    private void OnLButtonUp()
    {
        EndSearchResultsResize();
    }

    private void EndSearchResultsResize()
    {
        if (!_isSearchAreaResizing && !_isSearchResultsResizing)
        {
            return;
        }

        _isSearchAreaResizing = false;
        _searchAreaResizeStartY = 0;
        _searchAreaResizeStartHeight = 0;
        _isSearchResultsResizing = false;
        _resizingSearchResultIndex = -1;
        _searchResultResizeAvailableHeight = 0;
        _searchResultResizeStartRatio = 0d;
        _searchResultResizeStartRatios = null;
        NativeMethods.ReleaseCapture();
    }

    private int GetGripMouseYInHost(IntPtr lParam) =>
        NativeMethods.HighWord(lParam) + GetSearchResizeGripTop();

    private int GetSearchResizeGripTop() =>
        Math.Max(_layout.ClientRect.top, _layout.SearchAreaRect.top - SearchResizeHitSlopPx);

    private bool IsSearchResizeHit(int y)
        => TryGetSearchResizeHit(y, out _, out _);

    private bool TryGetSearchResizeHit(int y, out bool resizeSearchArea, out int index)
    {
        resizeSearchArea = false;
        index = -1;
        if (TryGetSearchAreaResizeHit(y))
        {
            resizeSearchArea = true;
            return true;
        }

        if (!TryGetSearchResultResizeHit(y, out index))
        {
            return false;
        }

        resizeSearchArea = GetVisibleActiveSearchResultCount() <= 1;
        return true;
    }

    private bool TryGetSearchAreaResizeHit(int y)
    {
        if (!HasActiveSearch || IsZeroRect(_layout.SearchAreaRect))
        {
            return false;
        }

        return Math.Abs(y - _layout.SearchAreaRect.top) <= SearchResizeHitSlopPx;
    }

    private bool TryGetSearchResultResizeHit(int y, out int index)
    {
        index = -1;
        if (!HasActiveSearch || _layout.SearchLevelLayouts is null)
        {
            return false;
        }

        int count = Math.Min(_activeSearchLevelCount, _layout.SearchLevelLayouts.Length);
        for (int i = 0; i < count; i++)
        {
            NativeMethods.RECT rect = _layout.SearchLevelLayouts[i].SearchResultsRect;
            if (!IsZeroRect(rect) && Math.Abs(y - rect.top) <= SearchResizeHitSlopPx)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private int GetClientCursorY()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT point))
        {
            return int.MinValue;
        }

        return NativeMethods.ScreenToClient(_hwnd, ref point) ? point.y : int.MinValue;
    }

    private bool HasActiveSearch => _activeSearchLevelCount > 0;

    private int GetActiveSearchResultCount() =>
        Math.Min(_activeSearchLevelCount, _searchLevels.Count);

    internal static int GetParserInputIndexForSearchResult(int resultIndex, int parserFilterLevelCount) =>
        resultIndex >= 0 && resultIndex < parserFilterLevelCount - 1
            ? resultIndex + 1
            : -1;

    internal static bool ShouldShowMainPaneForParserInputs(
        int parserFilterLevelCount,
        bool firstParserInputVisible) =>
        parserFilterLevelCount <= 0 || firstParserInputVisible;

    private bool IsParserInputVisible(int inputIndex) =>
        inputIndex >= 0 &&
        inputIndex < _parserFilterLevelCount &&
        inputIndex < _parserInputVisibility.Count &&
        _parserInputVisibility[inputIndex];

    private bool IsSearchResultVisible(int resultIndex)
    {
        int inputIndex = GetParserInputIndexForSearchResult(resultIndex, _parserFilterLevelCount);
        return inputIndex < 0 || IsParserInputVisible(inputIndex);
    }

    private int[] GetVisibleActiveSearchResultIndices()
    {
        int activeCount = GetActiveSearchResultCount();
        List<int> indices = new(activeCount);
        for (int i = 0; i < activeCount; i++)
        {
            if (IsSearchResultVisible(i))
            {
                indices.Add(i);
            }
        }

        return indices.ToArray();
    }

    private int GetVisibleActiveSearchResultCount()
    {
        int activeCount = GetActiveSearchResultCount();
        int visibleCount = 0;
        for (int i = 0; i < activeCount; i++)
        {
            if (IsSearchResultVisible(i))
            {
                visibleCount++;
            }
        }

        return visibleCount;
    }

    private SearchLevelState? FindSearchLevelByEdit(IntPtr hwnd)
    {
        foreach (SearchLevelState level in _searchLevels)
        {
            if (level.SearchEdit == hwnd)
            {
                return level;
            }
        }

        return null;
    }

    private SearchLevelState? FindSearchLevelByOptionControl(IntPtr hwnd)
    {
        foreach (SearchLevelState level in _searchLevels)
        {
            if (level.RegexCheckbox == hwnd ||
                level.IgnoreCaseCheckbox == hwnd ||
                level.InvertMatchCheckbox == hwnd)
            {
                return level;
            }
        }

        return null;
    }

    private SearchLevelState? FindSearchLevelByParserInputVisibilityButton(IntPtr hwnd)
    {
        foreach (SearchLevelState level in _searchLevels)
        {
            if (level.ParserInputVisibilityButton == hwnd)
            {
                return level;
            }
        }

        return null;
    }

    private SearchLevelState? FindSearchLevelByResultsPane(ViewportPaneWindow pane)
    {
        int index = FindSearchLevelIndexByResultsPane(pane);
        return index >= 0 ? _searchLevels[index] : null;
    }

    private int FindSearchLevelIndexByResultsPane(ViewportPaneWindow pane)
    {
        for (int i = 0; i < _searchLevels.Count; i++)
        {
            if (ReferenceEquals(_searchLevels[i].ResultsPane, pane))
            {
                return i;
            }
        }

        return -1;
    }

    private bool OnMainPaneResizeHit(ViewportPaneWindow pane, int paneY)
        => TryGetMainPaneResizeHit(pane, paneY, out _);

    private bool OnMainPaneResizeBegin(ViewportPaneWindow pane, int paneY)
    {
        if (!TryGetMainPaneResizeHit(pane, paneY, out int hostY))
        {
            return false;
        }

        BeginSearchAreaResize(hostY, _hwnd);
        return true;
    }

    private bool TryGetMainPaneResizeHit(ViewportPaneWindow pane, int paneY, out int hostY)
    {
        hostY = int.MinValue;
        if (!ReferenceEquals(_mainPane, pane) || !HasActiveSearch || IsZeroRect(_layout.ViewerRect))
        {
            return false;
        }

        hostY = _layout.ViewerRect.top + paneY;
        return TryGetSearchAreaResizeHit(hostY);
    }

    private bool OnResultsPaneResizeHit(ViewportPaneWindow pane, int paneY)
        => TryGetResultsPaneResizeHit(pane, paneY, out _, out _, out _);

    private bool OnResultsPaneResizeBegin(ViewportPaneWindow pane, int paneY)
    {
        if (!TryGetResultsPaneResizeHit(pane, paneY, out bool resizeSearchArea, out int index, out int hostY))
        {
            return false;
        }

        BeginSearchResize(resizeSearchArea, index, hostY, _hwnd);
        return true;
    }

    private bool TryGetResultsPaneResizeHit(ViewportPaneWindow pane, int paneY, out bool resizeSearchArea, out int index, out int hostY)
    {
        resizeSearchArea = false;
        index = FindSearchLevelIndexByResultsPane(pane);
        hostY = int.MinValue;
        if (index < 0 ||
            index >= _activeSearchLevelCount ||
            index >= _layout.SearchLevelLayouts.Length)
        {
            return false;
        }

        NativeMethods.RECT rect = _layout.SearchLevelLayouts[index].SearchResultsRect;
        if (IsZeroRect(rect))
        {
            return false;
        }

        hostY = rect.top + paneY;
        if (Math.Abs(hostY - rect.top) > SearchResizeHitSlopPx)
        {
            return false;
        }

        resizeSearchArea = GetVisibleActiveSearchResultCount() <= 1;
        return true;
    }

    private void ReadSearchLevelFromControls(SearchLevelState level)
    {
        if (level.IsParserFilter)
        {
            return;
        }

        level.Query = ReadWindowText(level.SearchEdit);
        level.UseRegex = IsButtonChecked(level.RegexCheckbox);
        level.IgnoreCase = IsButtonChecked(level.IgnoreCaseCheckbox);
        level.InvertMatch = IsButtonChecked(level.InvertMatchCheckbox);
    }

    private void ReadAllSearchLevelsFromControls()
    {
        foreach (SearchLevelState level in _searchLevels)
        {
            ReadSearchLevelFromControls(level);
        }
    }

    private void NormalizeSearchLevelsFromControls()
    {
        ReadAllSearchLevelsFromControls();
        int previousActiveCount = _activeSearchLevelCount;
        double[] previousPanelRatios = GetNormalizedPanelRatios(previousActiveCount);
        List<ActiveSearchLevelSnapshot> activeLevels = new();
        for (int i = 0; i < _parserFilterLevelCount && i < _searchLevels.Count; i++)
        {
            SearchLevelState level = _searchLevels[i];
            activeLevels.Add(new ActiveSearchLevelSnapshot(
                i,
                new SearchLevelSnapshot(level.Query, level.UseRegex, level.IgnoreCase, level.InvertMatch)));
        }

        for (int i = _parserFilterLevelCount; i < _searchLevels.Count; i++)
        {
            SearchLevelState level = _searchLevels[i];
            if (!level.IsParserFilter && !string.IsNullOrEmpty(level.Query))
            {
                activeLevels.Add(new ActiveSearchLevelSnapshot(
                    i,
                    new SearchLevelSnapshot(level.Query, level.UseRegex, level.IgnoreCase, level.InvertMatch)));
            }
        }

        bool structureChanged = activeLevels.Count != previousActiveCount || _searchLevels.Count != activeLevels.Count + 1;
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        while (_searchLevels.Count < activeLevels.Count + 1)
        {
            CreateSearchLevel(hInstance);
            structureChanged = true;
        }

        for (int i = _searchLevels.Count - 1; i >= activeLevels.Count + 1; i--)
        {
            DestroySearchLevel(_searchLevels[i]);
            _searchLevels.RemoveAt(i);
            structureChanged = true;
        }

        _updatingSearchControls = true;
        try
        {
            for (int i = 0; i < activeLevels.Count; i++)
            {
                SetSearchLevelControls(_searchLevels[i], activeLevels[i].Snapshot);
                SetSearchLevelParserFilterState(_searchLevels[i], i < _parserFilterLevelCount);
            }

            SearchLevelState emptyLevel = _searchLevels[activeLevels.Count];
            SetSearchLevelParserFilterState(emptyLevel, isParserFilter: false);
            SetSearchLevelControls(
                emptyLevel,
                new SearchLevelSnapshot(string.Empty, emptyLevel.UseRegex, emptyLevel.IgnoreCase, emptyLevel.InvertMatch));
        }
        finally
        {
            _updatingSearchControls = false;
        }

        _activeSearchLevelCount = activeLevels.Count;
        if (structureChanged)
        {
            ApplyPanelRatiosAfterStructureChange(previousPanelRatios, activeLevels, previousActiveCount);
        }
    }

    private void ApplyParserFilterSearchLevels(DisplayParserRule? parserRule)
    {
        SearchOptions[] filterOptions = DisplayParserEvaluator.GetFilterOptions(parserRule);
        if (_resetParserInputVisibilityOnNextApply)
        {
            ResetParserInputVisibility();
            _resetParserInputVisibilityOnNextApply = false;
        }

        SynchronizeParserInputVisibility(
            filterOptions.Length,
            preserveWhenEmpty: parserRule is null && _parserPreviewActive);

        int previousActiveCount = _activeSearchLevelCount;
        int previousParserFilterCount = _parserFilterLevelCount;
        double? preservedSearchAreaRatio = _customSearchAreaRatio;
        double[] previousPanelRatios = GetNormalizedPanelRatios(previousActiveCount);
        List<ActiveSearchLevelSnapshot> manualLevels = new();
        SearchLevelSnapshot emptyManualLevel = new(string.Empty, UseRegex: true, IgnoreCase: false, InvertMatch: false);
        for (int i = 0; i < _searchLevels.Count; i++)
        {
            SearchLevelState level = _searchLevels[i];
            if (!level.IsParserFilter)
            {
                ReadSearchLevelFromControls(level);
                if (!string.IsNullOrEmpty(level.Query))
                {
                    manualLevels.Add(new ActiveSearchLevelSnapshot(
                        i,
                        new SearchLevelSnapshot(level.Query, level.UseRegex, level.IgnoreCase, level.InvertMatch)));
                }
                else
                {
                    emptyManualLevel = new SearchLevelSnapshot(
                        string.Empty,
                        level.UseRegex,
                        level.IgnoreCase,
                        level.InvertMatch);
                }
            }
        }

        int activeCount = filterOptions.Length + manualLevels.Count;
        int requiredCount = activeCount + 1;
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        while (_searchLevels.Count < requiredCount)
        {
            CreateSearchLevel(hInstance);
        }

        for (int i = _searchLevels.Count - 1; i >= requiredCount; i--)
        {
            DestroySearchLevel(_searchLevels[i]);
            _searchLevels.RemoveAt(i);
        }

        List<ActiveSearchLevelSnapshot> resultingLevels = new(activeCount);
        _updatingSearchControls = true;
        try
        {
            for (int i = 0; i < filterOptions.Length; i++)
            {
                SearchOptions option = filterOptions[i];
                SearchLevelState level = _searchLevels[i];
                SetSearchLevelControls(
                    level,
                    new SearchLevelSnapshot(option.Query, option.UseRegex, option.IgnoreCase, option.InvertMatch));
                level.ShowParserInput = _parserInputVisibility[i];
                SetSearchLevelParserFilterState(level, isParserFilter: true);
                int sourceIndex = i < _parserFilterLevelCount && i < previousActiveCount ? i : -1;
                resultingLevels.Add(new ActiveSearchLevelSnapshot(
                    sourceIndex,
                    new SearchLevelSnapshot(option.Query, option.UseRegex, option.IgnoreCase, option.InvertMatch)));
            }

            for (int i = 0; i < manualLevels.Count; i++)
            {
                SearchLevelState level = _searchLevels[filterOptions.Length + i];
                SetSearchLevelControls(level, manualLevels[i].Snapshot);
                SetSearchLevelParserFilterState(level, isParserFilter: false);
                resultingLevels.Add(manualLevels[i]);
            }

            SearchLevelState emptyLevel = _searchLevels[activeCount];
            SetSearchLevelParserFilterState(emptyLevel, isParserFilter: false);
            SetSearchLevelControls(emptyLevel, emptyManualLevel);
        }
        finally
        {
            _updatingSearchControls = false;
        }

        bool structureChanged =
            previousActiveCount != activeCount ||
            previousParserFilterCount != filterOptions.Length;
        _parserFilterLevelCount = filterOptions.Length;
        _activeSearchLevelCount = activeCount;
        if (structureChanged)
        {
            ApplyPanelRatiosAfterStructureChange(
                previousPanelRatios,
                resultingLevels,
                previousActiveCount);
            if (previousParserFilterCount > 0 || filterOptions.Length > 0)
            {
                _customSearchAreaRatio = preservedSearchAreaRatio;
            }
        }
    }

    private void SetSearchLevelControls(SearchLevelState level, SearchLevelSnapshot snapshot)
    {
        if (level.Query != snapshot.Query)
        {
            NativeMethods.SetWindowTextW(level.SearchEdit, snapshot.Query);
        }

        level.Query = snapshot.Query;
        level.UseRegex = snapshot.UseRegex;
        level.IgnoreCase = snapshot.IgnoreCase;
        level.InvertMatch = snapshot.InvertMatch;
        SetButtonChecked(level.RegexCheckbox, snapshot.UseRegex);
        SetButtonChecked(level.IgnoreCaseCheckbox, snapshot.IgnoreCase);
        SetButtonChecked(level.InvertMatchCheckbox, snapshot.InvertMatch);
    }

    private void SetSearchLevelParserFilterState(SearchLevelState level, bool isParserFilter)
    {
        level.IsParserFilter = isParserFilter;
        if (!isParserFilter)
        {
            level.ShowParserInput = false;
        }

        SetButtonChecked(level.ParserInputVisibilityButton, level.ShowParserInput);
        UpdateParserInputTooltip(level);
        NativeMethods.EnableWindow(level.SearchEdit, !isParserFilter);
        NativeMethods.EnableWindow(level.RegexCheckbox, !isParserFilter);
        NativeMethods.EnableWindow(level.IgnoreCaseCheckbox, !isParserFilter);
        NativeMethods.EnableWindow(level.InvertMatchCheckbox, !isParserFilter);
    }

    private void ResetParserInputVisibility()
    {
        _parserInputVisibility.Clear();
        foreach (SearchLevelState level in _searchLevels)
        {
            level.ShowParserInput = false;
            SetButtonChecked(level.ParserInputVisibilityButton, checkedState: false);
            UpdateParserInputTooltip(level);
        }
    }

    private void SynchronizeParserInputVisibility(int filterCount, bool preserveWhenEmpty)
    {
        filterCount = Math.Max(0, filterCount);
        if (filterCount == 0 && preserveWhenEmpty)
        {
            return;
        }

        if (_parserInputVisibility.Count > filterCount)
        {
            _parserInputVisibility.RemoveRange(
                filterCount,
                _parserInputVisibility.Count - filterCount);
        }

        while (_parserInputVisibility.Count < filterCount)
        {
            _parserInputVisibility.Add(false);
        }
    }

    private double[] GetNormalizedPanelRatios(int activeSearchCount)
    {
        int activeCount = Math.Clamp(activeSearchCount, 0, _searchLevels.Count);
        if (activeCount == 0)
        {
            return new[] { 1d };
        }

        double searchAreaRatio = GetSearchAreaRatio(activeCount);
        double[] searchRatios = GetNormalizedSearchResultRatios(activeCount);
        double[] panelRatios = new double[activeCount + 1];
        panelRatios[0] = Math.Max(0d, 1d - searchAreaRatio);
        for (int i = 0; i < activeCount; i++)
        {
            panelRatios[i + 1] = searchAreaRatio * (i < searchRatios.Length ? searchRatios[i] : 0d);
        }

        return NormalizeRatios(panelRatios, panelRatios.Length);
    }

    private void ApplyPanelRatiosAfterStructureChange(
        IReadOnlyList<double> previousPanelRatios,
        IReadOnlyList<ActiveSearchLevelSnapshot> activeLevels,
        int previousActiveCount)
    {
        int activeCount = activeLevels.Count;
        if (activeCount == 0)
        {
            _customSearchAreaRatio = null;
            ClearSearchResultRatios();
            return;
        }

        double[] previousRatios = NormalizeRatios(previousPanelRatios, Math.Max(1, previousActiveCount + 1));
        double[] existingRatios = new double[activeCount + 1];
        bool[] newSearchPanels = new bool[activeCount];

        existingRatios[0] = previousRatios[0];
        double existingTotal = existingRatios[0];
        int existingPanelCount = 1;
        int newPanelCount = 0;
        for (int i = 0; i < activeCount; i++)
        {
            ActiveSearchLevelSnapshot activeLevel = activeLevels[i];
            if (activeLevel.SourceIndex >= 0 && activeLevel.SourceIndex < previousActiveCount)
            {
                double ratio = GetRatioOrZero(previousRatios, activeLevel.SourceIndex + 1);
                existingRatios[i + 1] = ratio;
                existingTotal += ratio;
                existingPanelCount++;
                continue;
            }

            newSearchPanels[i] = true;
            newPanelCount++;
        }

        double newPanelRatio = newPanelCount > 0 ? 1d / (activeCount + 1) : 0d;
        double remainingRatio = Math.Clamp(1d - (newPanelRatio * newPanelCount), 0d, 1d);
        double fallbackExistingRatio = existingPanelCount > 0 ? remainingRatio / existingPanelCount : 0d;
        double existingScale = existingTotal > 0d ? remainingRatio / existingTotal : 0d;

        double[] panelRatios = new double[activeCount + 1];
        panelRatios[0] = existingTotal > 0d ? existingRatios[0] * existingScale : fallbackExistingRatio;
        for (int i = 0; i < activeCount; i++)
        {
            panelRatios[i + 1] = newSearchPanels[i]
                ? newPanelRatio
                : existingTotal > 0d
                    ? existingRatios[i + 1] * existingScale
                    : fallbackExistingRatio;
        }

        ApplyPanelRatios(panelRatios, activeCount);
    }

    private void ApplyPanelRatios(IReadOnlyList<double> panelRatios, int activeSearchCount)
    {
        int activeCount = Math.Clamp(activeSearchCount, 0, _searchLevels.Count);
        if (activeCount == 0)
        {
            _customSearchAreaRatio = null;
            ClearSearchResultRatios();
            return;
        }

        double[] normalizedRatios = NormalizeRatios(panelRatios, activeCount + 1);
        double searchAreaRatio = 0d;
        for (int i = 0; i < activeCount; i++)
        {
            searchAreaRatio += normalizedRatios[i + 1];
        }

        _customSearchAreaRatio = Math.Clamp(searchAreaRatio, 0d, 1d);
        if (searchAreaRatio <= 0d)
        {
            double defaultRatio = 1d / activeCount;
            for (int i = 0; i < activeCount; i++)
            {
                _searchLevels[i].CustomResultRatio = defaultRatio;
            }
        }
        else
        {
            for (int i = 0; i < activeCount; i++)
            {
                _searchLevels[i].CustomResultRatio = Math.Clamp(normalizedRatios[i + 1] / searchAreaRatio, 0d, 1d);
            }
        }

        for (int i = activeCount; i < _searchLevels.Count; i++)
        {
            _searchLevels[i].CustomResultRatio = null;
        }
    }

    private void ClearSearchResultRatios()
    {
        foreach (SearchLevelState level in _searchLevels)
        {
            level.CustomResultRatio = null;
        }
    }

    private static double[] NormalizeRatios(IReadOnlyList<double> ratios, int expectedCount)
    {
        expectedCount = Math.Max(1, expectedCount);
        double[] normalizedRatios = new double[expectedCount];
        double total = 0d;
        for (int i = 0; i < expectedCount; i++)
        {
            double ratio = GetRatioOrZero(ratios, i);
            normalizedRatios[i] = ratio;
            total += ratio;
        }

        if (total <= 0d)
        {
            Array.Fill(normalizedRatios, 1d / expectedCount);
            return normalizedRatios;
        }

        for (int i = 0; i < normalizedRatios.Length; i++)
        {
            normalizedRatios[i] /= total;
        }

        return normalizedRatios;
    }

    private static double GetRatioOrZero(IReadOnlyList<double> ratios, int index)
    {
        if (index < 0 || index >= ratios.Count)
        {
            return 0d;
        }

        double ratio = ratios[index];
        return double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : 0d;
    }

    private SearchOptions[] GetActiveSearchOptions()
    {
        SearchOptions[] options = new SearchOptions[_activeSearchLevelCount];
        for (int i = 0; i < options.Length; i++)
        {
            SearchLevelState level = _searchLevels[i];
            options[i] = new SearchOptions(level.Query, level.UseRegex, level.IgnoreCase, level.InvertMatch);
        }

        return options;
    }

    private static SearchOptions[] GetManualSearchOptions(
        IReadOnlyList<SearchOptions> effectiveOptions,
        DisplayParserRule? parserRule)
    {
        int filterCount = Math.Min(
            DisplayParserEvaluator.GetFilterCount(parserRule),
            effectiveOptions.Count);
        SearchOptions[] manualOptions = new SearchOptions[effectiveOptions.Count - filterCount];
        for (int i = 0; i < manualOptions.Length; i++)
        {
            manualOptions[i] = effectiveOptions[filterCount + i];
        }

        return manualOptions;
    }

    private int[] GetActiveSearchVisibleLineCounts()
    {
        int[] visibleLines = new int[_activeSearchLevelCount];
        for (int i = 0; i < visibleLines.Length; i++)
        {
            visibleLines[i] = _searchLevels[i].ResultsPane?.VisibleDataLineCount ?? 1;
        }

        return visibleLines;
    }

    private FilteredLogRecordSource[]? GetActiveFilteredReaders()
    {
        if (_activeSearchLevelCount == 0)
        {
            return Array.Empty<FilteredLogRecordSource>();
        }

        FilteredLogRecordSource[] readers = new FilteredLogRecordSource[_activeSearchLevelCount];
        for (int i = 0; i < readers.Length; i++)
        {
            if (_searchLevels[i].ResultsPane?.Reader is not ProjectedViewport { Source: FilteredLogRecordSource reader })
            {
                return null;
            }

            readers[i] = reader;
        }

        return readers;
    }

    private FilteredLogRecordSource[]? GetFilteredReaderPrefix(int count)
    {
        if (count < 0 || count > _activeSearchLevelCount)
        {
            return null;
        }

        FilteredLogRecordSource[] readers = new FilteredLogRecordSource[count];
        for (int i = 0; i < count; i++)
        {
            if (_searchLevels[i].ResultsPane?.Reader is not ProjectedViewport { Source: FilteredLogRecordSource reader })
            {
                return null;
            }

            readers[i] = reader;
        }

        return readers;
    }

    private void SetActiveSearchResultStatus(string status, bool disposeReader = true, bool preserveColumnWidths = false)
    {
        for (int i = 0; i < _searchLevels.Count; i++)
        {
            if (i < _activeSearchLevelCount)
            {
                _searchLevels[i].ResultsPane?.SetStatus(status, disposeReader, preserveColumnWidths);
            }
            else
            {
                _searchLevels[i].ResultsPane?.SetStatus(string.Empty);
            }
        }
    }

    private void SetSearchResultStatusFromLevel(int startLevel, string status, bool disposeReader = true, bool preserveColumnWidths = false)
    {
        startLevel = Math.Clamp(startLevel, 0, _searchLevels.Count);
        for (int i = startLevel; i < _searchLevels.Count; i++)
        {
            if (i < _activeSearchLevelCount)
            {
                _searchLevels[i].ResultsPane?.SetStatus(status, disposeReader, preserveColumnWidths);
            }
            else
            {
                _searchLevels[i].ResultsPane?.SetStatus(string.Empty);
            }
        }
    }

    private void ShowActiveSearchEmptyReaders(IReadOnlyList<SearchOptions> options)
    {
        for (int i = 0; i < _searchLevels.Count; i++)
        {
            if (i < _activeSearchLevelCount)
            {
                ShowSearchEmptyReader(_searchLevels[i].ResultsPane, options, i);
            }
            else
            {
                _searchLevels[i].ResultsPane?.SetStatus(string.Empty);
            }
        }
    }

    private void ShowSearchEmptyReadersFromLevel(int startLevel, IReadOnlyList<SearchOptions> options)
    {
        startLevel = Math.Clamp(startLevel, 0, _searchLevels.Count);
        for (int i = startLevel; i < _searchLevels.Count; i++)
        {
            if (i < _activeSearchLevelCount)
            {
                ShowSearchEmptyReader(_searchLevels[i].ResultsPane, options, i);
            }
            else
            {
                _searchLevels[i].ResultsPane?.SetStatus(string.Empty);
            }
        }
    }

    private void ShowSearchEmptyReader(ViewportPaneWindow? pane, IReadOnlyList<SearchOptions> options, int stageIndex)
    {
        if (pane is null || _detectedEncoding is not DetectedEncodingInfo detected)
        {
            return;
        }

        DisplayParserRule? parserRule = GetEffectiveParserRule();
        SearchOptions[] manualOptions = GetManualSearchOptions(options, parserRule);
        FilteredLogRecordSource source = LogSearchBuilder.CreateEmptyReader(
            _contentSource,
            detected.Encoding,
            detected.DataOffset,
            GetKnownContentFileSize(detected.DataOffset),
            manualOptions,
            stageIndex,
            parserRule);
        var reader = new FilteredProjectedViewport(source);
        reader.ReadFromPercentage(0d, pane.VisibleDataLineCount);
        pane.SetReader(reader, pane.VisibleDataLineCount, preserveColumnWidths: true);
    }

    private void MarkActiveSearchReadersObservedZero()
    {
        for (int i = 0; i < _activeSearchLevelCount && i < _searchLevels.Count; i++)
        {
            _searchLevels[i].ResultsPane?.MarkReaderObservedZero();
        }

        for (int i = _activeSearchLevelCount; i < _searchLevels.Count; i++)
        {
            _searchLevels[i].ResultsPane?.SetStatus(string.Empty, disposeReader: false, preserveColumnWidths: true);
        }
    }

    private long ClearActiveSearchReadersObservedZero()
    {
        long matchedLineCount = _searchMatchedLineCount;
        for (int i = 0; i < _activeSearchLevelCount && i < _searchLevels.Count; i++)
        {
            if (_searchLevels[i].ResultsPane?.Reader is ProjectedViewport { Source: FilteredLogRecordSource reader })
            {
                _searchLevels[i].ResultsPane!.ClearReaderObservedZero();
                matchedLineCount = reader.MatchedLineCount;
            }
        }

        return matchedLineCount;
    }

    private void ClearPausedSearchCheckpoint()
    {
        _pausedSearchCheckpoint = null;
        _waitingForPausedSearchRequestId = 0;
    }

    private void ClearPendingLowerSearchChange()
    {
        _pendingLowerSearchChange = null;
    }

    private bool HasActiveWorkerBeforeLevel(int startLevel)
    {
        if (startLevel <= 0 || _searchStale || _waitingForPausedSearchRequestId != 0)
        {
            return false;
        }

        lock (_searchCancellationSync)
        {
            return _activeSearchCancellation is not null &&
                _activeSearchWorkerRequestId == _latestSearchRequestId &&
                _activeSearchWorkerStartLevel < startLevel;
        }
    }

    private void StorePendingLowerSearchChange(int startLevel, SearchOptions[] options)
    {
        long sourceRequestId;
        lock (_searchCancellationSync)
        {
            sourceRequestId = _activeSearchWorkerRequestId;
        }

        if (sourceRequestId == 0)
        {
            return;
        }

        if (_pendingLowerSearchChange is null || _pendingLowerSearchChange.SourceRequestId != sourceRequestId)
        {
            _pendingLowerSearchChange = new PendingLowerSearchChange
            {
                SourceRequestId = sourceRequestId,
                StartLevel = startLevel,
                Options = CopySearchOptions(options)
            };
        }
        else
        {
            _pendingLowerSearchChange.StartLevel = Math.Min(_pendingLowerSearchChange.StartLevel, startLevel);
            _pendingLowerSearchChange.Options = CopySearchOptions(options);
        }

        _pendingSearchStartLevel = 0;
        _appendSearchPending = false;
        _appendSearchInProgress = false;
        _searchDisplayActive = true;
        _searchErrorText = string.Empty;
        ShowSearchEmptyReadersFromLevel(_pendingLowerSearchChange.StartLevel, _pendingLowerSearchChange.Options);
        UpdateLayout();
    }

    private PausedSearchCheckpoint CreateSearchCheckpoint(SearchWorkerResult result)
    {
        return new PausedSearchCheckpoint
        {
            ProcessedOffset = result.ProcessedOffset,
            TargetFileSize = result.TargetFileSize,
            Options = result.Options is { Length: > 0 } ? CopySearchOptions(result.Options) : GetActiveSearchOptions(),
            MatchedLineCount = result.MatchedLineCount,
            ProgressPercentage = result.ProgressPercentage
        };
    }

    private void StorePausedSearchCheckpoint(SearchWorkerResult result)
    {
        _waitingForPausedSearchRequestId = 0;
        _pausedSearchCheckpoint = CreateSearchCheckpoint(result);

        _pausedSearchCheckpoint.ProgressPercentage = Math.Max(_searchProgressPercentage, Math.Clamp(result.ProgressPercentage, 0d, 100d));
        _pausedSearchCheckpoint.MatchedLineCount = Math.Max(_searchMatchedLineCount, Math.Max(0, result.MatchedLineCount));
        _searchProgressPercentage = _pausedSearchCheckpoint.ProgressPercentage;
        _searchMatchedLineCount = _pausedSearchCheckpoint.MatchedLineCount;
        _searchInProgress = false;
        _appendSearchInProgress = false;
        _appendSearchPending = false;
        _searchDisplayActive = true;
        _searchErrorText = "Search stale";
        _mainPane?.MarkReaderObservedZero();
        ApplySearchResultReaders(result, markObservedZero: true);
        UpdateLayout();
    }

    private void HandleObservedZeroFileSize()
    {
        if (_searchStale)
        {
            return;
        }

        ClearPendingLowerSearchChange();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        }

        _mainPane?.MarkReaderObservedZero();
        if ((_searchInProgress || _appendSearchInProgress) && _waitingForPausedSearchRequestId == 0)
        {
            long pausedRequestId = _latestSearchRequestId;
            if (CancelActiveSearch())
            {
                _waitingForPausedSearchRequestId = pausedRequestId;
            }
        }

        _searchDisplayActive = true;
        _searchErrorText = "Search stale";
        _searchInProgress = false;
        _appendSearchInProgress = false;
        _appendSearchPending = false;
        MarkActiveSearchReadersObservedZero();
        UpdateLayout();
    }

    private bool TryResumePausedSearch(long currentFileSize)
    {
        PausedSearchCheckpoint? checkpoint = _pausedSearchCheckpoint;
        if (checkpoint is null)
        {
            return false;
        }

        if (currentFileSize == 0)
        {
            HandleObservedZeroFileSize();
            return true;
        }

        if (currentFileSize < checkpoint.TargetFileSize)
        {
            ClearPausedSearchCheckpoint();
            MarkSearchStale();
            return true;
        }

        _mainPane?.ClearReaderObservedZero();
        _searchErrorText = string.Empty;
        _searchMatchedLineCount = ClearActiveSearchReadersObservedZero();
        _searchProgressPercentage = Math.Clamp(checkpoint.ProgressPercentage, 0d, 100d);

        FilteredLogRecordSource[]? currentReaders = GetActiveFilteredReaders();
        if (currentReaders is null || currentReaders.Length != checkpoint.Options.Length)
        {
            ClearPausedSearchCheckpoint();
            MarkSearchStale();
            return true;
        }

        if (currentFileSize <= checkpoint.ProcessedOffset)
        {
            _searchProgressPercentage = 100d;
            _searchMatchedLineCount = checkpoint.MatchedLineCount;
            ClearPausedSearchCheckpoint();
            for (int i = 0; i < _activeSearchLevelCount && i < _searchLevels.Count; i++)
            {
                _searchLevels[i].ResultsPane?.QueueReloadAfterFileChange();
            }

            UpdateLayout();
            return true;
        }

        SearchOptions[] options = CopySearchOptions(checkpoint.Options);
        long processedOffset = checkpoint.ProcessedOffset;
        ClearPausedSearchCheckpoint();
        DispatchResumeSearch(currentReaders, currentFileSize, processedOffset, options);
        return true;
    }

    private void DispatchFullSearchAfterObservedZeroRestore()
    {
        SearchOptions[] options = GetActiveSearchOptions();
        if (!AreSearchOptionsValid(options))
        {
            return;
        }

        long requestId = _latestSearchRequestId = ++_nextSearchRequestId;
        _appendSearchPending = false;
        _appendSearchInProgress = false;
        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _searchErrorText = string.Empty;
        ShowActiveSearchEmptyReaders(options);
        InvalidateSearchBar();
        DispatchSearch(requestId, options);
    }

    private void ApplySearchResultReaders(SearchWorkerResult result, bool markObservedZero)
    {
        if (result.Readers is null)
        {
            return;
        }

        for (int i = 0; i < result.Readers.Length; i++)
        {
            FilteredLogRecordSource? nextSource = result.Readers[i];
            if (nextSource is null)
            {
                continue;
            }

            if (i >= _activeSearchLevelCount || i >= _searchLevels.Count || _searchLevels[i].ResultsPane is not ViewportPaneWindow pane)
            {
                nextSource.Dispose();
                result.Readers[i] = null;
                continue;
            }

            long desiredTopRow = 0;
            bool shouldKeepAtEnd = false;
            if (!markObservedZero && pane.Reader is ProjectedViewport { Source: FilteredLogRecordSource currentFilteredReader })
            {
                desiredTopRow = currentFilteredReader.TopRecordOrdinal;
                shouldKeepAtEnd = result.IsAppendUpdate && currentFilteredReader.IsAtConfirmedEnd;
            }

            var nextReader = new FilteredProjectedViewport(nextSource);
            if (shouldKeepAtEnd)
            {
                nextReader.ReadFromPercentage(100d, pane.VisibleDataLineCount);
            }
            else if (desiredTopRow == nextSource.TopRecordOrdinal &&
                (nextSource.CurrentRecords.Count > 0 || !nextSource.HasContent))
            {
                nextReader.UseCurrentSourceRecords(pane.VisibleDataLineCount);
            }
            else
            {
                nextReader.ReadFromRowOrdinal(desiredTopRow, pane.VisibleDataLineCount);
            }

            if (markObservedZero)
            {
                nextReader.MarkObservedZeroFileSize();
            }

            pane.SetReader(
                nextReader,
                result.PreloadedVisibleLines is not null && i < result.PreloadedVisibleLines.Length
                    ? result.PreloadedVisibleLines[i]
                    : pane.VisibleDataLineCount,
                preserveColumnWidths: true,
                preserveSelection: true);
            if (markObservedZero)
            {
                pane.MarkReaderObservedZero();
            }

            result.Readers[i] = null;
        }
    }

    private static long GetFinalMatchedLineCount(IReadOnlyList<long>? counts)
    {
        return counts is null || counts.Count == 0 ? 0 : counts[counts.Count - 1];
    }

    private int GetSearchUpdateEndLevel(SearchWorkerResult result)
    {
        PendingLowerSearchChange? pending = _pendingLowerSearchChange;
        if (pending is not null &&
            pending.SourceRequestId == result.RequestId &&
            result.SearchStartLevel < pending.StartLevel)
        {
            return Math.Clamp(pending.StartLevel, 0, _activeSearchLevelCount);
        }

        return _activeSearchLevelCount;
    }

    private long GetSearchUpdateMatchedLineCount(SearchWorkerResult result)
    {
        PendingLowerSearchChange? pending = _pendingLowerSearchChange;
        if (pending is not null &&
            pending.SourceRequestId == result.RequestId &&
            pending.StartLevel > 0 &&
            result.StageMatchedLineCounts is not null &&
            pending.StartLevel - 1 < result.StageMatchedLineCounts.Length)
        {
            return Math.Max(0, result.StageMatchedLineCounts[pending.StartLevel - 1]);
        }

        return Math.Max(0, result.MatchedLineCount);
    }

    private static void DisposeReadersFromLevel(IList<FilteredLogRecordSource?>? readers, int startLevel)
    {
        if (readers is null)
        {
            return;
        }

        startLevel = Math.Clamp(startLevel, 0, readers.Count);
        for (int i = startLevel; i < readers.Count; i++)
        {
            readers[i]?.Dispose();
            readers[i] = null;
        }
    }

    private bool TryDispatchPendingLowerSearchChange(SearchWorkerResult completedResult)
    {
        PendingLowerSearchChange? pending = _pendingLowerSearchChange;
        if (pending is null || pending.SourceRequestId != completedResult.RequestId || !completedResult.IsFinal || !completedResult.Success)
        {
            return false;
        }

        int startLevel = pending.StartLevel;
        SearchOptions[] options = CopySearchOptions(pending.Options);
        ClearPendingLowerSearchChange();
        if (startLevel <= 0)
        {
            return false;
        }

        if (startLevel >= options.Length)
        {
            return TryCompleteSearchFromExistingPrefix(options.Length);
        }

        FilteredLogRecordSource[]? prefixReaders = GetFilteredReaderPrefix(startLevel);
        if (prefixReaders is null)
        {
            RestartSearchAfterInputChange();
            return true;
        }

        long requestId = _latestSearchRequestId = ++_nextSearchRequestId;
        _pendingSearchStartLevel = 0;
        _searchStale = false;
        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchErrorText = string.Empty;
        ShowSearchEmptyReadersFromLevel(startLevel, options);
        DispatchChangedSearch(requestId, prefixReaders, startLevel, options);
        return true;
    }

    private static void DisposeReaders(IReadOnlyList<FilteredLogRecordSource?>? readers)
    {
        if (readers is null)
        {
            return;
        }

        foreach (FilteredLogRecordSource? reader in readers)
        {
            reader?.Dispose();
        }
    }

    private static void ThrowIfCancelledBeforePostingUpdate(StagedSearchProgressUpdate update, CancellationToken cancellationToken)
    {
        if (update.IsPaused || !cancellationToken.IsCancellationRequested)
        {
            return;
        }

        DisposeReaders(update.Readers);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static SearchOptions[] CopySearchOptions(IReadOnlyList<SearchOptions> options)
    {
        SearchOptions[] copy = new SearchOptions[options.Count];
        for (int i = 0; i < options.Count; i++)
        {
            copy[i] = options[i];
        }

        return copy;
    }

    private static string FormatSearchQuerySummary(IReadOnlyList<SearchOptions> options)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int i = 0; i < options.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" -> ");
            }

            builder.Append(options[i].Query);
        }

        return builder.ToString();
    }

    private static bool AnySearchOptionUsesRegex(IReadOnlyList<SearchOptions> options)
    {
        foreach (SearchOptions option in options)
        {
            if (option.UseRegex)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnySearchOptionIgnoresCase(IReadOnlyList<SearchOptions> options)
    {
        foreach (SearchOptions option in options)
        {
            if (option.IgnoreCase)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnySearchOptionInvertsMatch(IReadOnlyList<SearchOptions> options)
    {
        foreach (SearchOptions option in options)
        {
            if (option.InvertMatch)
            {
                return true;
            }
        }

        return false;
    }

    private void DestroySearchLevel(SearchLevelState level)
    {
        if (level.ParserInputVisibilityButton != IntPtr.Zero)
        {
            RemoveParserInputTooltip(level);
            NativeMethods.DestroyWindow(level.ParserInputVisibilityButton);
            level.ParserInputVisibilityButton = IntPtr.Zero;
        }

        if (level.SearchEdit != IntPtr.Zero)
        {
            if (level.OriginalSearchEditProc != IntPtr.Zero)
            {
                NativeMethods.SetWindowLongPtrW(level.SearchEdit, NativeMethods.GWLP_WNDPROC, level.OriginalSearchEditProc);
                level.OriginalSearchEditProc = IntPtr.Zero;
            }

            NativeMethods.DestroyWindow(level.SearchEdit);
            level.SearchEdit = IntPtr.Zero;
        }

        if (level.RegexCheckbox != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(level.RegexCheckbox);
            level.RegexCheckbox = IntPtr.Zero;
        }

        if (level.IgnoreCaseCheckbox != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(level.IgnoreCaseCheckbox);
            level.IgnoreCaseCheckbox = IntPtr.Zero;
        }

        if (level.InvertMatchCheckbox != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(level.InvertMatchCheckbox);
            level.InvertMatchCheckbox = IntPtr.Zero;
        }

        level.ResultsPane?.Dispose();
        level.ResultsPane = null;
    }

    private bool OnDrawItem(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero || _parserCombo == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.DRAWITEMSTRUCT item = Marshal.PtrToStructure<NativeMethods.DRAWITEMSTRUCT>(lParam);
        if (item.hwndItem != _parserCombo)
        {
            return false;
        }

        int itemIndex = unchecked((int)item.itemID);
        if (itemIndex < 0)
        {
            return true;
        }

        string text = GetParserComboItemText(itemIndex);
        bool selected = (item.itemState & NativeMethods.ODS_SELECTED) != 0;
        bool configureItem = itemIndex == _parserRules.Count + 1;
        NativeMethods.RECT rect = item.rcItem;
        IntPtr backgroundBrush = selected
            ? NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT)
            : configureItem
                ? NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DFACE)
            : NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW);
        NativeMethods.FillRect(item.hDC, ref rect, backgroundBrush);
        if (configureItem && !selected)
        {
            NativeMethods.RECT separator = rect;
            separator.bottom = Math.Min(separator.bottom, separator.top + 1);
            NativeMethods.FillRect(item.hDC, ref separator, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_BTNSHADOW));
        }

        int savedDc = NativeMethods.SaveDC(item.hDC);
        try
        {
            NativeMethods.SetBkMode(item.hDC, NativeMethods.TRANSPARENT);
            int textColor = selected
                ? NativeMethods.GetSysColor(NativeMethods.COLOR_HIGHLIGHTTEXT)
                    : itemIndex == 0
                        ? NativeMethods.RGB(150, 150, 150)
                    : NativeMethods.GetSysColor(NativeMethods.COLOR_WINDOWTEXT);
            NativeMethods.SetTextColor(item.hDC, textColor);

            IntPtr font = configureItem && _boldFont != IntPtr.Zero ? _boldFont : _font;
            IntPtr oldFont = font != IntPtr.Zero
                ? NativeMethods.SelectObject(item.hDC, font)
                : IntPtr.Zero;
            try
            {
                NativeMethods.RECT textRect = rect;
                textRect.left += 4;
                textRect.right = Math.Max(textRect.left, textRect.right - 4);
                NativeMethods.DrawTextW(
                    item.hDC,
                    text,
                    text.Length,
                    ref textRect,
                    NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_END_ELLIPSIS | NativeMethods.DT_NOPREFIX);
            }
            finally
            {
                if (oldFont != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(item.hDC, oldFont);
                }
            }

        }
        finally
        {
            NativeMethods.RestoreDC(item.hDC, savedDc);
        }

        return true;
    }

    private string GetParserComboItemText(int itemIndex)
    {
        if (itemIndex == 0)
        {
            return NoParserText;
        }

        int ruleIndex = itemIndex - 1;
        if (ruleIndex >= 0 && ruleIndex < _parserRules.Count)
        {
            return _parserRules[ruleIndex].Name;
        }

        return ConfigureParserText;
    }

    private void OnCommand(IntPtr wParam, IntPtr lParam)
    {
        int notification = NativeMethods.HighWord(wParam);
        if (lParam == _openButton && notification == NativeMethods.BN_CLICKED)
        {
            OpenFileInCurrentWindow();
            return;
        }

        if (lParam == _pasteButton && notification == NativeMethods.BN_CLICKED)
        {
            OpenClipboardText();
            return;
        }

        if (lParam == _parserCombo && notification == NativeMethods.CBN_SELCHANGE && !_updatingParserCombo)
        {
            OnParserSelectionChanged();
            return;
        }

        if (lParam == _highlightingButton && notification == NativeMethods.BN_CLICKED)
        {
            OpenHighlightRuleManager();
            return;
        }

        SearchLevelState? parserInputLevel = FindSearchLevelByParserInputVisibilityButton(lParam);
        if (parserInputLevel is not null &&
            parserInputLevel.IsParserFilter &&
            notification == NativeMethods.BN_CLICKED)
        {
            parserInputLevel.ShowParserInput = IsButtonChecked(parserInputLevel.ParserInputVisibilityButton);
            int inputIndex = _searchLevels.IndexOf(parserInputLevel);
            if (inputIndex >= 0 && inputIndex < _parserInputVisibility.Count)
            {
                _parserInputVisibility[inputIndex] = parserInputLevel.ShowParserInput;
            }

            if (inputIndex == 0 &&
                parserInputLevel.ShowParserInput &&
                (_customSearchAreaRatio is <= 0d or >= 1d))
            {
                _customSearchAreaRatio = null;
            }

            UpdateParserInputTooltip(parserInputLevel);
            EndSearchResultsResize();
            UpdateLayout();
            return;
        }

        if (_updatingSearchControls)
        {
            return;
        }

        SearchLevelState? editedSearchLevel = FindSearchLevelByEdit(lParam);
        if (editedSearchLevel is not null && notification == NativeMethods.EN_CHANGE)
        {
            int changedLevelIndex = _searchLevels.IndexOf(editedSearchLevel);
            SearchOptions[] previousOptions = GetActiveSearchOptions();
            NormalizeSearchLevelsFromControls();
            RestartSearchAfterInputChange(changedLevelIndex, previousOptions);
            return;
        }

        SearchLevelState? optionSearchLevel = FindSearchLevelByOptionControl(lParam);
        if (optionSearchLevel is not null && notification == NativeMethods.BN_CLICKED)
        {
            int changedLevelIndex = _searchLevels.IndexOf(optionSearchLevel);
            SearchOptions[] previousOptions = GetActiveSearchOptions();
            NormalizeSearchLevelsFromControls();
            RestartSearchAfterInputChange(changedLevelIndex, previousOptions);
        }
    }

    private void RestartSearchAfterInputChange(
        int changedLevelIndex = 0,
        IReadOnlyList<SearchOptions>? previousOptions = null,
        bool dispatchImmediately = false)
    {
        SearchOptions[] options = GetActiveSearchOptions();
        int searchStartLevel = GetSearchStartLevelForInputChange(changedLevelIndex, previousOptions ?? Array.Empty<SearchOptions>(), options);

        if (options.Length == 0)
        {
            _latestSearchRequestId = ++_nextSearchRequestId;
            _pendingSearchStartLevel = 0;
            NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
            CancelActiveSearch();
            ClearPausedSearchCheckpoint();
            ClearPendingLowerSearchChange();
            _appendSearchPending = false;
            _appendSearchInProgress = false;
            _searchStale = false;
            _searchInProgress = false;
            _searchDisplayActive = false;
            _searchProgressPercentage = 0d;
            _searchMatchedLineCount = 0;
            _searchErrorText = string.Empty;
            SetActiveSearchResultStatus(string.Empty);
            UpdateLayout();
            return;
        }

        if (!TryValidateSearchOptions(options, searchStartLevel))
        {
            ClearPendingLowerSearchChange();
            return;
        }

        if (searchStartLevel > 0 && HasActiveWorkerBeforeLevel(searchStartLevel))
        {
            NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
            StorePendingLowerSearchChange(searchStartLevel, options);
            return;
        }

        _latestSearchRequestId = ++_nextSearchRequestId;
        _pendingSearchStartLevel = searchStartLevel;
        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        CancelActiveSearch();
        _appendSearchPending = false;
        _appendSearchInProgress = false;
        ClearPausedSearchCheckpoint();
        ClearPendingLowerSearchChange();

        if (searchStartLevel > 0 && searchStartLevel >= options.Length && TryCompleteSearchFromExistingPrefix(options.Length))
        {
            _pendingSearchStartLevel = 0;
            return;
        }

        _searchStale = false;
        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _searchErrorText = string.Empty;
        if (searchStartLevel > 0)
        {
            ShowSearchEmptyReadersFromLevel(searchStartLevel, options);
        }
        else
        {
            ShowActiveSearchEmptyReaders(options);
        }

        UpdateLayout();
        if (dispatchImmediately)
        {
            DispatchPendingSearch();
        }
        else
        {
            NativeMethods.SetTimer(_hwnd, SearchDebounceTimerId, SearchDebounceMs, IntPtr.Zero);
        }
    }

    private int GetSearchStartLevelForInputChange(
        int changedLevelIndex,
        IReadOnlyList<SearchOptions> previousOptions,
        IReadOnlyList<SearchOptions> currentOptions)
    {
        if (changedLevelIndex <= 0 || currentOptions.Count == 0 || _searchStale)
        {
            return 0;
        }

        int requiredPrefixCount = Math.Min(changedLevelIndex, currentOptions.Count);
        if (requiredPrefixCount == 0 || previousOptions.Count < requiredPrefixCount)
        {
            return 0;
        }

        for (int i = 0; i < requiredPrefixCount; i++)
        {
            if (!previousOptions[i].Equals(currentOptions[i]))
            {
                return 0;
            }
        }

        return GetFilteredReaderPrefix(requiredPrefixCount) is null ? 0 : changedLevelIndex;
    }

    private bool TryCompleteSearchFromExistingPrefix(int optionCount)
    {
        if (optionCount <= 0)
        {
            return false;
        }

        FilteredLogRecordSource[]? prefixReaders = GetFilteredReaderPrefix(optionCount);
        if (prefixReaders is null || prefixReaders.Length == 0)
        {
            return false;
        }

        _searchStale = false;
        _searchInProgress = false;
        _searchDisplayActive = true;
        _searchProgressPercentage = 100d;
        _searchMatchedLineCount = prefixReaders[^1].MatchedLineCount;
        _searchErrorText = string.Empty;
        SetSearchResultStatusFromLevel(optionCount, string.Empty);
        UpdateLayout();
        return true;
    }

    private bool TryValidateSearchOptions(IReadOnlyList<SearchOptions> options, int searchStartLevel = 0)
    {
        for (int i = 0; i < options.Count; i++)
        {
            SearchOptions option = options[i];
            try
            {
                LogSearchBuilder.ValidateOptions(option);
            }
            catch (ArgumentException ex) when (option.UseRegex)
            {
                string query = FormatSearchQuerySummary(options);
                _searchInProgress = false;
                _searchDisplayActive = true;
                _searchErrorText = "Regex error";
                int affectedStartLevel = searchStartLevel > 0 ? Math.Max(searchStartLevel, i) : 0;
                SetSearchResultStatusFromLevel(affectedStartLevel, string.Empty, disposeReader: false, preserveColumnWidths: true);
                UpdateLayout();
                AppLog.Instance.Error(
                    "search.failed",
                    "failed",
                    new LogField("requestId", _latestSearchRequestId.ToString()),
                    new LogField("query", query),
                    new LogField("queryLength", query.Length.ToString()),
                    new LogField("searchLevelCount", options.Count.ToString()),
                    new LogField("failedSearchLevel", (i + 1).ToString()),
                    new LogField("useRegex", option.UseRegex.ToString()),
                    new LogField("ignoreCase", option.IgnoreCase.ToString()),
                    new LogField("invertMatch", option.InvertMatch.ToString()),
                    new LogField("durationMs", "0"),
                    new LogField("reason", ex.Message));
                return false;
            }
        }

        return true;
    }

    private void OnTimer(nuint timerId)
    {
        if (timerId != SearchDebounceTimerId)
        {
            return;
        }

        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        DispatchPendingSearch();
    }

    private void DispatchPendingSearch()
    {
        SearchOptions[] options = GetActiveSearchOptions();
        int searchStartLevel = _pendingSearchStartLevel;
        _pendingSearchStartLevel = 0;
        if (searchStartLevel > 0 && searchStartLevel < options.Length)
        {
            FilteredLogRecordSource[]? prefixReaders = GetFilteredReaderPrefix(searchStartLevel);
            if (prefixReaders is not null)
            {
                DispatchChangedSearch(_latestSearchRequestId, prefixReaders, searchStartLevel, options);
                return;
            }
        }

        DispatchSearch(_latestSearchRequestId, options);
    }

    private void DispatchSearch(long requestId, SearchOptions[] options)
    {
        if (_closing || options.Length == 0 || _detectedEncoding is not DetectedEncodingInfo detected)
        {
            return;
        }

        int[] visibleLines = GetActiveSearchVisibleLineCounts();
        LogContentSource workerSource = _contentSource;
        IntPtr hwnd = _hwnd;
        SearchOptions[] workerOptions = CopySearchOptions(options);
        string query = FormatSearchQuerySummary(workerOptions);
        bool useRegex = AnySearchOptionUsesRegex(workerOptions);
        bool ignoreCase = AnySearchOptionIgnoresCase(workerOptions);
        bool invertMatch = AnySearchOptionInvertsMatch(workerOptions);
        DisplayParserRule? workerParserRule = GetEffectiveParserRule();
        SearchOptions[] workerManualOptions = GetManualSearchOptions(workerOptions, workerParserRule);
        CancellationTokenSource searchCancellation = BeginSearchCancellation(requestId, searchStartLevel: 0);
        CancellationToken cancellationToken = searchCancellation.Token;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Stopwatch workerStopwatch = Stopwatch.StartNew();
            long lastMatchedLineCount = 0;
            double lastProgressPercentage = 0d;
            int totalVisibleLines = 0;
            foreach (int visibleLineCount in visibleLines)
            {
                totalVisibleLines += visibleLineCount;
            }

            AppLog.Instance.Info(
                "search.begin",
                "begin",
                new LogField("requestId", requestId.ToString()),
                new LogField("query", query),
                new LogField("queryLength", query.Length.ToString()),
                new LogField("searchLevelCount", workerOptions.Length.ToString()),
                new LogField("useRegex", useRegex.ToString()),
                new LogField("ignoreCase", ignoreCase.ToString()),
                new LogField("invertMatch", invertMatch.ToString()),
                new LogField("visibleLines", totalVisibleLines.ToString()));

            try
            {
                LogSearchBuilder.BuildStagedFilteredReadersIncremental(workerSource, detected.Encoding, detected.DataOffset, workerManualOptions, visibleLines, update =>
                {
                    ThrowIfCancelledBeforePostingUpdate(update, cancellationToken);
                    lastMatchedLineCount = GetFinalMatchedLineCount(update.MatchedLineCounts);
                    lastProgressPercentage = update.ProgressPercentage;
                    PostSearchWorkerResult(new SearchWorkerResult
                    {
                        RequestId = requestId,
                        Query = query,
                        SearchLevelCount = workerOptions.Length,
                        UseRegex = useRegex,
                        IgnoreCase = ignoreCase,
                        InvertMatch = invertMatch,
                        Options = CopySearchOptions(workerOptions),
                        Success = true,
                        Readers = update.Readers,
                        PreloadedVisibleLines = visibleLines,
                        ProgressPercentage = update.ProgressPercentage,
                        MatchedLineCount = lastMatchedLineCount,
                        StageMatchedLineCounts = update.MatchedLineCounts,
                        ElapsedMilliseconds = update.ElapsedMilliseconds,
                        IsFinal = update.IsFinal,
                        IsAppendUpdate = false,
                        IsPaused = update.IsPaused,
                        ProcessedOffset = update.ProcessedOffset,
                        TargetFileSize = update.TargetFileSize
                    });
                }, cancellationToken, workerParserRule);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppLog.Instance.Info(
                    "search.cancelled",
                    "cancelled",
                    new LogField("requestId", requestId.ToString()),
                    new LogField("query", query),
                    new LogField("queryLength", query.Length.ToString()),
                    new LogField("searchLevelCount", workerOptions.Length.ToString()),
                    new LogField("useRegex", useRegex.ToString()),
                    new LogField("ignoreCase", ignoreCase.ToString()),
                    new LogField("invertMatch", invertMatch.ToString()),
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
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
                    DisposeReaders(result.Readers);
                    handle.Free();
                }
            }
        });
    }

    private void DispatchChangedSearch(long requestId, IReadOnlyList<FilteredLogRecordSource> prefixReaders, int changedLevelIndex, SearchOptions[] options)
    {
        if (_closing || options.Length == 0 || changedLevelIndex <= 0 || changedLevelIndex >= options.Length)
        {
            return;
        }

        FilteredLogRecordSource[] readerSnapshots = new FilteredLogRecordSource[prefixReaders.Count];
        for (int i = 0; i < prefixReaders.Count; i++)
        {
            readerSnapshots[i] = (FilteredLogRecordSource)prefixReaders[i].CloneForWorker();
        }

        int[] visibleLines = GetActiveSearchVisibleLineCounts();
        IntPtr hwnd = _hwnd;
        SearchOptions[] workerOptions = CopySearchOptions(options);
        string query = FormatSearchQuerySummary(workerOptions);
        bool useRegex = AnySearchOptionUsesRegex(workerOptions);
        bool ignoreCase = AnySearchOptionIgnoresCase(workerOptions);
        bool invertMatch = AnySearchOptionInvertsMatch(workerOptions);
        DisplayParserRule? workerParserRule = GetEffectiveParserRule();
        SearchOptions[] workerManualOptions = GetManualSearchOptions(workerOptions, workerParserRule);
        CancellationTokenSource searchCancellation = BeginSearchCancellation(requestId, changedLevelIndex);
        CancellationToken cancellationToken = searchCancellation.Token;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Stopwatch workerStopwatch = Stopwatch.StartNew();
            long lastMatchedLineCount = 0;
            double lastProgressPercentage = 0d;
            int totalVisibleLines = 0;
            foreach (int visibleLineCount in visibleLines)
            {
                totalVisibleLines += visibleLineCount;
            }

            AppLog.Instance.Info(
                "search.changed.begin",
                "begin",
                new LogField("requestId", requestId.ToString()),
                new LogField("query", query),
                new LogField("queryLength", query.Length.ToString()),
                new LogField("searchLevelCount", workerOptions.Length.ToString()),
                new LogField("changedSearchLevel", (changedLevelIndex + 1).ToString()),
                new LogField("useRegex", useRegex.ToString()),
                new LogField("ignoreCase", ignoreCase.ToString()),
                new LogField("visibleLines", totalVisibleLines.ToString()));

            try
            {
                LogSearchBuilder.BuildChangedStagedFilteredReadersIncremental(readerSnapshots, changedLevelIndex, workerManualOptions, visibleLines, update =>
                {
                    ThrowIfCancelledBeforePostingUpdate(update, cancellationToken);
                    lastMatchedLineCount = GetFinalMatchedLineCount(update.MatchedLineCounts);
                    lastProgressPercentage = update.ProgressPercentage;
                    PostSearchWorkerResult(new SearchWorkerResult
                    {
                        RequestId = requestId,
                        Query = query,
                        SearchLevelCount = workerOptions.Length,
                        UseRegex = useRegex,
                        IgnoreCase = ignoreCase,
                        InvertMatch = invertMatch,
                        Options = CopySearchOptions(workerOptions),
                        Success = true,
                        Readers = update.Readers,
                        PreloadedVisibleLines = visibleLines,
                        ProgressPercentage = update.ProgressPercentage,
                        MatchedLineCount = lastMatchedLineCount,
                        StageMatchedLineCounts = update.MatchedLineCounts,
                        SearchStartLevel = changedLevelIndex,
                        ElapsedMilliseconds = update.ElapsedMilliseconds,
                        IsFinal = update.IsFinal,
                        IsAppendUpdate = false,
                        IsPaused = update.IsPaused,
                        ProcessedOffset = update.ProcessedOffset,
                        TargetFileSize = update.TargetFileSize
                    });
                }, cancellationToken, workerParserRule);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppLog.Instance.Info(
                    "search.changed.cancelled",
                    "cancelled",
                    new LogField("requestId", requestId.ToString()),
                    new LogField("query", query),
                    new LogField("queryLength", query.Length.ToString()),
                    new LogField("searchLevelCount", workerOptions.Length.ToString()),
                    new LogField("changedSearchLevel", (changedLevelIndex + 1).ToString()),
                    new LogField("useRegex", useRegex.ToString()),
                    new LogField("ignoreCase", ignoreCase.ToString()),
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
                    Success = false,
                    IsStale = true,
                    Message = ex.Message,
                    ProgressPercentage = _searchProgressPercentage,
                    MatchedLineCount = _searchMatchedLineCount,
                    SearchStartLevel = changedLevelIndex,
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
                    Success = false,
                    Message = ex.Message,
                    ProgressPercentage = _searchProgressPercentage,
                    MatchedLineCount = _searchMatchedLineCount,
                    SearchStartLevel = changedLevelIndex,
                    ElapsedMilliseconds = workerStopwatch.ElapsedMilliseconds,
                    IsFinal = true,
                    IsAppendUpdate = false
                });
            }
            finally
            {
                foreach (FilteredLogRecordSource readerSnapshot in readerSnapshots)
                {
                    readerSnapshot.Dispose();
                }

                DisposeSearchCancellation(searchCancellation);
            }

            void PostSearchWorkerResult(SearchWorkerResult result)
            {
                GCHandle handle = GCHandle.Alloc(result);
                if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_SEARCH_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
                {
                    DisposeReaders(result.Readers);
                    handle.Free();
                }
            }
        });
    }

    private void QueueAppendSearchIfNeeded()
    {
        if (_closing || !HasActiveSearch)
        {
            _appendSearchPending = false;
            return;
        }

        if (!TryGetCurrentFileSize(out long currentFileSize))
        {
            return;
        }

        if (currentFileSize == 0)
        {
            HandleObservedZeroFileSize();
            return;
        }

        if (TryResumePausedSearch(currentFileSize))
        {
            return;
        }

        if (_waitingForPausedSearchRequestId != 0)
        {
            return;
        }

        bool wasObservedZeroSearchStale = !_searchStale && _searchErrorText == "Search stale";
        if (wasObservedZeroSearchStale)
        {
            _mainPane?.ClearReaderObservedZero();
            _searchErrorText = string.Empty;
            _searchMatchedLineCount = ClearActiveSearchReadersObservedZero();
            InvalidateSearchBar();
        }

        FilteredLogRecordSource[]? currentReaders = GetActiveFilteredReaders();
        long knownSearchFileSize = currentReaders is { Length: > 0 }
            ? currentReaders[0].ConfirmedFileSize
            : ((_mainPane?.Reader as ProjectedViewport)?.ConfirmedFileSize ?? currentFileSize);
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

        if (currentReaders is null || currentReaders.Length == 0)
        {
            if (wasObservedZeroSearchStale)
            {
                DispatchFullSearchAfterObservedZeroRestore();
            }

            return;
        }

        SearchOptions[] options = GetActiveSearchOptions();
        if (!AreSearchOptionsValid(options))
        {
            return;
        }

        if (currentFileSize <= currentReaders[0].ConfirmedFileSize)
        {
            if (wasObservedZeroSearchStale)
            {
                _searchProgressPercentage = 100d;
                InvalidateSearchBar();
            }

            return;
        }

        DispatchAppendSearch(currentReaders, currentFileSize, options);
    }

    private void QueueSearchReloadAfterSameSizeChange()
    {
        if (_closing || !HasActiveSearch || _searchStale)
        {
            return;
        }

        if (!TryGetCurrentFileSize(out long currentFileSize))
        {
            return;
        }

        if (_waitingForPausedSearchRequestId != 0)
        {
            return;
        }

        FilteredLogRecordSource[]? currentReaders = GetActiveFilteredReaders();
        if (currentReaders is null || currentReaders.Length == 0)
        {
            return;
        }

        if (currentFileSize != currentReaders[0].FileSize)
        {
            return;
        }

        for (int i = 0; i < _activeSearchLevelCount && i < _searchLevels.Count; i++)
        {
            if (_searchLevels[i].ResultsPane?.Reader is ProjectedViewport { Source: FilteredLogRecordSource })
            {
                _searchLevels[i].ResultsPane!.QueueReloadAfterFileChange();
            }
        }
    }

    private bool TryGetCurrentFileSize(out long currentFileSize)
    {
        try
        {
            currentFileSize = _contentSource.Length;
            RememberObservedContentFileSize(currentFileSize);
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

    private void RememberObservedContentFileSize(long fileSize)
    {
        if (fileSize > 0 || _knownContentFileSize == 0)
        {
            _knownContentFileSize = Math.Max(0, fileSize);
        }
    }

    private long GetKnownContentFileSize(long dataOffset)
    {
        long fileSize = _knownContentFileSize;
        if (_mainPane?.Reader is ProjectedViewport mainReader)
        {
            fileSize = Math.Max(fileSize, mainReader.ConfirmedFileSize);
        }

        FilteredLogRecordSource[]? currentReaders = GetActiveFilteredReaders();
        if (currentReaders is { Length: > 0 })
        {
            fileSize = Math.Max(fileSize, currentReaders[0].ConfirmedFileSize);
        }

        return Math.Max(dataOffset, fileSize);
    }

    private static bool AreSearchOptionsValid(IReadOnlyList<SearchOptions> options)
    {
        foreach (SearchOptions option in options)
        {
            try
            {
                LogSearchBuilder.ValidateOptions(option);
            }
            catch (ArgumentException) when (option.UseRegex)
            {
                return false;
            }
        }

        return true;
    }

    private void DispatchAppendSearch(FilteredLogRecordSource[] currentReaders, long newFileSize, SearchOptions[] options)
    {
        long requestId = _latestSearchRequestId;
        int[] visibleLines = GetActiveSearchVisibleLineCounts();
        FilteredLogRecordSource[] readerSnapshots = new FilteredLogRecordSource[currentReaders.Length];
        for (int i = 0; i < currentReaders.Length; i++)
        {
            readerSnapshots[i] = (FilteredLogRecordSource)currentReaders[i].CloneForWorker();
        }

        long initialMatchedLineCount = readerSnapshots.Length == 0 ? 0 : readerSnapshots[^1].MatchedLineCount;
        IntPtr hwnd = _hwnd;
        SearchOptions[] workerOptions = CopySearchOptions(options);
        string query = FormatSearchQuerySummary(workerOptions);
        bool useRegex = AnySearchOptionUsesRegex(workerOptions);
        bool ignoreCase = AnySearchOptionIgnoresCase(workerOptions);
        bool invertMatch = AnySearchOptionInvertsMatch(workerOptions);
        DisplayParserRule? workerParserRule = GetEffectiveParserRule();
        SearchOptions[] workerManualOptions = GetManualSearchOptions(workerOptions, workerParserRule);
        CancellationTokenSource searchCancellation = BeginSearchCancellation(requestId, searchStartLevel: 0);
        CancellationToken cancellationToken = searchCancellation.Token;
        _appendSearchPending = false;
        _appendSearchInProgress = true;
        _searchInProgress = true;
        _searchDisplayActive = true;
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
                new LogField("searchLevelCount", workerOptions.Length.ToString()),
                new LogField("useRegex", useRegex.ToString()),
                new LogField("ignoreCase", ignoreCase.ToString()),
                new LogField("invertMatch", invertMatch.ToString()),
                new LogField("oldFileSize", readerSnapshots[0].ConfirmedFileSize.ToString()),
                new LogField("newFileSize", newFileSize.ToString()));

            try
            {
                LogSearchBuilder.BuildAppendedStagedFilteredReadersIncremental(readerSnapshots, workerManualOptions, newFileSize, visibleLines, update =>
                {
                    ThrowIfCancelledBeforePostingUpdate(update, cancellationToken);
                    lastMatchedLineCount = GetFinalMatchedLineCount(update.MatchedLineCounts);
                    lastProgressPercentage = update.ProgressPercentage;
                    PostSearchWorkerResult(new SearchWorkerResult
                    {
                        RequestId = requestId,
                        Query = query,
                        SearchLevelCount = workerOptions.Length,
                        UseRegex = useRegex,
                        IgnoreCase = ignoreCase,
                        InvertMatch = invertMatch,
                        Options = CopySearchOptions(workerOptions),
                        Success = true,
                        Readers = update.Readers,
                        PreloadedVisibleLines = visibleLines,
                        ProgressPercentage = update.ProgressPercentage,
                        MatchedLineCount = lastMatchedLineCount,
                        StageMatchedLineCounts = update.MatchedLineCounts,
                        ElapsedMilliseconds = update.ElapsedMilliseconds,
                        IsFinal = update.IsFinal,
                        IsAppendUpdate = true,
                        IsPaused = update.IsPaused,
                        ProcessedOffset = update.ProcessedOffset,
                        TargetFileSize = update.TargetFileSize
                    });
                }, cancellationToken, workerParserRule);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppLog.Instance.Info(
                    "search.append.cancelled",
                    "cancelled",
                    new LogField("requestId", requestId.ToString()),
                    new LogField("query", query),
                    new LogField("queryLength", query.Length.ToString()),
                    new LogField("searchLevelCount", workerOptions.Length.ToString()),
                    new LogField("useRegex", useRegex.ToString()),
                    new LogField("ignoreCase", ignoreCase.ToString()),
                    new LogField("invertMatch", invertMatch.ToString()),
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
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
                foreach (FilteredLogRecordSource readerSnapshot in readerSnapshots)
                {
                    readerSnapshot.Dispose();
                }

                DisposeSearchCancellation(searchCancellation);
            }

            void PostSearchWorkerResult(SearchWorkerResult result)
            {
                GCHandle handle = GCHandle.Alloc(result);
                if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_SEARCH_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
                {
                    DisposeReaders(result.Readers);
                    handle.Free();
                }
            }
        });
    }

    private void DispatchResumeSearch(FilteredLogRecordSource[] pausedReaders, long newFileSize, long processedOffset, SearchOptions[] options)
    {
        long requestId = _latestSearchRequestId = ++_nextSearchRequestId;
        int[] visibleLines = GetActiveSearchVisibleLineCounts();
        FilteredLogRecordSource[] readerSnapshots = new FilteredLogRecordSource[pausedReaders.Length];
        for (int i = 0; i < pausedReaders.Length; i++)
        {
            readerSnapshots[i] = (FilteredLogRecordSource)pausedReaders[i].CloneForWorker();
        }

        long initialMatchedLineCount = readerSnapshots.Length == 0 ? 0 : readerSnapshots[^1].MatchedLineCount;
        IntPtr hwnd = _hwnd;
        SearchOptions[] workerOptions = CopySearchOptions(options);
        string query = FormatSearchQuerySummary(workerOptions);
        bool useRegex = AnySearchOptionUsesRegex(workerOptions);
        bool ignoreCase = AnySearchOptionIgnoresCase(workerOptions);
        bool invertMatch = AnySearchOptionInvertsMatch(workerOptions);
        DisplayParserRule? workerParserRule = GetEffectiveParserRule();
        SearchOptions[] workerManualOptions = GetManualSearchOptions(workerOptions, workerParserRule);
        CancellationTokenSource searchCancellation = BeginSearchCancellation(requestId, searchStartLevel: 0);
        CancellationToken cancellationToken = searchCancellation.Token;
        _appendSearchPending = false;
        _appendSearchInProgress = false;
        _searchInProgress = true;
        _searchDisplayActive = true;
        _searchErrorText = string.Empty;
        InvalidateSearchBar();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            Stopwatch workerStopwatch = Stopwatch.StartNew();
            long lastMatchedLineCount = initialMatchedLineCount;
            double lastProgressPercentage = _searchProgressPercentage;
            AppLog.Instance.Info(
                "search.resume.begin",
                "begin",
                new LogField("requestId", requestId.ToString()),
                new LogField("query", query),
                new LogField("queryLength", query.Length.ToString()),
                new LogField("searchLevelCount", workerOptions.Length.ToString()),
                new LogField("useRegex", useRegex.ToString()),
                new LogField("ignoreCase", ignoreCase.ToString()),
                new LogField("invertMatch", invertMatch.ToString()),
                new LogField("processedOffset", processedOffset.ToString()),
                new LogField("newFileSize", newFileSize.ToString()));

            try
            {
                LogSearchBuilder.ResumeStagedFilteredReadersIncremental(readerSnapshots, workerManualOptions, processedOffset, newFileSize, visibleLines, update =>
                {
                    ThrowIfCancelledBeforePostingUpdate(update, cancellationToken);
                    lastMatchedLineCount = GetFinalMatchedLineCount(update.MatchedLineCounts);
                    lastProgressPercentage = update.ProgressPercentage;
                    PostSearchWorkerResult(new SearchWorkerResult
                    {
                        RequestId = requestId,
                        Query = query,
                        SearchLevelCount = workerOptions.Length,
                        UseRegex = useRegex,
                        IgnoreCase = ignoreCase,
                        InvertMatch = invertMatch,
                        Options = CopySearchOptions(workerOptions),
                        Success = true,
                        Readers = update.Readers,
                        PreloadedVisibleLines = visibleLines,
                        ProgressPercentage = update.ProgressPercentage,
                        MatchedLineCount = lastMatchedLineCount,
                        StageMatchedLineCounts = update.MatchedLineCounts,
                        ElapsedMilliseconds = update.ElapsedMilliseconds,
                        IsFinal = update.IsFinal,
                        IsAppendUpdate = false,
                        IsPaused = update.IsPaused,
                        ProcessedOffset = update.ProcessedOffset,
                        TargetFileSize = update.TargetFileSize
                    });
                }, cancellationToken, workerParserRule);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppLog.Instance.Info(
                    "search.resume.cancelled",
                    "cancelled",
                    new LogField("requestId", requestId.ToString()),
                    new LogField("query", query),
                    new LogField("queryLength", query.Length.ToString()),
                    new LogField("searchLevelCount", workerOptions.Length.ToString()),
                    new LogField("useRegex", useRegex.ToString()),
                    new LogField("ignoreCase", ignoreCase.ToString()),
                    new LogField("invertMatch", invertMatch.ToString()),
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
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
                    SearchLevelCount = workerOptions.Length,
                    UseRegex = useRegex,
                    IgnoreCase = ignoreCase,
                    InvertMatch = invertMatch,
                    Options = CopySearchOptions(workerOptions),
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
                foreach (FilteredLogRecordSource readerSnapshot in readerSnapshots)
                {
                    readerSnapshot.Dispose();
                }

                DisposeSearchCancellation(searchCancellation);
            }

            void PostSearchWorkerResult(SearchWorkerResult result)
            {
                GCHandle handle = GCHandle.Alloc(result);
                if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_SEARCH_COMPLETE, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
                {
                    DisposeReaders(result.Readers);
                    handle.Free();
                }
            }
        });
    }

    private void MarkSearchStale()
    {
        _latestSearchRequestId = ++_nextSearchRequestId;
        CancelActiveSearch();
        ClearPausedSearchCheckpoint();
        ClearPendingLowerSearchChange();
        _appendSearchPending = false;
        _appendSearchInProgress = false;
        _searchInProgress = false;
        _searchStale = true;
        _searchDisplayActive = true;
        _searchProgressPercentage = 0d;
        _searchMatchedLineCount = 0;
        _searchErrorText = "Search stale";
        SetActiveSearchResultStatus(string.Empty);
        UpdateLayout();
    }

    private static bool IsFilteredLineStaleMessage(string? message)
    {
        return message is not null &&
            message.StartsWith("Filtered line ", StringComparison.Ordinal);
    }

    private void OnFilteredPaneStale(ViewportPaneWindow pane)
    {
        if (FindSearchLevelByResultsPane(pane) is null || _closing)
        {
            return;
        }

        MarkSearchStale();
    }

    private void OnFilteredRowActivated(ViewportPaneWindow pane, ViewportRowSelectionKey rowKey)
    {
        int sourceIndex = FindSearchLevelIndexByResultsPane(pane);
        if (sourceIndex < 0 || _closing || _mainPane is null)
        {
            return;
        }

        _mainPane.JumpToFileOffset(rowKey.StartOffset, selectRowAfterLoad: true);
        int upperLevelCount = Math.Min(sourceIndex, GetActiveSearchResultCount());
        for (int i = 0; i < upperLevelCount; i++)
        {
            _searchLevels[i].ResultsPane?.SynchronizeToSearchResult(rowKey);
        }
    }

    private CancellationTokenSource BeginSearchCancellation(long requestId, int searchStartLevel)
    {
        var nextCancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation;
        lock (_searchCancellationSync)
        {
            previousCancellation = _activeSearchCancellation;
            _activeSearchCancellation = nextCancellation;
            _activeSearchWorkerRequestId = requestId;
            _activeSearchWorkerStartLevel = searchStartLevel;
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

    private bool CancelActiveSearch()
    {
        CancellationTokenSource? cancellation;
        lock (_searchCancellationSync)
        {
            cancellation = _activeSearchCancellation;
            _activeSearchCancellation = null;
            _activeSearchWorkerRequestId = 0;
            _activeSearchWorkerStartLevel = 0;
        }

        if (cancellation is null)
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void DisposeSearchCancellation(CancellationTokenSource cancellation)
    {
        lock (_searchCancellationSync)
        {
            if (ReferenceEquals(_activeSearchCancellation, cancellation))
            {
                _activeSearchCancellation = null;
                _activeSearchWorkerRequestId = 0;
                _activeSearchWorkerStartLevel = 0;
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

            DisposeReaders(result.Readers);
            return;
        }

        if (result.RequestId != _latestSearchRequestId || !HasActiveSearch)
        {
            if (result.IsAppendUpdate)
            {
                _appendSearchInProgress = false;
            }

            if (result.IsFinal)
            {
                LogSearchDiscarded(result);
            }

            DisposeReaders(result.Readers);
            return;
        }

        if (_waitingForPausedSearchRequestId != 0)
        {
            if (result.IsPaused && _waitingForPausedSearchRequestId == result.RequestId)
            {
                StorePausedSearchCheckpoint(result);
                if (TryGetCurrentFileSize(out long restoredFileSize) && restoredFileSize > 0)
                {
                    TryResumePausedSearch(restoredFileSize);
                }

                return;
            }

            bool isWaitingRequest = _waitingForPausedSearchRequestId == result.RequestId;
            if (result.IsFinal)
            {
                LogSearchDiscarded(result);
                if (isWaitingRequest)
                {
                    _waitingForPausedSearchRequestId = 0;
                }
            }

            DisposeReaders(result.Readers);
            if (isWaitingRequest && result.IsFinal && TryGetCurrentFileSize(out long nonZeroFileSizeAfterFinal) && nonZeroFileSizeAfterFinal > 0)
            {
                QueueAppendSearchIfNeeded();
                QueueSearchReloadAfterSameSizeChange();
                UpdateLayout();
            }

            return;
        }

        if (!_searchStale && TryGetCurrentFileSize(out long currentFileSize) && currentFileSize == 0)
        {
            if (result.IsAppendUpdate)
            {
                _appendSearchInProgress = false;
            }

            if (result.IsFinal)
            {
                _searchInProgress = false;
            }

            DisposeReaders(result.Readers);
            HandleObservedZeroFileSize();
            return;
        }

        if (result.IsPaused)
        {
            DisposeReaders(result.Readers);
            return;
        }

        int applyEndLevel = GetSearchUpdateEndLevel(result);
        DisposeReadersFromLevel(result.Readers, applyEndLevel);
        _searchProgressPercentage = Math.Clamp(result.ProgressPercentage, 0d, 100d);
        _searchMatchedLineCount = GetSearchUpdateMatchedLineCount(result);
        _searchInProgress = !result.IsFinal;
        _searchDisplayActive = true;
        _searchErrorText = string.Empty;

        if (!result.Success)
        {
            if (result.IsAppendUpdate)
            {
                _appendSearchInProgress = false;
            }

            if (result.IsStale || IsFilteredLineStaleMessage(result.Message))
            {
                DisposeReaders(result.Readers);
                MarkSearchStale();
                return;
            }

            _searchInProgress = false;
            _searchDisplayActive = true;
            _searchErrorText = result.UseRegex ? "Regex error" : "Search error";
            DisposeReaders(result.Readers);
            SetActiveSearchResultStatus(string.Empty, disposeReader: false, preserveColumnWidths: true);
            InvalidateSearchBar();
            AppLog.Instance.Error(
                "search.failed",
                "failed",
                new LogField("requestId", result.RequestId.ToString()),
                new LogField("query", result.Query),
                new LogField("queryLength", result.Query.Length.ToString()),
                new LogField("searchLevelCount", result.SearchLevelCount.ToString()),
                new LogField("useRegex", result.UseRegex.ToString()),
                new LogField("ignoreCase", result.IgnoreCase.ToString()),
                new LogField("invertMatch", result.InvertMatch.ToString()),
                new LogField("durationMs", result.ElapsedMilliseconds.ToString()),
                new LogField("reason", result.Message ?? "unknown error"));
            return;
        }

        if (result.Readers is not null)
        {
            try
            {
                ApplySearchResultReaders(result, markObservedZero: false);
            }
            catch (FilteredLineStaleException)
            {
                DisposeReaders(result.Readers);
                MarkSearchStale();
                return;
            }
            catch
            {
                DisposeReaders(result.Readers);
                throw;
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
                new LogField("searchLevelCount", result.SearchLevelCount.ToString()),
                new LogField("useRegex", result.UseRegex.ToString()),
                new LogField("ignoreCase", result.IgnoreCase.ToString()),
                new LogField("invertMatch", result.InvertMatch.ToString()),
                new LogField("durationMs", result.ElapsedMilliseconds.ToString()),
                new LogField("matchedLineCount", result.MatchedLineCount.ToString()),
                new LogField("progressPercentage", Math.Round(_searchProgressPercentage).ToString()));

            if (TryDispatchPendingLowerSearchChange(result))
            {
                UpdateLayout();
                return;
            }

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
            new LogField("searchLevelCount", result.SearchLevelCount.ToString()),
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
            new LogField("source", pane.Reader.SourceName),
            new LogField("encoding", pane.Reader.EncodingName),
            new LogField("dataOffset", pane.Reader.DataOffset.ToString()),
            new LogField("fileSize", pane.Reader.FileSize.ToString()),
            new LogField("topOffset", pane.Reader.TopOffset.ToString()),
            new LogField("visibleLines", pane.VisibleLineCount.ToString()),
            new LogField("viewportBytes", pane.Reader.ViewportBytes.ToString()));
    }

    private static IntPtr CreateContentFont(int weight, bool italic)
    {
        return NativeMethods.CreateFontW(
            -13,
            0,
            0,
            0,
            weight,
            italic ? 1u : 0u,
            0,
            0,
            NativeMethods.DEFAULT_CHARSET,
            NativeMethods.OUT_OUTLINE_PRECIS,
            NativeMethods.CLIP_DEFAULT_PRECIS,
            NativeMethods.CLEARTYPE_QUALITY,
            NativeMethods.FF_MODERN,
            "Consolas");
    }

    private static IntPtr CreateParserInputVisibilityFont()
    {
        return NativeMethods.CreateFontW(
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
            0,
            "Segoe MDL2 Assets");
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

    private int GetPreferredSearchAreaHeight(int clientHeight)
    {
        if (clientHeight <= 0)
        {
            return 0;
        }

        int fixedHeight = GetSearchFixedAreaHeight();
        if (GetVisibleActiveSearchResultCount() == 0)
        {
            return Math.Min(clientHeight, fixedHeight);
        }

        int requestedHeight = (int)Math.Round(clientHeight * GetSearchAreaRatio());
        return ClampSearchAreaHeight(requestedHeight, clientHeight);
    }

    private double GetSearchAreaRatio()
    {
        return GetSearchAreaRatio(GetVisibleActiveSearchResultCount());
    }

    private double GetSearchAreaRatio(int activeSearchCount)
    {
        if (_parserFilterLevelCount > 0 && !IsParserInputVisible(0))
        {
            return 1d;
        }

        double defaultRatio = GetDefaultSearchAreaRatio(activeSearchCount);
        double ratio = _customSearchAreaRatio ?? defaultRatio;
        return double.IsFinite(ratio)
            ? Math.Clamp(ratio, 0d, 1d)
            : defaultRatio;
    }

    private static double GetDefaultSearchAreaRatio(int activeSearchCount) =>
        activeSearchCount > 0 ? activeSearchCount / (activeSearchCount + 1d) : 0d;

    private int ClampSearchAreaHeight(int requestedHeight, int clientHeight)
    {
        if (clientHeight <= 0)
        {
            return 0;
        }

        return Math.Clamp(requestedHeight, 0, clientHeight);
    }

    private int GetSearchFixedAreaHeight()
    {
        int levelCount = Math.Max(1, _searchLevels.Count);
        int activeCount = Math.Min(GetVisibleActiveSearchResultCount(), levelCount);
        return (SearchInnerPadding * 2) +
            (levelCount * SearchInputRowHeight) +
            (Math.Max(0, levelCount - 1) * SearchLevelRowGap) +
            (activeCount * SearchLevelRowGap) +
            SearchProgressGap +
            SearchProgressRowHeight;
    }

    private int GetAvailableSearchResultHeight(int searchAreaHeight)
        => Math.Max(0, searchAreaHeight - GetSearchFixedAreaHeight());

    private double[] GetNormalizedSearchResultRatios(int activeSearchCount)
    {
        int activeCount = Math.Clamp(activeSearchCount, 0, _searchLevels.Count);
        double[] ratios = new double[activeCount];
        if (activeCount == 0)
        {
            return ratios;
        }

        double defaultRatio = 1d / activeCount;
        double total = 0d;
        for (int i = 0; i < ratios.Length; i++)
        {
            double ratio = _searchLevels[i].CustomResultRatio ?? defaultRatio;
            ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : defaultRatio;
            ratios[i] = ratio;
            total += ratio;
        }

        if (total <= 0d)
        {
            Array.Fill(ratios, defaultRatio);
            return ratios;
        }

        for (int i = 0; i < ratios.Length; i++)
        {
            ratios[i] /= total;
        }

        return ratios;
    }

    private double[] GetNormalizedVisibleSearchResultRatios()
    {
        int activeCount = GetActiveSearchResultCount();
        double[] ratios = new double[activeCount];
        int[] visibleIndices = GetVisibleActiveSearchResultIndices();
        if (visibleIndices.Length == 0)
        {
            return ratios;
        }

        double defaultRatio = 1d / visibleIndices.Length;
        double total = 0d;
        foreach (int index in visibleIndices)
        {
            double ratio = _searchLevels[index].CustomResultRatio ?? defaultRatio;
            ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : defaultRatio;
            ratios[index] = ratio;
            total += ratio;
        }

        if (total <= 0d)
        {
            foreach (int index in visibleIndices)
            {
                ratios[index] = defaultRatio;
            }

            return ratios;
        }

        foreach (int index in visibleIndices)
        {
            ratios[index] /= total;
        }

        return ratios;
    }

    private void ApplySearchResultRatioResize(int changedIndex, double requestedRatio, IReadOnlyList<double> startRatios)
    {
        int[] visibleIndices = GetVisibleActiveSearchResultIndices();
        if (visibleIndices.Length == 0 || Array.IndexOf(visibleIndices, changedIndex) < 0)
        {
            return;
        }

        if (visibleIndices.Length == 1)
        {
            _searchLevels[changedIndex].CustomResultRatio = 1d;
            return;
        }

        double changedRatio = Math.Clamp(requestedRatio, 0d, 1d);
        double remainingRatio = Math.Max(0d, 1d - changedRatio);
        double remainingWeight = 0d;
        foreach (int index in visibleIndices)
        {
            if (index != changedIndex)
            {
                double ratio = index < startRatios.Count ? startRatios[index] : 0d;
                remainingWeight += Math.Max(0d, ratio);
            }
        }

        foreach (int index in visibleIndices)
        {
            if (index == changedIndex)
            {
                _searchLevels[index].CustomResultRatio = changedRatio;
                continue;
            }

            double startRatio = index < startRatios.Count ? startRatios[index] : 0d;
            double weight = Math.Max(0d, startRatio);
            double redistributedRatio = remainingWeight > 0d
                ? remainingRatio * (weight / remainingWeight)
                : remainingRatio / (visibleIndices.Length - 1);
            _searchLevels[index].CustomResultRatio = Math.Clamp(redistributedRatio, 0d, 1d);
        }
    }

    private int[] CalculateSearchResultHeights(int availableResultHeight)
    {
        int activeCount = GetActiveSearchResultCount();
        int[] heights = new int[activeCount];
        if (activeCount == 0)
        {
            return heights;
        }

        double[] ratios = GetNormalizedVisibleSearchResultRatios();
        for (int i = 0; i < heights.Length; i++)
        {
            double ratio = i < ratios.Length ? ratios[i] : 0d;
            int desiredHeight = (int)Math.Round(Math.Max(0, availableResultHeight) * ratio);
            heights[i] = Math.Max(0, desiredHeight);
        }

        return heights;
    }

    private void UpdateLayout(LayoutRedrawMode redrawMode = LayoutRedrawMode.Deferred)
    {
        RecalculateLayout();
        ApplyLayout(redrawMode);
    }

    private void RecalculateLayout()
    {
        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        int clientHeight = GetRectHeight(clientRect);
        int contentHeight = Math.Max(0, clientHeight - TopBarHeight);
        int searchAreaHeight = GetPreferredSearchAreaHeight(contentHeight);
        int availableResultHeight = GetAvailableSearchResultHeight(searchAreaHeight);
        int[] searchResultHeights = CalculateSearchResultHeights(availableResultHeight);
        int topBarBottom = Math.Min(clientRect.bottom, clientRect.top + TopBarHeight);
        NativeMethods.RECT topBarRect = new()
        {
            left = clientRect.left,
            top = clientRect.top,
            right = clientRect.right,
            bottom = topBarBottom
        };
        int controlTop = topBarRect.top + TopBarPadding;
        int controlBottom = Math.Min(topBarRect.bottom, controlTop + SearchInputRowHeight);
        int controlsRight = Math.Max(topBarRect.left + TopBarPadding, topBarRect.right - TopBarPadding);
        int controlCursor = topBarRect.left + TopBarPadding;
        int openButtonWidth = Math.Min(OpenButtonWidth, Math.Max(0, controlsRight - controlCursor));
        NativeMethods.RECT openButtonRect = new()
        {
            left = controlCursor,
            top = controlTop,
            right = controlCursor + openButtonWidth,
            bottom = controlBottom
        };
        if (openButtonWidth > 0)
        {
            controlCursor = openButtonRect.right + SearchToggleGap;
        }

        int pasteButtonWidth = Math.Min(PasteButtonWidth, Math.Max(0, controlsRight - controlCursor));
        NativeMethods.RECT pasteButtonRect = new()
        {
            left = controlCursor,
            top = controlTop,
            right = controlCursor + pasteButtonWidth,
            bottom = controlBottom
        };
        if (pasteButtonWidth > 0)
        {
            controlCursor = pasteButtonRect.right + SearchToggleGap;
        }

        int parserComboLeft = controlCursor;
        int availableControlsWidth = Math.Max(0, controlsRight - parserComboLeft);
        int highlightingWidth = Math.Min(HighlightingButtonWidth, availableControlsWidth);
        int highlightingGap = highlightingWidth > 0 ? SearchToggleGap : 0;
        int parserComboWidth = Math.Min(ParserComboWidth, Math.Max(0, availableControlsWidth - highlightingWidth - highlightingGap));
        NativeMethods.RECT parserComboRect = new()
        {
            left = parserComboLeft,
            top = controlTop,
            right = parserComboLeft + parserComboWidth,
            bottom = controlTop + 220
        };
        NativeMethods.RECT highlightingButtonRect = new()
        {
            left = parserComboRect.right + highlightingGap,
            top = controlTop,
            right = Math.Min(controlsRight, parserComboRect.right + highlightingGap + highlightingWidth),
            bottom = controlBottom
        };
        int viewerBottom = Math.Max(topBarBottom, clientRect.bottom - searchAreaHeight);
        int searchAreaTop = viewerBottom;
        int searchAreaBottom = Math.Min(clientRect.bottom, searchAreaTop + searchAreaHeight);
        NativeMethods.RECT viewerRect = new()
        {
            left = clientRect.left,
            top = topBarBottom,
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

        NativeMethods.RECT searchAreaInner = InsetRect(searchAreaRect, SearchInnerPadding);
        SearchLevelLayout[] searchLevelLayouts = new SearchLevelLayout[_searchLevels.Count];
        int rowTop = searchAreaInner.top;
        bool hasVisibleLevel = false;
        for (int i = 0; i < searchLevelLayouts.Length; i++)
        {
            if (hasVisibleLevel)
            {
                rowTop += SearchLevelRowGap;
            }

            int inputBottom = Math.Min(searchAreaInner.bottom, rowTop + SearchInputRowHeight);
            NativeMethods.RECT inputRowRect = new()
            {
                left = searchAreaInner.left,
                top = rowTop,
                right = searchAreaInner.right,
                bottom = inputBottom
            };

            NativeMethods.RECT parserInputVisibilityRect = CreateZeroRect();
            int editLeft = inputRowRect.left;
            if (i < _parserFilterLevelCount)
            {
                int visibilityButtonWidth = Math.Min(
                    ParserInputVisibilityButtonWidth,
                    GetRectWidth(inputRowRect));
                parserInputVisibilityRect = new NativeMethods.RECT
                {
                    left = inputRowRect.left,
                    top = inputRowRect.top,
                    right = inputRowRect.left + visibilityButtonWidth,
                    bottom = inputRowRect.bottom
                };
                editLeft = Math.Min(
                    inputRowRect.right,
                    parserInputVisibilityRect.right + SearchLevelRowGap);
            }

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
                left = editLeft,
                top = inputRowRect.top,
                right = Math.Max(inputRowRect.left, regexRect.left - SearchToggleGap),
                bottom = inputRowRect.bottom
            };

            NativeMethods.RECT resultRect = CreateZeroRect();
            rowTop = inputBottom;
            if (i < _activeSearchLevelCount && IsSearchResultVisible(i))
            {
                rowTop += SearchLevelRowGap;
                int resultHeight = i < searchResultHeights.Length ? searchResultHeights[i] : 0;
                int resultBottom = Math.Min(searchAreaInner.bottom, rowTop + resultHeight);
                if (rowTop < resultBottom)
                {
                    resultRect = new NativeMethods.RECT
                    {
                        left = searchAreaInner.left,
                        top = rowTop,
                        right = searchAreaInner.right,
                        bottom = resultBottom
                    };
                }

                rowTop = resultBottom;
            }

            searchLevelLayouts[i] = new SearchLevelLayout(
                parserInputVisibilityRect,
                editRect,
                regexRect,
                ignoreCaseRect,
                invertMatchRect,
                resultRect);
            hasVisibleLevel = true;
        }

        int progressTop = Math.Min(searchAreaInner.bottom, rowTop + SearchProgressGap);
        NativeMethods.RECT progressRect = progressTop < searchAreaInner.bottom
            ? new NativeMethods.RECT
            {
                left = searchAreaInner.left,
                top = progressTop,
                right = searchAreaInner.right,
                bottom = Math.Min(searchAreaInner.bottom, progressTop + SearchProgressRowHeight)
            }
            : CreateZeroRect();

        _layout = new WindowLayout(
            clientRect,
            topBarRect,
            openButtonRect,
            pasteButtonRect,
            parserComboRect,
            highlightingButtonRect,
            viewerRect,
            searchAreaRect,
            searchLevelLayouts,
            progressRect);
    }

    private void ApplyLayout(LayoutRedrawMode redrawMode)
    {
        if (_openButton != IntPtr.Zero)
        {
            NativeMethods.MoveWindow(
                _openButton,
                _layout.OpenButtonRect.left,
                _layout.OpenButtonRect.top,
                GetRectWidth(_layout.OpenButtonRect),
                GetRectHeight(_layout.OpenButtonRect),
                false);
            NativeMethods.ShowWindow(_openButton, GetRectWidth(_layout.OpenButtonRect) > 0 ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        }

        if (_pasteButton != IntPtr.Zero)
        {
            NativeMethods.MoveWindow(
                _pasteButton,
                _layout.PasteButtonRect.left,
                _layout.PasteButtonRect.top,
                GetRectWidth(_layout.PasteButtonRect),
                GetRectHeight(_layout.PasteButtonRect),
                false);
            NativeMethods.ShowWindow(_pasteButton, GetRectWidth(_layout.PasteButtonRect) > 0 ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        }

        if (_parserCombo != IntPtr.Zero)
        {
            NativeMethods.MoveWindow(
                _parserCombo,
                _layout.ParserComboRect.left,
                _layout.ParserComboRect.top,
                GetRectWidth(_layout.ParserComboRect),
                GetRectHeight(_layout.ParserComboRect),
                false);
            NativeMethods.ShowWindow(_parserCombo, GetRectWidth(_layout.ParserComboRect) > 0 ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        }

        if (_highlightingButton != IntPtr.Zero)
        {
            NativeMethods.MoveWindow(
                _highlightingButton,
                _layout.HighlightingButtonRect.left,
                _layout.HighlightingButtonRect.top,
                GetRectWidth(_layout.HighlightingButtonRect),
                GetRectHeight(_layout.HighlightingButtonRect),
                false);
            NativeMethods.ShowWindow(_highlightingButton, GetRectWidth(_layout.HighlightingButtonRect) > 0 ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        }

        _mainPane?.SetBounds(
            _layout.ViewerRect,
            ShouldShowMainPaneForParserInputs(_parserFilterLevelCount, IsParserInputVisible(0)));

        for (int i = 0; i < _searchLevels.Count && i < _layout.SearchLevelLayouts.Length; i++)
        {
            SearchLevelState level = _searchLevels[i];
            SearchLevelLayout levelLayout = _layout.SearchLevelLayouts[i];
            NativeMethods.MoveWindow(
                level.ParserInputVisibilityButton,
                levelLayout.ParserInputVisibilityButtonRect.left,
                levelLayout.ParserInputVisibilityButtonRect.top,
                GetRectWidth(levelLayout.ParserInputVisibilityButtonRect),
                GetRectHeight(levelLayout.ParserInputVisibilityButtonRect),
                false);
            NativeMethods.MoveWindow(
                level.SearchEdit,
                levelLayout.SearchEditRect.left,
                levelLayout.SearchEditRect.top,
                GetRectWidth(levelLayout.SearchEditRect),
                GetRectHeight(levelLayout.SearchEditRect),
                false);
            NativeMethods.MoveWindow(
                level.RegexCheckbox,
                levelLayout.SearchRegexToggleRect.left,
                levelLayout.SearchRegexToggleRect.top,
                GetRectWidth(levelLayout.SearchRegexToggleRect),
                GetRectHeight(levelLayout.SearchRegexToggleRect),
                false);
            NativeMethods.MoveWindow(
                level.IgnoreCaseCheckbox,
                levelLayout.SearchIgnoreCaseToggleRect.left,
                levelLayout.SearchIgnoreCaseToggleRect.top,
                GetRectWidth(levelLayout.SearchIgnoreCaseToggleRect),
                GetRectHeight(levelLayout.SearchIgnoreCaseToggleRect),
                false);
            NativeMethods.MoveWindow(
                level.InvertMatchCheckbox,
                levelLayout.SearchInvertMatchToggleRect.left,
                levelLayout.SearchInvertMatchToggleRect.top,
                GetRectWidth(levelLayout.SearchInvertMatchToggleRect),
                GetRectHeight(levelLayout.SearchInvertMatchToggleRect),
                false);
            bool controlsVisible = !IsZeroRect(levelLayout.SearchEditRect);
            int showControlsCommand = controlsVisible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE;
            NativeMethods.ShowWindow(
                level.ParserInputVisibilityButton,
                IsZeroRect(levelLayout.ParserInputVisibilityButtonRect) ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOW);
            NativeMethods.ShowWindow(level.SearchEdit, showControlsCommand);
            NativeMethods.ShowWindow(level.RegexCheckbox, showControlsCommand);
            NativeMethods.ShowWindow(level.IgnoreCaseCheckbox, showControlsCommand);
            NativeMethods.ShowWindow(level.InvertMatchCheckbox, showControlsCommand);
            bool resultsVisible = i < _activeSearchLevelCount && !IsZeroRect(levelLayout.SearchResultsRect);
            level.ResultsPane?.SetBounds(levelLayout.SearchResultsRect, resultsVisible);
        }

        ApplySearchResizeGripLayout();
        RedrawHostAfterLayout(redrawMode);
    }

    private void ApplySearchResizeGripLayout()
    {
        if (_searchResizeGrip == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _searchResizeGrip,
            NativeMethods.HWND_TOP,
            _layout.ClientRect.left,
            _layout.ClientRect.top,
            GetRectWidth(_layout.ClientRect),
            1,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_HIDEWINDOW);
    }

    private void ScheduleSearch()
    {
        if (!HasActiveSearch)
        {
            return;
        }

        RestartSearchAfterInputChange();
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        _pasteTransferCancellation.Cancel();
        NativeMethods.KillTimer(_hwnd, SearchDebounceTimerId);
        StopFileWatcher();
        CancelActiveSearch();
        if (_isSearchAreaResizing || _isSearchResultsResizing)
        {
            _isSearchAreaResizing = false;
            _isSearchResultsResizing = false;
            NativeMethods.ReleaseCapture();
        }

        _mainPane?.Dispose();

        if (_openButton != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_openButton);
            _openButton = IntPtr.Zero;
        }

        if (_pasteButton != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_pasteButton);
            _pasteButton = IntPtr.Zero;
        }

        if (_parserCombo != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_parserCombo);
            _parserCombo = IntPtr.Zero;
        }

        if (_highlightingButton != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_highlightingButton);
            _highlightingButton = IntPtr.Zero;
        }

        foreach (SearchLevelState level in _searchLevels)
        {
            DestroySearchLevel(level);
        }

        _searchLevels.Clear();

        if (_tooltipWindow != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_tooltipWindow);
            _tooltipWindow = IntPtr.Zero;
        }

        if (_searchResizeGrip != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_searchResizeGrip);
        }

        if (_parserInputVisibilityFont != IntPtr.Zero && _parserInputVisibilityFont != _font)
        {
            NativeMethods.DeleteObject(_parserInputVisibilityFont);
        }

        _parserInputVisibilityFont = IntPtr.Zero;
        if (_font != IntPtr.Zero && _font != NativeMethods.GetStockObject(NativeMethods.SYSTEM_FIXED_FONT))
        {
            NativeMethods.DeleteObject(_font);
        }

        DeleteOwnedFont(ref _boldFont);
        DeleteOwnedFont(ref _italicFont);
        DeleteOwnedFont(ref _boldItalicFont);
        _searchResizeGrip = IntPtr.Zero;
        _font = IntPtr.Zero;
        _pasteTransferCancellation.Dispose();
    }

    private static void DeleteOwnedFont(ref IntPtr font)
    {
        if (font != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(font);
            font = IntPtr.Zero;
        }
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
        string filterLabel = _activeSearchLevelCount == 1 ? "filtro" : "filtros";
        return $"{Math.Round(_searchProgressPercentage):0}% \u2022 {matches} {label} \u2022 {_activeSearchLevelCount} {filterLabel}";
    }

    private void InvalidateHost()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void RedrawHostAfterLayout(LayoutRedrawMode redrawMode)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        uint flags = NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_FRAME | NativeMethods.RDW_ALLCHILDREN;
        if (redrawMode == LayoutRedrawMode.Immediate)
        {
            flags |= NativeMethods.RDW_UPDATENOW;
        }

        NativeMethods.RedrawWindow(
            _hwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            flags);
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

    private static void SetButtonChecked(IntPtr hwnd, bool checkedState)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SendMessageW(
            hwnd,
            NativeMethods.BM_SETCHECK,
            new IntPtr(checkedState ? NativeMethods.BST_CHECKED : NativeMethods.BST_UNCHECKED),
            IntPtr.Zero);
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
