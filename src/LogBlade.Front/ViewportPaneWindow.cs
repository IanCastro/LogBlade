using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

internal enum ViewportRequestKind
{
    LoadAtPercentage,
    LoadAtRowOrdinal,
    LoadAtOffset,
    ScrollByLines,
    JumpHome,
    JumpEnd,
    RefreshTailIfAtEnd,
    ReloadAfterFileChange,
    RefreshHighlighting
}

internal readonly record struct ViewportRequest(long Id, ViewportRequestKind Kind, int DeltaLines, double RequestedPercentage, long RequestedOffset, long RequestedRowOrdinal, int VisibleLines, bool SelectRowAfterLoad);

internal sealed class ViewportWorkerResult
{
    public long RequestId { get; set; }
    public bool Success { get; set; }
    public bool IsStale { get; set; }
    public string? Message { get; set; }
    public IViewportReader? Reader { get; set; }
    public bool SelectRowAfterLoad { get; set; }
    public long SelectedOffset { get; set; }
    public Dictionary<ViewportHighlightGroupKey, HighlightStyle?>? HighlightStyleCache { get; set; }
}

internal sealed class ViewportPaneWindow : IDisposable
{
    private enum PendingSearchKeyboardSelection
    {
        None,
        FirstVisibleRow,
        LastVisibleRow,
        SpecificRowOrdinal,
        SynchronizedRowOrdinal
    }

    private enum GridAxisSelectionKind
    {
        None,
        Row,
        Column
    }

    private readonly record struct GridCellKey(ViewportRowSelectionKey RowKey, int ColumnIndex);

    private const int WheelLinesPerNotch = 3;
    private const int ScrollRange = 1_000_000;
    private const int VerticalScrollVirtualRows = 4000;
    private const int ColumnResizeHitSlopPx = 4;
    private const int DefaultTextColumnWidthPx = 900;
    private const int DefaultGroupColumnWidthPx = 200;
    private const int GridCellPaddingPx = 4;
    private const int GridDividerThicknessPx = 1;
    private const int MaximumHighlightStyleCacheEntries = 2048;
    private const int InactiveSelectionColor = 0x00FAF0E8;
    private const string WindowClassName = "LogBladeViewportPaneWindow";

    private static readonly object s_registrationSync = new();
    private static bool s_registered;
    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static readonly string[] s_textOnlyGridHeaders = ["Text"];

    private readonly IntPtr _font;
    private readonly IntPtr _boldFont;
    private readonly IntPtr _italicFont;
    private readonly IntPtr _boldItalicFont;
    private readonly int _lineHeight;
    private readonly int _charWidth;
    private readonly Action<ViewportPaneWindow>? _onUsefulPaint;
    private readonly Action<ViewportPaneWindow>? _onStale;
    private readonly Action<ViewportPaneWindow, ViewportRowSelectionKey>? _onRowActivated;
    private readonly Action? _onPasteRequested;
    private readonly Func<ViewportPaneWindow, int, bool>? _onHostVerticalResizeHit;
    private readonly Func<ViewportPaneWindow, int, bool>? _onHostVerticalResizeBegin;

    private IntPtr _hwnd;
    private IntPtr _inactiveSelectionBrush;
    private readonly Dictionary<int, IntPtr> _highlightBrushes = new();
    private readonly Dictionary<ViewportHighlightGroupKey, HighlightStyle?> _highlightStyleCache = new();
    private IReadOnlyList<CompiledHighlightRule> _highlightRules = Array.Empty<CompiledHighlightRule>();
    private GCHandle _selfHandle;
    private int _visibleColumnCount = 1;
    private int _visibleLineCount = 1;
    private int _xOffsetChars;
    private bool _isVisible = true;
    private bool _disposed;
    private bool _usefulPaintNotified;
    private bool _hasFocus;
    private string _statusText = string.Empty;
    private string _emptyContentText = "(empty file)";
    private IViewportReader? _reader;
    private long _nextViewportRequestId;
    private long _latestViewportRequestId;
    private bool _viewportWorkerRunning;
    private bool _tailRefreshPending;
    private bool _fileSizeRefreshPending;
    private bool _tailFollowSuspended;
    private ViewportRequest? _pendingViewportRequest;
    private int[]? _manualColumnWidths;
    private int _hoverResizeColumnIndex = -1;
    private int _resizingColumnIndex = -1;
    private int _resizeStartX;
    private int _resizeStartWidth;
    private bool _isColumnResizing;
    private readonly List<ViewportRowSelectionRange> _selectionRanges = new();
    private readonly HashSet<ViewportRowSelectionKey> _selectionExcludedRows = new();
    private List<ViewportRowSelectionRange>? _selectionDragBaseRanges;
    private HashSet<ViewportRowSelectionKey>? _selectionDragBaseExcludedRows;
    private bool _selectionSelectAllRows;
    private bool _selectionDragBaseSelectAllRows;
    private bool _isSelectingRows;
    private bool _isSelectingText;
    private bool _selectionDragMoved;
    private bool _selectionMouseStartedWithControl;
    private bool _selectionMouseStartedWithShift;
    private PendingSearchKeyboardSelection _pendingSearchKeyboardSelection;
    private long _pendingSearchKeyboardSelectionRequestId;
    private int _pendingSearchKeyboardSelectionColumn = -1;
    private long _pendingSearchKeyboardSelectionRowOrdinal = -1;
    private bool _pendingSearchKeyboardSelectionExtend;
    private bool _pendingSearchKeyboardSelectionActivate = true;
    private int _selectionMouseDownX;
    private int _selectionMouseDownY;
    private int _selectionAnchorDataIndex = -1;
    private int _selectionFocusDataIndex = -1;
    private ViewportRowSelectionKey? _selectionAnchorKey;
    private ViewportRowSelectionKey? _selectionFocusKey;
    private ViewportRowSelectionKey? _textSelectionRowKey;
    private ViewportTextSegmentKey? _textSelectionSegmentKey;
    private ViewportTextSelectionContext? _textSelectionContext;
    private int _textSelectionDataIndex = -1;
    private int _textSelectionColumnIndex = -1;
    private int _textSelectionAnchorChar = -1;
    private int _textSelectionFocusChar = -1;
    private bool _textSelectionDragThresholdArmed;
    private int _textSelectionDragStartX;
    private int _textSelectionDragStartY;
    private readonly HashSet<int> _cellSelectionColumns = new();
    private HashSet<int>? _cellSelectionDragBaseColumns;
    private List<ViewportRowSelectionRange>? _cellSelectionDragBaseRanges;
    private HashSet<ViewportRowSelectionKey>? _cellSelectionDragBaseExcludedRows;
    private bool _cellSelectionDragBaseSelectAllRows;
    private bool _isSelectingCells;
    private bool _isSelectingGridAxis;
    private GridAxisSelectionKind _gridAxisSelectionKind;
    private bool _cellSelectionDragMoved;
    private bool _cellSelectionDragStartedOnSelected;
    private bool _gridAxisDragStartedOnSelected;
    private bool _cellSelectionMouseStartedWithControl;
    private bool _cellSelectionMouseStartedWithShift;
    private int _cellSelectionMouseDownX;
    private int _cellSelectionMouseDownY;
    private int _cellSelectionAnchorDataIndex = -1;
    private int _cellSelectionFocusDataIndex = -1;
    private int _gridAxisAnchorColumn = -1;
    private int _gridAxisFocusColumn = -1;
    private GridCellKey? _cellSelectionAnchorKey;
    private GridCellKey? _cellSelectionFocusKey;

    public ViewportPaneWindow(
        IntPtr font,
        int lineHeight,
        int charWidth,
        IntPtr boldFont = default,
        IntPtr italicFont = default,
        IntPtr boldItalicFont = default,
        Action<ViewportPaneWindow>? onUsefulPaint = null,
        Action<ViewportPaneWindow>? onStale = null,
        Action<ViewportPaneWindow, ViewportRowSelectionKey>? onRowActivated = null,
        Action? onPasteRequested = null,
        Func<ViewportPaneWindow, int, bool>? onHostVerticalResizeHit = null,
        Func<ViewportPaneWindow, int, bool>? onHostVerticalResizeBegin = null)
    {
        _font = font;
        _boldFont = boldFont != IntPtr.Zero ? boldFont : font;
        _italicFont = italicFont != IntPtr.Zero ? italicFont : font;
        _boldItalicFont = boldItalicFont != IntPtr.Zero ? boldItalicFont : font;
        _lineHeight = Math.Max(1, lineHeight);
        _charWidth = Math.Max(1, charWidth);
        _inactiveSelectionBrush = NativeMethods.CreateSolidBrush(InactiveSelectionColor);
        _onUsefulPaint = onUsefulPaint;
        _onStale = onStale;
        _onRowActivated = onRowActivated;
        _onPasteRequested = onPasteRequested;
        _onHostVerticalResizeHit = onHostVerticalResizeHit;
        _onHostVerticalResizeBegin = onHostVerticalResizeBegin;
    }

    public IntPtr Hwnd => _hwnd;
    public IViewportReader? Reader => _reader;
    public int VisibleLineCount => Math.Max(1, _visibleLineCount);
    public int VisibleDataLineCount => Math.Max(1, _visibleLineCount - GetHeaderLineCount(_reader));
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
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    public void SetEmptyContentText(string text)
    {
        _emptyContentText = text;
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void SetHighlightRules(IReadOnlyList<CompiledHighlightRule> rules)
    {
        _highlightRules = rules;
        _highlightStyleCache.Clear();
        ClearHighlightBrushes();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
            if (_reader is not null)
            {
                QueueViewportRequest(ViewportRequestKind.RefreshHighlighting, visibleLines: VisibleDataLineCount);
            }
        }
    }

    public void SetStatus(string statusText, bool disposeReader = true, bool preserveColumnWidths = false)
    {
        ResetColumnResizeState(clearManualWidths: !preserveColumnWidths);
        ClearSelection(invalidate: false);
        if (disposeReader)
        {
            _reader?.Dispose();
            _reader = null;
            _highlightStyleCache.Clear();
        }

        _statusText = statusText;
        ResetViewportAsyncState();
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void SetReader(IViewportReader reader, int preloadedVisibleLines, bool preserveColumnWidths = false, bool preserveSelection = false)
    {
        bool canPreserveColumnState = CanPreserveColumnState(reader, preserveColumnWidths);
        if (!_isColumnResizing || !canPreserveColumnState)
        {
            ResetColumnResizeState(clearManualWidths: !canPreserveColumnState);
        }

        if (!preserveSelection)
        {
            ClearSelection(invalidate: false);
        }

        _reader?.Dispose();
        _reader = reader;
        _highlightStyleCache.Clear();
        _statusText = string.Empty;
        ResetViewportAsyncState();
        UpdateScrollBar();
        bool viewportRequestQueued = false;
        if (VisibleDataLineCount != Math.Max(1, preloadedVisibleLines))
        {
            QueueViewportRequest(
                ViewportRequestKind.LoadAtPercentage,
                requestedPercentage: _reader.ScrollPercentage,
                visibleLines: VisibleDataLineCount);
            viewportRequestQueued = true;
        }

        if (!viewportRequestQueued && _highlightRules.Count > 0)
        {
            QueueViewportRequest(ViewportRequestKind.RefreshHighlighting, visibleLines: VisibleDataLineCount);
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void RefreshReaderDisplayState()
    {
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void MarkReaderObservedZero()
    {
        if (_reader is ProjectedViewport projected)
        {
            projected.MarkObservedZeroFileSize();
        }

        _statusText = string.Empty;
        InvalidatePendingViewportRequests();
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void ClearReaderObservedZero()
    {
        if (_reader is ProjectedViewport projected)
        {
            projected.ClearObservedZeroFileSize();
        }

        _statusText = string.Empty;
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void Focus()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetFocus(_hwnd);
        }
    }

    public void JumpToFileOffset(long offset, bool selectRowAfterLoad = false)
    {
        if (_reader is null)
        {
            return;
        }

        SuspendTailFollow();
        QueueViewportRequest(ViewportRequestKind.LoadAtOffset, requestedOffset: offset, visibleLines: VisibleDataLineCount, selectRowAfterLoad: selectRowAfterLoad);
    }

    public bool SynchronizeToSearchResult(ViewportRowSelectionKey rowKey)
    {
        if (_reader is not IRowOrdinalViewportReader ordinalReader ||
            !ordinalReader.TryGetRowOrdinal(rowKey, out long rowOrdinal))
        {
            return false;
        }

        return QueueSearchKeyboardSelectionAtRowOrdinal(
            rowOrdinal,
            selectedColumn: -1,
            extendSelection: false,
            activate: false,
            synchronizeRow: true);
    }

    public void QueueTailRefreshIfAtEnd()
    {
        if (_reader is not ProjectedViewport { Source: LogRecordSource } visualReader || _hwnd == IntPtr.Zero)
        {
            _tailRefreshPending = false;
            _fileSizeRefreshPending = false;
            return;
        }

        if (!visualReader.IsAtEnd || _tailFollowSuspended)
        {
            _tailRefreshPending = false;
            if (_viewportWorkerRunning || _pendingViewportRequest is not null)
            {
                _fileSizeRefreshPending = true;
                return;
            }

            QueueViewportRequest(ViewportRequestKind.ReloadAfterFileChange, visibleLines: VisibleDataLineCount);
            return;
        }

        if (_pendingViewportRequest is ViewportRequest { Kind: ViewportRequestKind.RefreshTailIfAtEnd })
        {
            return;
        }

        if (_viewportWorkerRunning ||
            _pendingViewportRequest is ViewportRequest { Kind: not ViewportRequestKind.RefreshTailIfAtEnd })
        {
            _tailRefreshPending = true;
            return;
        }

        _tailRefreshPending = false;
        QueueViewportRequest(ViewportRequestKind.RefreshTailIfAtEnd, visibleLines: VisibleDataLineCount);
    }

    public void QueueReloadAfterFileChange()
    {
        if (_reader is not ProjectedViewport || _hwnd == IntPtr.Zero)
        {
            _fileSizeRefreshPending = false;
            return;
        }

        _tailRefreshPending = false;
        if (_viewportWorkerRunning || _pendingViewportRequest is not null)
        {
            _fileSizeRefreshPending = true;
            return;
        }

        QueueViewportRequest(ViewportRequestKind.ReloadAfterFileChange, visibleLines: VisibleDataLineCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hwnd != IntPtr.Zero && NativeMethods.DestroyWindow(_hwnd))
        {
            return;
        }

        DisposeWindowResources();
    }

    private void DisposeWindowResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResetColumnResizeState(clearManualWidths: true);
        ClearSelection(invalidate: false);
        _reader?.Dispose();
        _reader = null;
        _pendingViewportRequest = null;
        if (_inactiveSelectionBrush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_inactiveSelectionBrush);
            _inactiveSelectionBrush = IntPtr.Zero;
        }

        ClearHighlightBrushes();
        _highlightStyleCache.Clear();
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
                style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW | NativeMethods.CS_DBLCLKS,
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
        _tailRefreshPending = false;
        _fileSizeRefreshPending = false;
        _tailFollowSuspended = false;
        _pendingViewportRequest = null;
        ClearPendingSearchKeyboardSelection();
    }

    private void InvalidatePendingViewportRequests()
    {
        _latestViewportRequestId = ++_nextViewportRequestId;
        _pendingViewportRequest = null;
        _tailRefreshPending = false;
        _fileSizeRefreshPending = false;
        ClearPendingSearchKeyboardSelection();
    }

    private void QueuePendingTailRefreshIfReady()
    {
        if (!_tailRefreshPending || _viewportWorkerRunning || _pendingViewportRequest is not null)
        {
            return;
        }

        if (_reader is not ProjectedViewport { Source: LogRecordSource })
        {
            _tailRefreshPending = false;
            return;
        }

        _tailRefreshPending = false;
        QueueViewportRequest(ViewportRequestKind.RefreshTailIfAtEnd, visibleLines: VisibleDataLineCount);
    }

    private void RefreshPendingFileSizeIfReady()
    {
        if (!_fileSizeRefreshPending || _viewportWorkerRunning || _pendingViewportRequest is not null)
        {
            return;
        }

        _fileSizeRefreshPending = false;
        QueueViewportRequest(ViewportRequestKind.ReloadAfterFileChange, visibleLines: VisibleDataLineCount);
    }

    private void SuspendTailFollow()
    {
        _tailRefreshPending = false;
        _tailFollowSuspended = true;
    }

    private void ResumeTailFollowIfAtEnd()
    {
        if (_reader is ProjectedViewport { Source: LogRecordSource, IsAtEnd: true })
        {
            _tailFollowSuspended = false;
        }
    }

    private long QueueViewportRequest(ViewportRequestKind kind, int deltaLines = 0, double requestedPercentage = 0d, long requestedOffset = 0L, long requestedRowOrdinal = 0L, int? visibleLines = null, bool selectRowAfterLoad = false)
    {
        if (_reader is null || _hwnd == IntPtr.Zero)
        {
            return 0L;
        }

        if (kind != ViewportRequestKind.RefreshHighlighting)
        {
            ClearTextSelection(invalidate: false);
        }
        if (kind is ViewportRequestKind.RefreshTailIfAtEnd or ViewportRequestKind.ReloadAfterFileChange)
        {
            _highlightStyleCache.Clear();
        }
        int effectiveVisible = Math.Max(1, visibleLines ?? VisibleDataLineCount);
        var request = new ViewportRequest(
            Id: ++_nextViewportRequestId,
            Kind: kind,
            DeltaLines: deltaLines,
            RequestedPercentage: requestedPercentage,
            RequestedOffset: requestedOffset,
            RequestedRowOrdinal: requestedRowOrdinal,
            VisibleLines: effectiveVisible,
            SelectRowAfterLoad: selectRowAfterLoad);

        _latestViewportRequestId = request.Id;
        _pendingViewportRequest = request;
        if (!_viewportWorkerRunning)
        {
            DispatchViewportRequest();
        }

        return request.Id;
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
        IReadOnlyList<CompiledHighlightRule> highlightRules = _highlightRules;
        Dictionary<ViewportHighlightGroupKey, HighlightStyle?> highlightStyleCache = new(_highlightStyleCache);
        IntPtr hwnd = _hwnd;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = new ViewportWorkerResult
            {
                RequestId = request.Id,
                SelectRowAfterLoad = request.SelectRowAfterLoad,
                SelectedOffset = request.RequestedOffset
            };
            try
            {
                switch (request.Kind)
                {
                    case ViewportRequestKind.LoadAtPercentage:
                        workerReader.ReadFromPercentage(request.RequestedPercentage, request.VisibleLines);
                        break;
                    case ViewportRequestKind.LoadAtRowOrdinal:
                        if (workerReader is ProjectedViewport filteredReader)
                        {
                            filteredReader.ReadFromRowOrdinal(request.RequestedRowOrdinal, request.VisibleLines);
                        }
                        else
                        {
                            workerReader.ReadFromPercentage(request.RequestedPercentage, request.VisibleLines);
                        }

                        break;
                    case ViewportRequestKind.LoadAtOffset:
                        if (workerReader is ProjectedViewport offsetReader)
                        {
                            offsetReader.ReadFromOffset(request.RequestedOffset, request.VisibleLines);
                        }

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
                    case ViewportRequestKind.RefreshTailIfAtEnd:
                        if (workerReader is ProjectedViewport visualReader)
                        {
                            visualReader.RefreshTail(request.VisibleLines);
                        }

                        break;
                    case ViewportRequestKind.ReloadAfterFileChange:
                        if (workerReader is ProjectedViewport changedReader)
                        {
                            changedReader.ReloadAfterFileChange(request.VisibleLines);
                        }

                        break;
                    case ViewportRequestKind.RefreshHighlighting:
                        break;
                }

                result.HighlightStyleCache = PrepareHighlightStyleCache(
                    workerReader,
                    highlightRules,
                    highlightStyleCache);
                result.Success = true;
                result.Reader = workerReader;
            }
            catch (FilteredLineStaleException ex)
            {
                result.Success = false;
                result.IsStale = true;
                result.Message = ex.Message;
                workerReader.Dispose();
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
        bool isCurrentRequest = result.RequestId == _latestViewportRequestId;

        if (isCurrentRequest && result.Success && result.Reader is not null)
        {
            _reader?.Dispose();
            _reader = result.Reader;
            ReplaceHighlightStyleCache(result.HighlightStyleCache);
            ResumeTailFollowIfAtEnd();
            UpdateScrollBar();
            if (result.SelectRowAfterLoad)
            {
                SelectLoadedRowByOffset(result.SelectedOffset);
            }

            ApplyPendingSearchKeyboardSelection(result.RequestId);
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
        else
        {
            if (result.RequestId == _pendingSearchKeyboardSelectionRequestId)
            {
                ClearPendingSearchKeyboardSelection();
            }

            if (isCurrentRequest && result.IsStale)
            {
                _onStale?.Invoke(this);
            }

            result.Reader?.Dispose();
        }

        if (_pendingViewportRequest is not null)
        {
            DispatchViewportRequest();
            return;
        }

        RefreshPendingFileSizeIfReady();
        QueuePendingTailRefreshIfReady();
    }

    private void OnSize()
    {
        int previousVisibleDataLineCount = VisibleDataLineCount;
        UpdateScrollBar();
        if (_reader is not null &&
            _reader.HasContent &&
            VisibleDataLineCount != previousVisibleDataLineCount)
        {
            if (_reader is ProjectedViewport { Source: FilteredLogRecordSource filteredReader })
            {
                if (filteredReader.IsAtEnd)
                {
                    QueueViewportRequest(
                        ViewportRequestKind.LoadAtPercentage,
                        requestedPercentage: 100d,
                        visibleLines: VisibleDataLineCount);
                }
                else
                {
                    QueueViewportRequest(
                        ViewportRequestKind.LoadAtRowOrdinal,
                        requestedPercentage: _reader.ScrollPercentage,
                        requestedRowOrdinal: filteredReader.TopRecordOrdinal,
                        visibleLines: VisibleDataLineCount);
                }
            }
            else
            {
                QueueViewportRequest(
                    ViewportRequestKind.LoadAtPercentage,
                    requestedPercentage: _reader.ScrollPercentage,
                    visibleLines: VisibleDataLineCount);
            }
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
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

        int visibleColumns = GetHorizontalVisibleColumnCount();
        int maxOffset = Math.Max(0, GetContentWidthChars() - visibleColumns);
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
                nextOffset -= visibleColumns;
                break;
            case NativeMethods.SB_PAGERIGHT:
                nextOffset += visibleColumns;
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
        double trackPercentage = (Math.Clamp(trackPos, 0, ScrollRange) * 100d) / ScrollRange;
        if (command is NativeMethods.SB_THUMBPOSITION or NativeMethods.SB_THUMBTRACK)
        {
            NativeMethods.SCROLLINFO si = new()
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
                fMask = NativeMethods.SIF_TRACKPOS | NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE
            };
            if (NativeMethods.GetScrollInfo(_hwnd, NativeMethods.SB_VERT, ref si))
            {
                trackPos = si.nTrackPos;
                trackPercentage = ScrollTrackPositionToPercentage(trackPos, si);
            }
        }

        Scroll(command, trackPercentage);
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

    private void OnMouseMove(IntPtr lParam)
    {
        int x = NativeMethods.LowWord(lParam);
        int y = NativeMethods.HighWord(lParam);
        if (_isColumnResizing)
        {
            UpdateColumnResize(x);
            SetResizeCursor();
            return;
        }

        if (_isSelectingText)
        {
            UpdateTextSelection(x, y);
            return;
        }

        if (_isSelectingRows)
        {
            UpdateRowSelection(x, y);
            return;
        }

        if (_isSelectingCells)
        {
            UpdateCellSelection(x, y);
            return;
        }

        if (_isSelectingGridAxis)
        {
            UpdateGridAxisSelection(x, y);
            return;
        }

        if (IsHostVerticalResizeHit(y))
        {
            _hoverResizeColumnIndex = -1;
            SetVerticalResizeCursor();
            return;
        }

        int nextHover = HitTestColumnResize(x, y);
        if (_hoverResizeColumnIndex != nextHover)
        {
            _hoverResizeColumnIndex = nextHover;
        }

        if (_hoverResizeColumnIndex >= 0)
        {
            SetResizeCursor();
        }
    }

    private void OnLButtonDown(IntPtr lParam)
    {
        int x = NativeMethods.LowWord(lParam);
        int y = NativeMethods.HighWord(lParam);
        if (TryBeginHostVerticalResize(y))
        {
            return;
        }

        Focus();
        ClearPendingSearchKeyboardSelection();
        if (TryBeginArmedTextSelection(x, y))
        {
            return;
        }

        ClearTextSelection(invalidate: true);

        int resizeColumn = HitTestColumnResize(x, y);
        if (resizeColumn >= 0)
        {
            BeginColumnResize(resizeColumn, x);
            return;
        }

        if (IsSearchCellSelectionMode && TryHandleGridAxisClick(x, y))
        {
            return;
        }

        if (IsFixedLineNumberColumnHit(x))
        {
            return;
        }

        if (IsSearchCellSelectionMode)
        {
            if (!BeginCellSelection(x, y))
            {
                ClearSelection(invalidate: true);
            }

            return;
        }

        if (!BeginRowSelection(x, y))
        {
            ClearSelection(invalidate: true);
        }
    }

    private void OnLButtonUp()
    {
        if (_isColumnResizing)
        {
            EndColumnResize();
            return;
        }

        if (_isSelectingText)
        {
            EndTextSelection();
            return;
        }

        if (_isSelectingRows)
        {
            EndRowSelection();
            return;
        }

        if (_isSelectingCells)
        {
            EndCellSelection();
            return;
        }

        if (_isSelectingGridAxis)
        {
            EndGridAxisSelection();
        }
    }

    private void OnLButtonDoubleClick(IntPtr lParam)
    {
        Focus();
        int x = NativeMethods.LowWord(lParam);
        int y = NativeMethods.HighWord(lParam);
        int resizeColumn = HitTestColumnResize(x, y);
        if (resizeColumn >= 0)
        {
            AutoFitColumn(resizeColumn);
            return;
        }

        if (BeginTextSelectionFromDoubleClick(x, y))
        {
            return;
        }
    }

    private void OnFocusChanged(bool hasFocus)
    {
        if (_hasFocus == hasFocus)
        {
            return;
        }

        _hasFocus = hasFocus;
        if (HasSelection)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private bool OnSetCursor()
    {
        if (TryGetClientCursorY(out int y) && IsHostVerticalResizeHit(y))
        {
            SetVerticalResizeCursor();
            return true;
        }

        if (_isColumnResizing || _hoverResizeColumnIndex >= 0)
        {
            SetResizeCursor();
            return true;
        }

        return false;
    }

    private bool IsHostVerticalResizeHit(int y) =>
        !_isColumnResizing &&
        !_isSelectingText &&
        !_isSelectingRows &&
        !_isSelectingCells &&
        !_isSelectingGridAxis &&
        (_onHostVerticalResizeHit?.Invoke(this, y) ?? false);

    private bool TryBeginHostVerticalResize(int y) =>
        _onHostVerticalResizeBegin?.Invoke(this, y) ?? false;

    private bool TryGetClientCursorY(out int y)
    {
        y = int.MinValue;
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT point))
        {
            return false;
        }

        if (!NativeMethods.ScreenToClient(_hwnd, ref point))
        {
            return false;
        }

        y = point.y;
        return true;
    }

    private bool BeginTextSelectionFromDoubleClick(int x, int y)
    {
        if (IsSearchCellSelectionMode)
        {
            return BeginGridTextSelectionFromDoubleClick(x, y);
        }

        if (!TryHitTestMainTextRow(x, y, out int rowIndex, out ViewportRowSelectionKey rowKey, out string text))
        {
            return false;
        }

        if (!TryGetTextWordCharIndexFromX(x, text, out int localCharIndex))
        {
            return false;
        }

        ClearSelection(invalidate: false);
        if (TryGetTextSelectionContext(rowIndex, out ViewportTextSegmentKey segmentKey, out ViewportTextSelectionContext context, out ViewportTextSegmentRange segmentRange))
        {
            int globalCharIndex = Math.Clamp(segmentRange.Start + localCharIndex, segmentRange.Start, segmentRange.Start + segmentRange.Length);
            if (TryGetWordSelection(context.Text, globalCharIndex, out int wordStart, out int wordEnd))
            {
                StartLogicalTextSelection(rowIndex, segmentKey, context, wordStart, wordEnd, captureMouse: true, columnIndex: -1);
            }
            else
            {
                StartLogicalTextSelection(rowIndex, segmentKey, context, globalCharIndex, globalCharIndex, captureMouse: true, columnIndex: -1);
            }
        }
        else
        {
            if (TryGetWordSelection(text, localCharIndex, out int wordStart, out int wordEnd))
            {
                StartTextSelection(rowIndex, rowKey, wordStart, wordEnd, captureMouse: true, columnIndex: -1);
            }
            else
            {
                StartTextSelection(rowIndex, rowKey, localCharIndex, captureMouse: true, columnIndex: -1);
            }
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        ArmTextSelectionDragThreshold(x, y);
        return true;
    }

    private bool BeginGridTextSelectionFromDoubleClick(int x, int y)
    {
        if (!TryHitTestGridTextCell(
                x,
                y,
                out int rowIndex,
                out ViewportRowSelectionKey rowKey,
                out int columnIndex,
                out string text,
                out NativeMethods.RECT cellRect,
                out int widthChars))
        {
            return false;
        }

        if (!TryGetGridCellWordCharIndexFromX(x, cellRect, widthChars, text, out int localCharIndex))
        {
            return false;
        }

        ClearSelection(invalidate: false);
        if (columnIndex == 1 &&
            TryGetTextSelectionContext(rowIndex, out ViewportTextSegmentKey segmentKey, out ViewportTextSelectionContext context, out ViewportTextSegmentRange segmentRange))
        {
            int globalCharIndex = Math.Clamp(segmentRange.Start + localCharIndex, segmentRange.Start, segmentRange.Start + segmentRange.Length);
            if (TryGetWordSelection(context.Text, globalCharIndex, out int wordStart, out int wordEnd))
            {
                StartLogicalTextSelection(rowIndex, segmentKey, context, wordStart, wordEnd, captureMouse: true, columnIndex: columnIndex);
            }
            else
            {
                StartLogicalTextSelection(rowIndex, segmentKey, context, globalCharIndex, globalCharIndex, captureMouse: true, columnIndex: columnIndex);
            }
        }
        else
        {
            if (TryGetWordSelection(text, localCharIndex, out int wordStart, out int wordEnd))
            {
                StartTextSelection(rowIndex, rowKey, wordStart, wordEnd, captureMouse: true, columnIndex: columnIndex);
            }
            else
            {
                StartTextSelection(rowIndex, rowKey, localCharIndex, captureMouse: true, columnIndex: columnIndex);
            }
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        ArmTextSelectionDragThreshold(x, y);
        return true;
    }

    private bool TryBeginArmedTextSelection(int x, int y)
    {
        if (!HasTextSelection)
        {
            return false;
        }

        if (_textSelectionSegmentKey is ViewportTextSegmentKey selectedSegment &&
            _textSelectionContext is ViewportTextSelectionContext selectedContext)
        {
            if (_textSelectionColumnIndex == 1)
            {
                if (!TryHitTestGridTextCell(
                        x,
                        y,
                        out int gridRowIndex,
                        out _,
                        out int columnIndex,
                        out string gridText,
                        out NativeMethods.RECT cellRect,
                        out int widthChars) ||
                    columnIndex != 1 ||
                    !TryGetTextSelectionContext(gridRowIndex, out ViewportTextSegmentKey gridSegment, out ViewportTextSelectionContext gridContext, out ViewportTextSegmentRange gridRange) ||
                    gridSegment.GroupKey != selectedSegment.GroupKey)
                {
                    return false;
                }

                int localCharIndex = GetGridCellTextCharIndexFromX(x, cellRect, widthChars, gridText);
                int globalCharIndex = Math.Clamp(gridRange.Start + localCharIndex, gridRange.Start, gridRange.Start + gridRange.Length);
                StartLogicalTextSelection(gridRowIndex, gridSegment, gridContext, globalCharIndex, globalCharIndex, captureMouse: true, columnIndex: 1);
                NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
                return true;
            }

            if (_textSelectionColumnIndex == -1 &&
                TryHitTestMainTextRow(x, y, out int logicalMainRowIndex, out _, out string logicalMainText) &&
                TryGetTextSelectionContext(logicalMainRowIndex, out ViewportTextSegmentKey mainSegment, out ViewportTextSelectionContext mainContext, out ViewportTextSegmentRange mainRange) &&
                mainSegment.GroupKey == selectedContext.GroupKey)
            {
                int localCharIndex = GetTextCharIndexFromX(x, logicalMainText);
                int globalCharIndex = Math.Clamp(mainRange.Start + localCharIndex, mainRange.Start, mainRange.Start + mainRange.Length);
                StartLogicalTextSelection(logicalMainRowIndex, mainSegment, mainContext, globalCharIndex, globalCharIndex, captureMouse: true, columnIndex: -1);
                NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
                return true;
            }

            return false;
        }

        if (_textSelectionRowKey is not ViewportRowSelectionKey selectedRowKey)
        {
            return false;
        }

        if (_textSelectionColumnIndex > 0)
        {
            if (!TryHitTestGridTextCell(
                    x,
                    y,
                    out int gridRowIndex,
                    out ViewportRowSelectionKey gridRowKey,
                    out int columnIndex,
                    out string gridText,
                    out NativeMethods.RECT cellRect,
                    out int widthChars) ||
                gridRowKey != selectedRowKey ||
                columnIndex != _textSelectionColumnIndex)
            {
                return false;
            }

            StartTextSelection(
                gridRowIndex,
                gridRowKey,
                GetGridCellTextCharIndexFromX(x, cellRect, widthChars, gridText),
                captureMouse: true,
                columnIndex: columnIndex);
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
            return true;
        }

        if (!TryHitTestMainTextRow(x, y, out int mainRowIndex, out ViewportRowSelectionKey mainRowKey, out string mainText) ||
            mainRowKey != selectedRowKey)
        {
            return false;
        }

        StartTextSelection(mainRowIndex, mainRowKey, GetTextCharIndexFromX(x, mainText), captureMouse: true, columnIndex: -1);
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        return true;
    }

    private void StartTextSelection(int rowIndex, ViewportRowSelectionKey rowKey, int charIndex, bool captureMouse, int columnIndex)
    {
        _textSelectionDragThresholdArmed = false;
        _isSelectingText = true;
        _textSelectionDataIndex = rowIndex;
        _textSelectionColumnIndex = columnIndex;
        _textSelectionRowKey = rowKey;
        _textSelectionSegmentKey = null;
        _textSelectionContext = null;
        _textSelectionAnchorChar = charIndex;
        _textSelectionFocusChar = charIndex;
        if (captureMouse)
        {
            NativeMethods.SetCapture(_hwnd);
        }
    }

    private void StartTextSelection(int rowIndex, ViewportRowSelectionKey rowKey, int anchorChar, int focusChar, bool captureMouse, int columnIndex)
    {
        _textSelectionDragThresholdArmed = false;
        _isSelectingText = true;
        _textSelectionDataIndex = rowIndex;
        _textSelectionColumnIndex = columnIndex;
        _textSelectionRowKey = rowKey;
        _textSelectionSegmentKey = null;
        _textSelectionContext = null;
        _textSelectionAnchorChar = anchorChar;
        _textSelectionFocusChar = focusChar;
        if (captureMouse)
        {
            NativeMethods.SetCapture(_hwnd);
        }
    }

    private void StartLogicalTextSelection(
        int rowIndex,
        ViewportTextSegmentKey segmentKey,
        ViewportTextSelectionContext context,
        int anchorChar,
        int focusChar,
        bool captureMouse,
        int columnIndex)
    {
        _textSelectionDragThresholdArmed = false;
        _isSelectingText = true;
        _textSelectionDataIndex = rowIndex;
        _textSelectionColumnIndex = columnIndex;
        _textSelectionRowKey = null;
        _textSelectionSegmentKey = segmentKey;
        _textSelectionContext = context;
        _textSelectionAnchorChar = anchorChar;
        _textSelectionFocusChar = focusChar;
        if (captureMouse)
        {
            NativeMethods.SetCapture(_hwnd);
        }
    }

    internal static bool TryGetWordSelection(string text, int charIndex, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrEmpty(text) ||
            charIndex < 0 ||
            charIndex >= text.Length ||
            !IsLogTokenCharacter(text[charIndex]))
        {
            return false;
        }

        start = charIndex;
        while (start > 0 && IsLogTokenCharacter(text[start - 1]))
        {
            start--;
        }

        end = charIndex + 1;
        while (end < text.Length && IsLogTokenCharacter(text[end]))
        {
            end++;
        }

        return end > start;
    }

    private static bool IsLogTokenCharacter(char value) =>
        char.IsLetterOrDigit(value) ||
        value is '_' or '.' or '-' or '/' or '\\' or ':' or '@';

    private void UpdateTextSelection(int x, int y)
    {
        if (IsTextSelectionMouseMoveInsideDragThreshold(x, y))
        {
            return;
        }

        if (_textSelectionSegmentKey is not null &&
            _textSelectionContext is ViewportTextSelectionContext context)
        {
            if (!TryGetLogicalTextSelectionFocus(
                    x,
                    y,
                    context,
                    out int logicalRowIndex,
                    out ViewportTextSegmentKey focusSegment,
                    out ViewportTextSegmentRange segmentRange,
                    out int localCharIndex))
            {
                return;
            }

            _textSelectionDataIndex = logicalRowIndex;
            _textSelectionSegmentKey = focusSegment;
            _textSelectionFocusChar = Math.Clamp(
                segmentRange.Start + localCharIndex,
                segmentRange.Start,
                segmentRange.Start + segmentRange.Length);
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
            return;
        }

        if (_textSelectionRowKey is null || !TryFindCurrentRowIndex(_textSelectionRowKey, out int rowIndex))
        {
            ClearTextSelection(invalidate: true);
            return;
        }

        IReadOnlyList<string> rows = _reader?.CurrentRows ?? Array.Empty<string>();
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            ClearTextSelection(invalidate: true);
            return;
        }

        _textSelectionDataIndex = rowIndex;
        if (_textSelectionColumnIndex > 0)
        {
            if (!TryGetGridCellTextAndRect(
                    rowIndex,
                    _textSelectionColumnIndex,
                    out string cellText,
                    out NativeMethods.RECT cellRect,
                    out _,
                    out int widthChars))
            {
                ClearTextSelection(invalidate: true);
                return;
            }

            _textSelectionFocusChar = GetGridCellTextCharIndexFromX(x, cellRect, widthChars, cellText);
        }
        else
        {
            _textSelectionFocusChar = GetTextCharIndexFromX(x, rows[rowIndex]);
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private bool TryGetLogicalTextSelectionFocus(
        int x,
        int y,
        ViewportTextSelectionContext context,
        out int rowIndex,
        out ViewportTextSegmentKey segmentKey,
        out ViewportTextSegmentRange segmentRange,
        out int localCharIndex)
    {
        rowIndex = -1;
        segmentKey = default;
        segmentRange = default;
        localCharIndex = 0;

        int targetRow = GetDataRowIndexFromY(y, clamp: true);
        if (targetRow < 0 ||
            !TryGetTextSelectionContext(targetRow, out segmentKey, out _, out segmentRange) ||
            segmentKey.GroupKey != context.GroupKey)
        {
            return false;
        }

        if (_textSelectionColumnIndex == 1)
        {
            if (!TryGetGridCellTextAndRect(
                    targetRow,
                    1,
                    out string cellText,
                    out NativeMethods.RECT cellRect,
                    out _,
                    out int widthChars))
            {
                return false;
            }

            localCharIndex = GetGridCellTextCharIndexFromX(x, cellRect, widthChars, cellText);
        }
        else
        {
            IReadOnlyList<string> logicalRows = _reader?.CurrentRows ?? Array.Empty<string>();
            if (targetRow < 0 || targetRow >= logicalRows.Count)
            {
                return false;
            }

            localCharIndex = GetTextCharIndexFromX(x, logicalRows[targetRow]);
        }

        rowIndex = targetRow;
        return true;
    }

    private void EndTextSelection()
    {
        _textSelectionDragThresholdArmed = false;
        _isSelectingText = false;
        NativeMethods.ReleaseCapture();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private void ArmTextSelectionDragThreshold(int x, int y)
    {
        _textSelectionDragThresholdArmed = true;
        _textSelectionDragStartX = x;
        _textSelectionDragStartY = y;
    }

    private bool IsTextSelectionMouseMoveInsideDragThreshold(int x, int y)
    {
        if (!_textSelectionDragThresholdArmed)
        {
            return false;
        }

        int dragWidth = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDRAG));
        int dragHeight = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDRAG));
        if (Math.Abs(x - _textSelectionDragStartX) <= dragWidth &&
            Math.Abs(y - _textSelectionDragStartY) <= dragHeight)
        {
            return true;
        }

        _textSelectionDragThresholdArmed = false;
        return false;
    }

    private bool TryHitTestMainTextRow(int x, int y, out int rowIndex, out ViewportRowSelectionKey rowKey, out string text)
    {
        rowIndex = -1;
        rowKey = default;
        text = string.Empty;
        if (IsSearchCellSelectionMode || IsFixedLineNumberColumnHit(x))
        {
            return false;
        }

        rowIndex = GetDataRowIndexFromY(y, clamp: false);
        return rowIndex >= 0 && TryGetCurrentRowSelection(rowIndex, out rowKey, out text);
    }

    private int GetTextCharIndexFromX(int x, string text)
    {
        int visibleChar = x <= 0 ? 0 : x / Math.Max(1, _charWidth);
        return Math.Clamp(_xOffsetChars + visibleChar, 0, text.Length);
    }

    private bool TryGetTextWordCharIndexFromX(int x, string text, out int charIndex)
    {
        charIndex = -1;
        if (text.Length == 0)
        {
            return false;
        }

        int visibleChar = x <= 0 ? 0 : x / Math.Max(1, _charWidth);
        int candidate = _xOffsetChars + visibleChar;
        if (candidate < 0 || candidate >= text.Length)
        {
            return false;
        }

        charIndex = candidate;
        return true;
    }

    private int GetGridCellTextCharIndexFromX(int x, NativeMethods.RECT cellRect, int widthChars, string text)
    {
        int textLeft = cellRect.left + GridCellPaddingPx;
        int visibleChar = x <= textLeft ? 0 : (x - textLeft) / Math.Max(1, _charWidth);
        int visibleCapacity = Math.Min(text.Length, GetGridTextCapacityChars(widthChars));
        return Math.Clamp(visibleChar, 0, visibleCapacity);
    }

    private bool TryGetGridCellWordCharIndexFromX(
        int x,
        NativeMethods.RECT cellRect,
        int widthChars,
        string text,
        out int charIndex)
    {
        charIndex = -1;
        if (text.Length == 0)
        {
            return false;
        }

        int textLeft = cellRect.left + GridCellPaddingPx;
        int visibleChar = x <= textLeft ? 0 : (x - textLeft) / Math.Max(1, _charWidth);
        int visibleCapacity = Math.Min(text.Length, GetGridTextCapacityChars(widthChars));
        if (visibleChar < 0 || visibleChar >= visibleCapacity)
        {
            return false;
        }

        charIndex = visibleChar;
        return true;
    }

    private bool BeginRowSelection(int x, int y)
    {
        if (IsFixedLineNumberColumnHit(x))
        {
            return false;
        }

        int rowIndex = GetDataRowIndexFromY(y, clamp: false);
        if (rowIndex < 0 || !TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            return false;
        }

        bool control = IsControlKeyDown();
        bool shift = IsShiftKeyDown();
        _isSelectingRows = true;
        _selectionDragMoved = false;
        _selectionMouseStartedWithControl = control;
        _selectionMouseStartedWithShift = shift;
        _selectionMouseDownX = x;
        _selectionMouseDownY = y;
        _selectionDragBaseRanges = new List<ViewportRowSelectionRange>(_selectionRanges);
        _selectionDragBaseExcludedRows = new HashSet<ViewportRowSelectionKey>(_selectionExcludedRows);
        _selectionDragBaseSelectAllRows = _selectionSelectAllRows;
        if (!shift || _selectionAnchorKey is null)
        {
            _selectionAnchorDataIndex = rowIndex;
            _selectionAnchorKey = rowKey;
        }
        else
        {
            TryFindCurrentRowIndex(_selectionAnchorKey, out _selectionAnchorDataIndex);
        }

        _selectionFocusDataIndex = rowIndex;
        _selectionFocusKey = rowKey;
        ApplyMouseSelection(rowIndex, isDrag: false);
        NativeMethods.SetCapture(_hwnd);
        return true;
    }

    private void UpdateRowSelection(int x, int y)
    {
        int rowIndex = GetDataRowIndexFromY(y, clamp: true);
        if (rowIndex < 0)
        {
            return;
        }

        if (Math.Abs(x - _selectionMouseDownX) > 2 || Math.Abs(y - _selectionMouseDownY) > 2)
        {
            _selectionDragMoved = true;
        }

        if (rowIndex == _selectionFocusDataIndex)
        {
            return;
        }

        _selectionFocusDataIndex = rowIndex;
        _selectionFocusKey = GetCurrentRowSelectionKey(rowIndex);
        ApplyMouseSelection(rowIndex, isDrag: true);
    }

    private void EndRowSelection()
    {
        int activatedRowIndex = _selectionFocusDataIndex;
        bool shouldActivate = !_selectionDragMoved &&
            !_selectionMouseStartedWithControl &&
            !_selectionMouseStartedWithShift &&
            activatedRowIndex >= 0;

        _isSelectingRows = false;
        _selectionDragBaseRanges = null;
        _selectionDragBaseExcludedRows = null;
        _selectionDragBaseSelectAllRows = false;
        _selectionMouseStartedWithControl = false;
        _selectionMouseStartedWithShift = false;
        NativeMethods.ReleaseCapture();

        if (shouldActivate)
        {
            ActivateRowAt(activatedRowIndex);
        }
    }

    private bool BeginCellSelection(int x, int y)
    {
        if (!TryHitTestGridCell(x, y, out int rowIndex, out int columnIndex) ||
            !TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            return false;
        }

        bool control = IsControlKeyDown();
        bool shift = IsShiftKeyDown();
        var key = new GridCellKey(rowKey, columnIndex);
        _isSelectingCells = true;
        _cellSelectionDragMoved = false;
        _cellSelectionDragStartedOnSelected = IsGridCellSelected(key);
        _cellSelectionMouseStartedWithControl = control;
        _cellSelectionMouseStartedWithShift = shift;
        _cellSelectionMouseDownX = x;
        _cellSelectionMouseDownY = y;
        _cellSelectionDragBaseRanges = new List<ViewportRowSelectionRange>(_selectionRanges);
        _cellSelectionDragBaseExcludedRows = new HashSet<ViewportRowSelectionKey>(_selectionExcludedRows);
        _cellSelectionDragBaseSelectAllRows = _selectionSelectAllRows;
        _cellSelectionDragBaseColumns = new HashSet<int>(_cellSelectionColumns);
        if (!shift || _cellSelectionAnchorKey is null)
        {
            _cellSelectionAnchorDataIndex = rowIndex;
            _cellSelectionAnchorKey = key;
        }
        else
        {
            TryFindCurrentRowIndex(_cellSelectionAnchorKey.Value.RowKey, out _cellSelectionAnchorDataIndex);
        }

        _cellSelectionFocusDataIndex = rowIndex;
        _cellSelectionFocusKey = key;
        ApplyMouseCellSelection(key, isDrag: false);
        NativeMethods.SetCapture(_hwnd);
        return true;
    }

    private void UpdateCellSelection(int x, int y)
    {
        if (!TryHitTestGridCell(x, y, out int rowIndex, out int columnIndex, clampRow: true) ||
            !TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            return;
        }

        if (Math.Abs(x - _cellSelectionMouseDownX) > 2 || Math.Abs(y - _cellSelectionMouseDownY) > 2)
        {
            _cellSelectionDragMoved = true;
        }

        var key = new GridCellKey(rowKey, columnIndex);
        if (_cellSelectionFocusKey == key)
        {
            return;
        }

        _cellSelectionFocusDataIndex = rowIndex;
        _cellSelectionFocusKey = key;
        ApplyMouseCellSelection(key, isDrag: true);
    }

    private void EndCellSelection()
    {
        int activatedRowIndex = _cellSelectionFocusDataIndex;
        bool shouldActivate = !_cellSelectionDragMoved && activatedRowIndex >= 0;

        _isSelectingCells = false;
        _cellSelectionDragBaseRanges = null;
        _cellSelectionDragBaseExcludedRows = null;
        _cellSelectionDragBaseSelectAllRows = false;
        _cellSelectionDragBaseColumns = null;
        _cellSelectionDragStartedOnSelected = false;
        _cellSelectionMouseStartedWithControl = false;
        _cellSelectionMouseStartedWithShift = false;
        NativeMethods.ReleaseCapture();

        if (shouldActivate)
        {
            ActivateRowAt(activatedRowIndex);
        }
    }

    private void UpdateGridAxisSelection(int x, int y)
    {
        if (Math.Abs(x - _cellSelectionMouseDownX) > 2 || Math.Abs(y - _cellSelectionMouseDownY) > 2)
        {
            _cellSelectionDragMoved = true;
        }

        if (_gridAxisSelectionKind == GridAxisSelectionKind.Column)
        {
            if (!TryHitTestGridColumn(x, out int columnIndex))
            {
                return;
            }

            if (_gridAxisFocusColumn == columnIndex)
            {
                return;
            }

            _gridAxisFocusColumn = columnIndex;
            ApplyGridColumnSelection(_gridAxisAnchorColumn > 0 ? _gridAxisAnchorColumn : columnIndex, columnIndex, isDrag: true);
            return;
        }

        if (_gridAxisSelectionKind == GridAxisSelectionKind.Row)
        {
            int rowIndex = GetDataRowIndexFromY(y, clamp: true);
            if (rowIndex < 0 || !TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
            {
                return;
            }

            if (_cellSelectionFocusKey is GridCellKey focusKey && focusKey.RowKey == rowKey)
            {
                return;
            }

            _cellSelectionFocusDataIndex = rowIndex;
            _cellSelectionFocusKey = new GridCellKey(rowKey, _cellSelectionAnchorKey?.ColumnIndex ?? GetFirstSelectedColumnOrDefault());
            ApplyGridRowSelection(_cellSelectionAnchorKey?.RowKey ?? rowKey, rowKey, isDrag: true);
        }
    }

    private void EndGridAxisSelection()
    {
        _isSelectingGridAxis = false;
        _gridAxisSelectionKind = GridAxisSelectionKind.None;
        _gridAxisDragStartedOnSelected = false;
        _cellSelectionDragBaseRanges = null;
        _cellSelectionDragBaseExcludedRows = null;
        _cellSelectionDragBaseSelectAllRows = false;
        _cellSelectionDragBaseColumns = null;
        _cellSelectionMouseStartedWithControl = false;
        _cellSelectionMouseStartedWithShift = false;
        NativeMethods.ReleaseCapture();
    }

    private int GetDataRowIndexFromY(int y, bool clamp)
    {
        if (_reader is null || !_reader.HasContent || _reader.CurrentRows.Count == 0)
        {
            return -1;
        }

        int headerLines = GetHeaderLineCount(_reader);
        int dataTop = headerLines * _lineHeight;
        if (!clamp && (y < dataTop || y < 0))
        {
            return -1;
        }

        int rowIndex = (y / _lineHeight) - headerLines;
        if (clamp)
        {
            return Math.Clamp(rowIndex, 0, _reader.CurrentRows.Count - 1);
        }

        return rowIndex >= 0 && rowIndex < _reader.CurrentRows.Count ? rowIndex : -1;
    }

    private bool TryHitTestGridCell(int x, int y, out int rowIndex, out int columnIndex, bool clampRow = false)
    {
        rowIndex = GetDataRowIndexFromY(y, clamp: clampRow);
        columnIndex = -1;
        if (rowIndex < 0 || _reader is not IColumnViewportReader columnReader || x < 0)
        {
            return false;
        }

        int[] widths = CalculateColumnWidths(columnReader);
        if (widths.Length <= 1)
        {
            return false;
        }

        int fixedWidthPx = Math.Max(1, widths[0]) * _charWidth;
        if (x < fixedWidthPx)
        {
            return false;
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        int startChars = 0;
        for (int i = 1; i < widths.Length; i++)
        {
            int widthChars = Math.Max(1, widths[i]);
            int left = fixedWidthPx + ((startChars - _xOffsetChars) * _charWidth);
            int right = fixedWidthPx + ((startChars + widthChars - _xOffsetChars) * _charWidth);
            int visibleLeft = Math.Max(left, fixedWidthPx);
            int visibleRight = Math.Min(right, clientRect.right);
            if (x >= visibleLeft && x < visibleRight)
            {
                columnIndex = i;
                return true;
            }

            startChars += widthChars;
        }

        return false;
    }

    private bool TryHitTestGridTextCell(
        int x,
        int y,
        out int rowIndex,
        out ViewportRowSelectionKey rowKey,
        out int columnIndex,
        out string text,
        out NativeMethods.RECT cellRect,
        out int widthChars)
    {
        rowKey = default;
        text = string.Empty;
        cellRect = default;
        widthChars = 0;
        if (!TryHitTestGridCell(x, y, out rowIndex, out columnIndex) ||
            columnIndex <= 0 ||
            !TryGetCurrentRowSelection(rowIndex, out rowKey, out _) ||
            !TryGetGridCellTextAndRect(rowIndex, columnIndex, out text, out cellRect, out _, out widthChars))
        {
            return false;
        }

        return true;
    }

    private bool TryGetGridCellTextAndRect(
        int rowIndex,
        int columnIndex,
        out string text,
        out NativeMethods.RECT cellRect,
        out NativeMethods.RECT visibleRect,
        out int widthChars)
    {
        text = string.Empty;
        cellRect = default;
        visibleRect = default;
        widthChars = 0;
        if (_reader is not IColumnViewportReader columnReader ||
            rowIndex < 0 ||
            columnIndex <= 0)
        {
            return false;
        }

        int[] widths = CalculateColumnWidths(columnReader);
        if (widths.Length <= 1 || columnIndex >= widths.Length)
        {
            return false;
        }

        if (columnReader.ColumnHeaders.Count > 0)
        {
            if (rowIndex >= columnReader.CurrentCells.Count)
            {
                return false;
            }

            IReadOnlyList<string> cells = columnReader.CurrentCells[rowIndex];
            text = columnIndex < cells.Count ? cells[columnIndex] : string.Empty;
        }
        else
        {
            if (columnIndex != 0 || rowIndex >= columnReader.CurrentRows.Count)
            {
                return false;
            }

            text = columnReader.CurrentRows[rowIndex];
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        int firstWidthPx = Math.Max(1, widths[0]) * _charWidth;
        int startChars = 0;
        for (int i = 1; i < widths.Length; i++)
        {
            int currentWidthChars = Math.Max(1, widths[i]);
            if (i == columnIndex)
            {
                widthChars = currentWidthChars;
                cellRect = new NativeMethods.RECT
                {
                    left = firstWidthPx + ((startChars - _xOffsetChars) * _charWidth),
                    top = (GetHeaderLineCount(_reader) + rowIndex) * _lineHeight,
                    right = firstWidthPx + ((startChars + currentWidthChars - _xOffsetChars) * _charWidth),
                    bottom = (GetHeaderLineCount(_reader) + rowIndex + 1) * _lineHeight
                };

                NativeMethods.RECT scrollRect = clientRect;
                scrollRect.left = Math.Max(scrollRect.left, firstWidthPx);
                visibleRect = Intersect(cellRect, scrollRect);
                return visibleRect.right > visibleRect.left && visibleRect.bottom > visibleRect.top;
            }

            startChars += currentWidthChars;
        }

        return false;
    }

    private bool TryHitTestGridColumn(int x, out int columnIndex)
    {
        columnIndex = -1;
        if (_reader is not IColumnViewportReader columnReader || x < 0)
        {
            return false;
        }

        int[] widths = CalculateColumnWidths(columnReader);
        if (widths.Length <= 1)
        {
            return false;
        }

        int fixedWidthPx = Math.Max(1, widths[0]) * _charWidth;
        if (x < fixedWidthPx)
        {
            return false;
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        int startChars = 0;
        for (int i = 1; i < widths.Length; i++)
        {
            int widthChars = Math.Max(1, widths[i]);
            int left = fixedWidthPx + ((startChars - _xOffsetChars) * _charWidth);
            int right = fixedWidthPx + ((startChars + widthChars - _xOffsetChars) * _charWidth);
            int visibleLeft = Math.Max(left, fixedWidthPx);
            int visibleRight = Math.Min(right, clientRect.right);
            if (x >= visibleLeft && x < visibleRight)
            {
                columnIndex = i;
                return true;
            }

            startChars += widthChars;
        }

        return false;
    }

    private bool TryHandleGridAxisClick(int x, int y)
    {
        if (_reader is not IColumnViewportReader)
        {
            return false;
        }

        if (y >= 0 && y < _lineHeight)
        {
            if (IsFixedLineNumberColumnHit(x))
            {
                ToggleAllGridCellsFromHeader();
                return true;
            }

            if (TryHitTestGridColumn(x, out int columnIndex))
            {
                BeginGridColumnSelection(columnIndex, x, y);
            }

            return columnIndex >= 0;
        }

        if (!IsFixedLineNumberColumnHit(x))
        {
            return false;
        }

        int rowIndex = GetDataRowIndexFromY(y, clamp: false);
        if (rowIndex >= 0 && TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            BeginGridRowSelection(rowIndex, rowKey, x, y);
        }

        return true;
    }

    private void BeginGridColumnSelection(int columnIndex, int x, int y)
    {
        bool control = IsControlKeyDown();
        bool shift = IsShiftKeyDown();
        _isSelectingGridAxis = true;
        _gridAxisSelectionKind = GridAxisSelectionKind.Column;
        _cellSelectionDragMoved = false;
        _gridAxisDragStartedOnSelected = _cellSelectionColumns.Contains(columnIndex);
        _cellSelectionMouseStartedWithControl = control;
        _cellSelectionMouseStartedWithShift = shift;
        _cellSelectionMouseDownX = x;
        _cellSelectionMouseDownY = y;
        CaptureGridAxisSelectionBase();

        int anchorColumn = columnIndex;
        if (shift)
        {
            if (_gridAxisAnchorColumn > 0)
            {
                anchorColumn = _gridAxisAnchorColumn;
            }
            else if (_cellSelectionAnchorKey is GridCellKey anchorKey && anchorKey.ColumnIndex > 0)
            {
                anchorColumn = anchorKey.ColumnIndex;
            }
        }
        else if (_reader?.CurrentRows.Count > 0 && TryGetCurrentRowSelection(0, out ViewportRowSelectionKey rowKey, out _))
        {
            _cellSelectionAnchorDataIndex = 0;
            _cellSelectionFocusDataIndex = 0;
            _cellSelectionAnchorKey = new GridCellKey(rowKey, columnIndex);
            _cellSelectionFocusKey = new GridCellKey(rowKey, columnIndex);
        }

        _gridAxisAnchorColumn = anchorColumn;
        _gridAxisFocusColumn = columnIndex;
        ApplyGridColumnSelection(anchorColumn, columnIndex, isDrag: false);
        NativeMethods.SetCapture(_hwnd);
    }

    private void BeginGridRowSelection(int rowIndex, ViewportRowSelectionKey rowKey, int x, int y)
    {
        bool control = IsControlKeyDown();
        bool shift = IsShiftKeyDown();
        _isSelectingGridAxis = true;
        _gridAxisSelectionKind = GridAxisSelectionKind.Row;
        _cellSelectionDragMoved = false;
        _gridAxisDragStartedOnSelected = IsSelectionKeySelected(rowKey);
        _cellSelectionMouseStartedWithControl = control;
        _cellSelectionMouseStartedWithShift = shift;
        _cellSelectionMouseDownX = x;
        _cellSelectionMouseDownY = y;
        CaptureGridAxisSelectionBase();

        if (!shift || _cellSelectionAnchorKey is null)
        {
            _cellSelectionAnchorDataIndex = rowIndex;
            _cellSelectionAnchorKey = new GridCellKey(rowKey, GetFirstSelectedColumnOrDefault());
        }
        else
        {
            TryFindCurrentRowIndex(_cellSelectionAnchorKey.Value.RowKey, out _cellSelectionAnchorDataIndex);
        }

        _cellSelectionFocusDataIndex = rowIndex;
        GridCellKey anchorKey = _cellSelectionAnchorKey ?? new GridCellKey(rowKey, GetFirstSelectedColumnOrDefault());
        _cellSelectionFocusKey = new GridCellKey(rowKey, anchorKey.ColumnIndex);
        ApplyGridRowSelection(anchorKey.RowKey, rowKey, isDrag: false);
        NativeMethods.SetCapture(_hwnd);
    }

    private void CaptureGridAxisSelectionBase()
    {
        _cellSelectionDragBaseRanges = new List<ViewportRowSelectionRange>(_selectionRanges);
        _cellSelectionDragBaseExcludedRows = new HashSet<ViewportRowSelectionKey>(_selectionExcludedRows);
        _cellSelectionDragBaseSelectAllRows = _selectionSelectAllRows;
        _cellSelectionDragBaseColumns = new HashSet<int>(_cellSelectionColumns);
    }

    private void ApplyMouseSelection(int rowIndex, bool isDrag)
    {
        if (!TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            return;
        }

        List<ViewportRowSelectionRange> nextRanges = _selectionDragBaseRanges is null
            ? new List<ViewportRowSelectionRange>()
            : new List<ViewportRowSelectionRange>(_selectionDragBaseRanges);
        HashSet<ViewportRowSelectionKey> nextExcluded = _selectionDragBaseExcludedRows is null
            ? new HashSet<ViewportRowSelectionKey>()
            : new HashSet<ViewportRowSelectionKey>(_selectionDragBaseExcludedRows);
        bool nextSelectAll = _selectionDragBaseSelectAllRows;
        ViewportRowSelectionKey anchorKey = _selectionAnchorKey ?? rowKey;

        if (_selectionMouseStartedWithControl && _selectionMouseStartedWithShift)
        {
            AddRangeSelection(nextRanges, nextExcluded, anchorKey, rowKey);
        }
        else if (_selectionMouseStartedWithShift)
        {
            nextSelectAll = false;
            nextRanges.Clear();
            nextExcluded.Clear();
            AddRangeSelection(nextRanges, nextExcluded, anchorKey, rowKey);
        }
        else if (_selectionMouseStartedWithControl)
        {
            if (isDrag)
            {
                AddRangeSelection(nextRanges, nextExcluded, anchorKey, rowKey);
            }
            else
            {
                ToggleRowSelection(ref nextSelectAll, nextRanges, nextExcluded, rowKey);
            }
        }
        else
        {
            nextSelectAll = false;
            nextRanges.Clear();
            nextExcluded.Clear();
            AddRangeSelection(nextRanges, nextExcluded, anchorKey, rowKey);
        }

        ReplaceSelection(nextSelectAll, nextRanges, nextExcluded);
    }

    private void ApplyMouseCellSelection(GridCellKey key, bool isDrag)
    {
        bool nextSelectAllRows = _cellSelectionDragBaseSelectAllRows;
        List<ViewportRowSelectionRange> nextRanges = _cellSelectionDragBaseRanges is null
            ? new List<ViewportRowSelectionRange>()
            : new List<ViewportRowSelectionRange>(_cellSelectionDragBaseRanges);
        HashSet<ViewportRowSelectionKey> nextExcluded = _cellSelectionDragBaseExcludedRows is null
            ? new HashSet<ViewportRowSelectionKey>()
            : new HashSet<ViewportRowSelectionKey>(_cellSelectionDragBaseExcludedRows);
        HashSet<int> nextColumns = _cellSelectionDragBaseColumns is null
            ? new HashSet<int>()
            : new HashSet<int>(_cellSelectionDragBaseColumns);
        GridCellKey anchorKey = _cellSelectionAnchorKey ?? key;

        if (_cellSelectionMouseStartedWithControl && _cellSelectionMouseStartedWithShift)
        {
            AddCellAxisRange(nextRanges, nextExcluded, nextColumns, anchorKey, key);
        }
        else if (_cellSelectionMouseStartedWithShift)
        {
            nextSelectAllRows = false;
            nextRanges.Clear();
            nextExcluded.Clear();
            nextColumns.Clear();
            AddCellAxisRange(nextRanges, nextExcluded, nextColumns, anchorKey, key);
        }
        else if (_cellSelectionMouseStartedWithControl)
        {
            if (isDrag && _cellSelectionDragStartedOnSelected)
            {
                RemoveCellAxisRows(nextSelectAllRows, nextRanges, nextExcluded, anchorKey, key);
            }
            else if (isDrag)
            {
                AddCellAxisRange(nextRanges, nextExcluded, nextColumns, anchorKey, key);
            }
            else
            {
                ToggleCellAxisSelection(ref nextSelectAllRows, nextRanges, nextExcluded, nextColumns, key);
            }
        }
        else
        {
            nextSelectAllRows = false;
            nextRanges.Clear();
            nextExcluded.Clear();
            nextColumns.Clear();
            AddCellAxisRange(nextRanges, nextExcluded, nextColumns, anchorKey, key);
        }

        ReplaceCellSelection(nextSelectAllRows, nextRanges, nextExcluded, nextColumns);
    }

    private void ReplaceCellSelection(bool selectAllRows, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded, HashSet<int> columns)
    {
        ClearTextSelection(invalidate: false);
        if (CellSelectionEqual(selectAllRows, ranges, excluded, columns))
        {
            return;
        }

        _selectionSelectAllRows = selectAllRows;
        _selectionRanges.Clear();
        _selectionRanges.AddRange(ranges);
        _selectionExcludedRows.Clear();
        foreach (ViewportRowSelectionKey key in excluded)
        {
            _selectionExcludedRows.Add(key);
        }

        _selectionAnchorDataIndex = -1;
        _selectionFocusDataIndex = -1;
        _selectionAnchorKey = null;
        _selectionFocusKey = null;
        _cellSelectionColumns.Clear();
        foreach (int column in columns)
        {
            if (column > 0)
            {
                _cellSelectionColumns.Add(column);
            }
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private bool CellSelectionEqual(bool selectAllRows, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded, HashSet<int> columns)
    {
        if (_selectionSelectAllRows != selectAllRows ||
            _selectionRanges.Count != ranges.Count ||
            _selectionExcludedRows.Count != excluded.Count ||
            _cellSelectionColumns.Count != columns.Count)
        {
            return false;
        }

        for (int i = 0; i < ranges.Count; i++)
        {
            if (!_selectionRanges[i].Equals(ranges[i]))
            {
                return false;
            }
        }

        foreach (ViewportRowSelectionKey key in excluded)
        {
            if (!_selectionExcludedRows.Contains(key))
            {
                return false;
            }
        }

        foreach (int column in columns)
        {
            if (!_cellSelectionColumns.Contains(column))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddCellAxisRange(List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded, HashSet<int> columns, GridCellKey first, GridCellKey last)
    {
        AddRangeSelection(ranges, excluded, first.RowKey, last.RowKey);
        int startColumn = Math.Min(first.ColumnIndex, last.ColumnIndex);
        int endColumn = Math.Max(first.ColumnIndex, last.ColumnIndex);
        for (int column = startColumn; column <= endColumn; column++)
        {
            if (column > 0)
            {
                columns.Add(column);
            }
        }
    }

    private static void ToggleCellAxisSelection(ref bool selectAllRows, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded, HashSet<int> columns, GridCellKey key)
    {
        if (IsSelectionKeySelected(selectAllRows, ranges, excluded, key.RowKey) && columns.Contains(key.ColumnIndex))
        {
            excluded.Add(key.RowKey);
            return;
        }

        excluded.Remove(key.RowKey);
        ranges.Add(new ViewportRowSelectionRange(key.RowKey, key.RowKey));
        if (key.ColumnIndex > 0)
        {
            columns.Add(key.ColumnIndex);
        }
    }

    private void ToggleGridRowSelection(int rowIndex, ViewportRowSelectionKey rowKey, bool preserveExisting)
    {
        bool nextSelectAllRows = preserveExisting && _selectionSelectAllRows;
        List<ViewportRowSelectionRange> nextRanges = preserveExisting
            ? new List<ViewportRowSelectionRange>(_selectionRanges)
            : new List<ViewportRowSelectionRange>();
        HashSet<ViewportRowSelectionKey> nextExcluded = preserveExisting
            ? new HashSet<ViewportRowSelectionKey>(_selectionExcludedRows)
            : new HashSet<ViewportRowSelectionKey>();
        HashSet<int> nextColumns = preserveExisting
            ? new HashSet<int>(_cellSelectionColumns)
            : new HashSet<int>();

        if (preserveExisting && IsSelectionKeySelected(nextSelectAllRows, nextRanges, nextExcluded, rowKey))
        {
            nextExcluded.Add(rowKey);
        }
        else
        {
            nextExcluded.Remove(rowKey);
            nextRanges.Add(new ViewportRowSelectionRange(rowKey, rowKey));
            if (nextColumns.Count == 0 && _reader is IColumnViewportReader columnReader)
            {
                AddAllCopyableColumns(nextColumns, columnReader);
            }
        }

        int focusColumn = nextColumns.Count > 0 ? GetFirstSelectedColumn(nextColumns) : 1;
        _cellSelectionAnchorDataIndex = rowIndex;
        _cellSelectionFocusDataIndex = rowIndex;
        _cellSelectionAnchorKey = new GridCellKey(rowKey, focusColumn);
        _cellSelectionFocusKey = new GridCellKey(rowKey, focusColumn);
        ReplaceCellSelection(nextSelectAllRows, nextRanges, nextExcluded, nextColumns);
    }

    private void ToggleGridColumnSelection(int columnIndex, bool preserveExisting)
    {
        if (_reader is not IColumnViewportReader columnReader)
        {
            return;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (columnIndex <= 0 || columnIndex >= columnCount)
        {
            return;
        }

        bool nextSelectAllRows = preserveExisting && _selectionSelectAllRows;
        List<ViewportRowSelectionRange> nextRanges = preserveExisting
            ? new List<ViewportRowSelectionRange>(_selectionRanges)
            : new List<ViewportRowSelectionRange>();
        HashSet<ViewportRowSelectionKey> nextExcluded = preserveExisting
            ? new HashSet<ViewportRowSelectionKey>(_selectionExcludedRows)
            : new HashSet<ViewportRowSelectionKey>();
        HashSet<int> nextColumns = preserveExisting
            ? new HashSet<int>(_cellSelectionColumns)
            : new HashSet<int>();

        if (preserveExisting && nextColumns.Contains(columnIndex))
        {
            nextColumns.Remove(columnIndex);
        }
        else
        {
            nextColumns.Add(columnIndex);
            if (!HasAnySelectedRows(nextSelectAllRows, nextRanges))
            {
                nextSelectAllRows = true;
                nextRanges.Clear();
                nextExcluded.Clear();
            }
        }

        if (_reader.CurrentRows.Count > 0 && TryGetCurrentRowSelection(0, out ViewportRowSelectionKey rowKey, out _))
        {
            _cellSelectionAnchorDataIndex = 0;
            _cellSelectionFocusDataIndex = 0;
            _cellSelectionAnchorKey = new GridCellKey(rowKey, columnIndex);
            _cellSelectionFocusKey = new GridCellKey(rowKey, columnIndex);
        }

        ReplaceCellSelection(nextSelectAllRows, nextRanges, nextExcluded, nextColumns);
    }

    private void ApplyGridColumnSelection(int anchorColumn, int focusColumn, bool isDrag)
    {
        if (_reader is not IColumnViewportReader columnReader)
        {
            return;
        }

        int columnCount = GetGridColumnCount(columnReader);
        int startColumn = Math.Clamp(Math.Min(anchorColumn, focusColumn), 1, Math.Max(1, columnCount - 1));
        int endColumn = Math.Clamp(Math.Max(anchorColumn, focusColumn), 1, Math.Max(1, columnCount - 1));
        bool nextSelectAllRows = _cellSelectionDragBaseSelectAllRows;
        List<ViewportRowSelectionRange> nextRanges = _cellSelectionDragBaseRanges is null
            ? new List<ViewportRowSelectionRange>()
            : new List<ViewportRowSelectionRange>(_cellSelectionDragBaseRanges);
        HashSet<ViewportRowSelectionKey> nextExcluded = _cellSelectionDragBaseExcludedRows is null
            ? new HashSet<ViewportRowSelectionKey>()
            : new HashSet<ViewportRowSelectionKey>(_cellSelectionDragBaseExcludedRows);
        HashSet<int> nextColumns = _cellSelectionDragBaseColumns is null
            ? new HashSet<int>()
            : new HashSet<int>(_cellSelectionDragBaseColumns);

        if (_cellSelectionMouseStartedWithControl && _gridAxisDragStartedOnSelected)
        {
            for (int column = startColumn; column <= endColumn; column++)
            {
                nextColumns.Remove(column);
            }
        }
        else if (_cellSelectionMouseStartedWithControl)
        {
            for (int column = startColumn; column <= endColumn; column++)
            {
                nextColumns.Add(column);
            }
        }
        else if (_cellSelectionMouseStartedWithShift)
        {
            nextColumns.Clear();
            for (int column = startColumn; column <= endColumn; column++)
            {
                nextColumns.Add(column);
            }
        }
        else
        {
            nextSelectAllRows = true;
            nextRanges.Clear();
            nextExcluded.Clear();
            nextColumns.Clear();
            for (int column = startColumn; column <= endColumn; column++)
            {
                nextColumns.Add(column);
            }
        }

        if (nextColumns.Count > 0 && !HasAnySelectedRows(nextSelectAllRows, nextRanges))
        {
            nextSelectAllRows = true;
            nextRanges.Clear();
            nextExcluded.Clear();
        }

        ReplaceCellSelection(nextSelectAllRows, nextRanges, nextExcluded, nextColumns);
    }

    private void ApplyGridRowSelection(ViewportRowSelectionKey anchorRow, ViewportRowSelectionKey focusRow, bool isDrag)
    {
        bool nextSelectAllRows = _cellSelectionDragBaseSelectAllRows;
        List<ViewportRowSelectionRange> nextRanges = _cellSelectionDragBaseRanges is null
            ? new List<ViewportRowSelectionRange>()
            : new List<ViewportRowSelectionRange>(_cellSelectionDragBaseRanges);
        HashSet<ViewportRowSelectionKey> nextExcluded = _cellSelectionDragBaseExcludedRows is null
            ? new HashSet<ViewportRowSelectionKey>()
            : new HashSet<ViewportRowSelectionKey>(_cellSelectionDragBaseExcludedRows);
        HashSet<int> nextColumns = _cellSelectionDragBaseColumns is null
            ? new HashSet<int>()
            : new HashSet<int>(_cellSelectionDragBaseColumns);

        if (_cellSelectionMouseStartedWithControl && _gridAxisDragStartedOnSelected)
        {
            RemoveCellAxisRows(nextSelectAllRows, nextRanges, nextExcluded, new GridCellKey(anchorRow, 1), new GridCellKey(focusRow, 1));
        }
        else if (_cellSelectionMouseStartedWithControl)
        {
            AddRangeSelection(nextRanges, nextExcluded, anchorRow, focusRow);
        }
        else if (_cellSelectionMouseStartedWithShift)
        {
            nextSelectAllRows = false;
            nextRanges.Clear();
            nextExcluded.Clear();
            AddRangeSelection(nextRanges, nextExcluded, anchorRow, focusRow);
        }
        else
        {
            nextSelectAllRows = false;
            nextRanges.Clear();
            nextExcluded.Clear();
            nextColumns.Clear();
            AddRangeSelection(nextRanges, nextExcluded, anchorRow, focusRow);
        }

        if (nextColumns.Count == 0 && _reader is IColumnViewportReader columnReader)
        {
            AddAllCopyableColumns(nextColumns, columnReader);
        }

        ReplaceCellSelection(nextSelectAllRows, nextRanges, nextExcluded, nextColumns);
    }

    private void ToggleAllGridCellsFromHeader()
    {
        if (IsControlKeyDown() && IsAllGridCellsSelected())
        {
            ClearSelection(invalidate: true);
            return;
        }

        SelectAllGridCells();
    }

    private void RemoveCellAxisRows(bool selectAllRows, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded, GridCellKey first, GridCellKey last)
    {
        if (_reader is not ISelectableViewportReader selectableReader)
        {
            excluded.Add(last.RowKey);
            return;
        }

        ViewportRowSelectionRange removeRange = new(first.RowKey, last.RowKey);
        IReadOnlyList<ViewportRowSelectionKey> keys = selectableReader.CurrentRowSelectionKeys;
        bool removedAny = false;
        for (int i = 0; i < keys.Count; i++)
        {
            if (removeRange.Contains(keys[i]) && IsSelectionKeySelected(selectAllRows, ranges, excluded, keys[i]))
            {
                excluded.Add(keys[i]);
                removedAny = true;
            }
        }

        if (!removedAny)
        {
            excluded.Add(last.RowKey);
        }
    }

    private static bool HasAnySelectedRows(bool selectAllRows, List<ViewportRowSelectionRange> ranges) =>
        selectAllRows || ranges.Count > 0;

    private static int GetFirstSelectedColumn(HashSet<int> columns)
    {
        int firstColumn = int.MaxValue;
        foreach (int column in columns)
        {
            if (column > 0 && column < firstColumn)
            {
                firstColumn = column;
            }
        }

        return firstColumn == int.MaxValue ? 1 : firstColumn;
    }

    private int GetFirstSelectedColumnOrDefault() => GetFirstSelectedColumn(_cellSelectionColumns);

    private static void AddAllCopyableColumns(HashSet<int> columns, IColumnViewportReader columnReader)
    {
        int columnCount = GetGridColumnCount(columnReader);
        for (int column = 1; column < columnCount; column++)
        {
            columns.Add(column);
        }
    }

    private void ReplaceSelection(bool selectAll, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded)
    {
        ClearTextSelection(invalidate: false);
        if (SelectionEqual(selectAll, ranges, excluded))
        {
            return;
        }

        _selectionSelectAllRows = selectAll;
        _selectionRanges.Clear();
        _selectionRanges.AddRange(ranges);
        _selectionExcludedRows.Clear();
        foreach (ViewportRowSelectionKey key in excluded)
        {
            _selectionExcludedRows.Add(key);
        }

        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private void SelectLoadedRowByOffset(long offset)
    {
        if (_reader is not ISelectableViewportReader selectableReader)
        {
            ClearSelection(invalidate: false);
            return;
        }

        IReadOnlyList<ViewportRowSelectionKey> keys = selectableReader.CurrentRowSelectionKeys;
        if (keys.Count == 0)
        {
            ClearSelection(invalidate: false);
            return;
        }

        int selectedIndex = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].StartOffset <= offset && offset <= Math.Max(keys[i].StartOffset, keys[i].EndOffset))
            {
                selectedIndex = i;
                break;
            }
        }

        ViewportRowSelectionKey rowKey = keys[selectedIndex];
        _selectionSelectAllRows = false;
        _selectionRanges.Clear();
        _selectionRanges.Add(new ViewportRowSelectionRange(rowKey, rowKey));
        _selectionExcludedRows.Clear();
        _selectionAnchorDataIndex = selectedIndex;
        _selectionFocusDataIndex = selectedIndex;
        _selectionAnchorKey = rowKey;
        _selectionFocusKey = rowKey;
        _isSelectingRows = false;
        _selectionDragMoved = false;
        _selectionDragBaseRanges = null;
        _selectionDragBaseExcludedRows = null;
        _selectionDragBaseSelectAllRows = false;
    }

    private bool SelectionEqual(bool selectAll, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded)
    {
        if (_selectionSelectAllRows != selectAll ||
            _selectionRanges.Count != ranges.Count ||
            _selectionExcludedRows.Count != excluded.Count)
        {
            return false;
        }

        for (int i = 0; i < ranges.Count; i++)
        {
            if (!_selectionRanges[i].Equals(ranges[i]))
            {
                return false;
            }
        }

        foreach (ViewportRowSelectionKey key in excluded)
        {
            if (!_selectionExcludedRows.Contains(key))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddRangeSelection(List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded, ViewportRowSelectionKey first, ViewportRowSelectionKey last)
    {
        ViewportRowSelectionRange range = new(first, last);
        ranges.Add(range);
        RemoveExcludedRowsInRange(excluded, range);
    }

    private static void ToggleRowSelection(ref bool selectAll, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded, ViewportRowSelectionKey key)
    {
        if (IsSelectionKeySelected(selectAll, ranges, excluded, key))
        {
            excluded.Add(key);
            return;
        }

        excluded.Remove(key);
        ranges.Add(new ViewportRowSelectionRange(key, key));
    }

    private static void RemoveExcludedRowsInRange(HashSet<ViewportRowSelectionKey> excluded, ViewportRowSelectionRange range)
    {
        if (excluded.Count == 0)
        {
            return;
        }

        List<ViewportRowSelectionKey> toRemove = new();
        foreach (ViewportRowSelectionKey key in excluded)
        {
            if (range.Contains(key))
            {
                toRemove.Add(key);
            }
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            excluded.Remove(toRemove[i]);
        }
    }

    private void ClearSelection(bool invalidate)
    {
        bool hadSelection = _isSelectingRows || _isSelectingText || _isSelectingCells || _isSelectingGridAxis || HasSelection;
        if (_isSelectingRows)
        {
            NativeMethods.ReleaseCapture();
        }

        if (_isSelectingText)
        {
            NativeMethods.ReleaseCapture();
        }

        if (_isSelectingCells)
        {
            NativeMethods.ReleaseCapture();
        }

        if (_isSelectingGridAxis)
        {
            NativeMethods.ReleaseCapture();
        }

        _isSelectingRows = false;
        _isSelectingText = false;
        _isSelectingCells = false;
        _isSelectingGridAxis = false;
        _gridAxisSelectionKind = GridAxisSelectionKind.None;
        _selectionDragMoved = false;
        _cellSelectionDragMoved = false;
        _cellSelectionDragStartedOnSelected = false;
        _gridAxisDragStartedOnSelected = false;
        _selectionMouseStartedWithControl = false;
        _selectionMouseStartedWithShift = false;
        _cellSelectionMouseStartedWithControl = false;
        _cellSelectionMouseStartedWithShift = false;
        _textSelectionDragThresholdArmed = false;
        _selectionDragBaseRanges = null;
        _selectionDragBaseExcludedRows = null;
        _selectionDragBaseSelectAllRows = false;
        _cellSelectionDragBaseRanges = null;
        _cellSelectionDragBaseExcludedRows = null;
        _cellSelectionDragBaseSelectAllRows = false;
        _cellSelectionDragBaseColumns = null;
        _selectionMouseDownX = 0;
        _selectionMouseDownY = 0;
        _cellSelectionMouseDownX = 0;
        _cellSelectionMouseDownY = 0;
        _selectionAnchorDataIndex = -1;
        _selectionFocusDataIndex = -1;
        _textSelectionDataIndex = -1;
        _textSelectionColumnIndex = -1;
        _textSelectionAnchorChar = -1;
        _textSelectionFocusChar = -1;
        _cellSelectionAnchorDataIndex = -1;
        _cellSelectionFocusDataIndex = -1;
        _gridAxisAnchorColumn = -1;
        _gridAxisFocusColumn = -1;
        _selectionAnchorKey = null;
        _selectionFocusKey = null;
        _textSelectionRowKey = null;
        _textSelectionSegmentKey = null;
        _textSelectionContext = null;
        _cellSelectionAnchorKey = null;
        _cellSelectionFocusKey = null;
        _selectionSelectAllRows = false;
        _selectionRanges.Clear();
        _selectionExcludedRows.Clear();
        _cellSelectionColumns.Clear();

        if (hadSelection && invalidate && _hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void ClearTextSelection(bool invalidate)
    {
        bool hadSelection = _isSelectingText || HasTextSelection;
        if (_isSelectingText)
        {
            NativeMethods.ReleaseCapture();
        }

        _isSelectingText = false;
        _textSelectionDragThresholdArmed = false;
        _textSelectionRowKey = null;
        _textSelectionSegmentKey = null;
        _textSelectionContext = null;
        _textSelectionDataIndex = -1;
        _textSelectionColumnIndex = -1;
        _textSelectionAnchorChar = -1;
        _textSelectionFocusChar = -1;
        if (hadSelection && invalidate && _hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private bool IsDataRowSelected(int dataRowIndex) =>
        !IsSearchCellSelectionMode &&
        TryGetCurrentRowSelection(dataRowIndex, out ViewportRowSelectionKey key, out _) &&
        IsSelectionKeySelected(key);

    private bool IsSelectionKeySelected(ViewportRowSelectionKey key) =>
        IsSelectionKeySelected(_selectionSelectAllRows, _selectionRanges, _selectionExcludedRows, key);

    private IntPtr GetInactiveSelectionBrush() =>
        _inactiveSelectionBrush != IntPtr.Zero
            ? _inactiveSelectionBrush
            : NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DFACE);

    private HighlightStyle? TryGetGridRowHighlight(IReadOnlyList<string> rows, int dataRowIndex)
    {
        if (dataRowIndex < 0 || dataRowIndex >= rows.Count)
        {
            return null;
        }

        return TryGetHighlightStyle(dataRowIndex, NormalizeDisplayText(rows[dataRowIndex]), out HighlightStyle style)
            ? style
            : null;
    }

    private bool TryGetHighlightStyle(int rowIndex, string fallbackText, out HighlightStyle style)
    {
        if (_reader is IHighlightGroupViewportReader highlightReader)
        {
            IReadOnlyList<ViewportHighlightGroupKey> keys = highlightReader.CurrentHighlightGroupKeys;
            if (rowIndex >= 0 && rowIndex < keys.Count &&
                _highlightStyleCache.TryGetValue(keys[rowIndex], out HighlightStyle? cachedStyle) &&
                cachedStyle is HighlightStyle matchedStyle)
            {
                style = matchedStyle;
                return true;
            }

            style = default;
            return false;
        }

        if (_highlightRules.Count > 0)
        {
            return HighlightRuleCompiler.TryMatch(_highlightRules, fallbackText, out style);
        }

        style = default;
        return false;
    }

    private static Dictionary<ViewportHighlightGroupKey, HighlightStyle?> PrepareHighlightStyleCache(
        IViewportReader reader,
        IReadOnlyList<CompiledHighlightRule> rules,
        Dictionary<ViewportHighlightGroupKey, HighlightStyle?> cache)
    {
        if (rules.Count == 0 || reader is not IHighlightGroupViewportReader highlightReader)
        {
            return new Dictionary<ViewportHighlightGroupKey, HighlightStyle?>();
        }

        IReadOnlyList<ViewportHighlightGroup> groups = highlightReader.ReadCurrentHighlightGroups();
        if (cache.Count + groups.Count > MaximumHighlightStyleCacheEntries)
        {
            cache.Clear();
        }

        for (int i = 0; i < groups.Count; i++)
        {
            ViewportHighlightGroup group = groups[i];
            if (cache.ContainsKey(group.Key))
            {
                continue;
            }

            string displayText = NormalizeDisplayText(group.Text);
            cache[group.Key] = HighlightRuleCompiler.TryMatch(rules, displayText, out HighlightStyle style)
                ? style
                : null;
        }

        return cache;
    }

    private void ReplaceHighlightStyleCache(Dictionary<ViewportHighlightGroupKey, HighlightStyle?>? cache)
    {
        _highlightStyleCache.Clear();
        if (cache is null)
        {
            return;
        }

        foreach ((ViewportHighlightGroupKey key, HighlightStyle? style) in cache)
        {
            _highlightStyleCache[key] = style;
        }
    }

    private IntPtr GetHighlightFont(HighlightStyle? highlight, bool selected)
    {
        if (selected || highlight is not HighlightStyle style)
        {
            return _font;
        }

        if (style.Bold && style.Italic)
        {
            return _boldItalicFont;
        }

        if (style.Bold)
        {
            return _boldFont;
        }

        return style.Italic ? _italicFont : _font;
    }

    private IntPtr GetHighlightBrush(int color)
    {
        if (_highlightBrushes.TryGetValue(color, out IntPtr brush))
        {
            return brush;
        }

        brush = NativeMethods.CreateSolidBrush(color);
        if (brush == IntPtr.Zero)
        {
            return NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW);
        }

        _highlightBrushes.Add(color, brush);
        return brush;
    }

    private void ClearHighlightBrushes()
    {
        foreach (IntPtr brush in _highlightBrushes.Values)
        {
            if (brush != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(brush);
            }
        }

        _highlightBrushes.Clear();
    }

    private static bool IsSelectionKeySelected(
        bool selectAll,
        IReadOnlyList<ViewportRowSelectionRange> ranges,
        HashSet<ViewportRowSelectionKey> excluded,
        ViewportRowSelectionKey key)
    {
        if (excluded.Contains(key))
        {
            return false;
        }

        if (selectAll)
        {
            return true;
        }

        for (int i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasSelection => HasTextSelection || _selectionSelectAllRows || _selectionRanges.Count > 0 || _cellSelectionColumns.Count > 0;

    private bool HasTextSelection => _textSelectionRowKey is not null || (_textSelectionSegmentKey is not null && _textSelectionContext is not null);

    private bool HasTextSelectionRange => HasTextSelection && _textSelectionAnchorChar != _textSelectionFocusChar;

    private bool IsSearchCellSelectionMode => _onRowActivated is not null && _reader is IColumnViewportReader;

    private bool IsGridCellSelected(int dataRowIndex, int columnIndex)
    {
        if (!IsSearchCellSelectionMode ||
            columnIndex <= 0 ||
            !TryGetCurrentRowSelection(dataRowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            return false;
        }

        return IsGridCellSelected(new GridCellKey(rowKey, columnIndex));
    }

    private bool IsGridCellSelected(GridCellKey key)
    {
        if (key.ColumnIndex <= 0)
        {
            return false;
        }

        return _cellSelectionColumns.Contains(key.ColumnIndex) && IsSelectionKeySelected(key.RowKey);
    }

    private bool IsSearchColumnAxisSelected(int columnIndex) =>
        IsSearchCellSelectionMode &&
        columnIndex > 0 &&
        _cellSelectionColumns.Contains(columnIndex);

    private bool IsSearchRowAxisSelected(int dataRowIndex) =>
        IsSearchCellSelectionMode &&
        dataRowIndex >= 0 &&
        TryGetCurrentRowSelection(dataRowIndex, out ViewportRowSelectionKey rowKey, out _) &&
        IsSelectionKeySelected(rowKey);

    private bool IsAllGridCellsSelected()
    {
        if (_reader is not IColumnViewportReader columnReader ||
            !_selectionSelectAllRows ||
            _selectionExcludedRows.Count > 0)
        {
            return false;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (columnCount <= 1)
        {
            return false;
        }

        for (int column = 1; column < columnCount; column++)
        {
            if (!_cellSelectionColumns.Contains(column))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetCurrentRowSelection(int rowIndex, out ViewportRowSelectionKey key, out string text)
    {
        key = default;
        text = string.Empty;
        if (_reader is not ISelectableViewportReader selectableReader)
        {
            return false;
        }

        IReadOnlyList<ViewportRowSelectionKey> keys = selectableReader.CurrentRowSelectionKeys;
        IReadOnlyList<string> rows = _reader.CurrentRows;
        if (rowIndex < 0 || rowIndex >= keys.Count || rowIndex >= rows.Count)
        {
            return false;
        }

        key = keys[rowIndex];
        text = rows[rowIndex];
        return true;
    }

    private bool TryGetTextSelectionContext(
        int rowIndex,
        out ViewportTextSegmentKey segmentKey,
        out ViewportTextSelectionContext context,
        out ViewportTextSegmentRange segmentRange)
    {
        segmentKey = default;
        context = default;
        segmentRange = default;
        if (_reader is not ITextSelectionViewportReader textReader)
        {
            return false;
        }

        IReadOnlyList<ViewportTextSegmentKey> keys = textReader.CurrentTextSegmentKeys;
        if (rowIndex < 0 || rowIndex >= keys.Count)
        {
            return false;
        }

        segmentKey = keys[rowIndex];
        try
        {
            return textReader.TryReadTextSelectionContext(segmentKey, out context) &&
                TryGetTextSegmentRange(context, segmentKey, out segmentRange);
        }
        catch (FilteredLineStaleException)
        {
            _onStale?.Invoke(this);
            return false;
        }
    }

    private static bool TryGetTextSegmentRange(
        ViewportTextSelectionContext context,
        ViewportTextSegmentKey key,
        out ViewportTextSegmentRange segmentRange)
    {
        for (int i = 0; i < context.Segments.Count; i++)
        {
            if (context.Segments[i].Key == key)
            {
                segmentRange = context.Segments[i];
                return true;
            }
        }

        segmentRange = default;
        return false;
    }

    private bool TryGetLogicalTextSelectionPaintRange(
        int rowIndex,
        out int selectionStart,
        out int selectionEnd,
        out bool showIndicator)
    {
        selectionStart = 0;
        selectionEnd = 0;
        showIndicator = false;
        if (_textSelectionSegmentKey is null ||
            _textSelectionContext is not ViewportTextSelectionContext context ||
            _reader is not ITextSelectionViewportReader textReader)
        {
            return false;
        }

        IReadOnlyList<ViewportTextSegmentKey> keys = textReader.CurrentTextSegmentKeys;
        if (rowIndex < 0 || rowIndex >= keys.Count)
        {
            return false;
        }

        ViewportTextSegmentKey currentSegment = keys[rowIndex];
        if (currentSegment.GroupKey != context.GroupKey ||
            !TryGetTextSegmentRange(context, currentSegment, out ViewportTextSegmentRange segmentRange))
        {
            return false;
        }

        showIndicator = true;
        if (!HasTextSelectionRange)
        {
            return true;
        }

        int globalStart = Math.Clamp(Math.Min(_textSelectionAnchorChar, _textSelectionFocusChar), 0, context.Text.Length);
        int globalEnd = Math.Clamp(Math.Max(_textSelectionAnchorChar, _textSelectionFocusChar), 0, context.Text.Length);
        int segmentEnd = segmentRange.Start + segmentRange.Length;
        int intersectionStart = Math.Max(globalStart, segmentRange.Start);
        int intersectionEnd = Math.Min(globalEnd, segmentEnd);
        if (intersectionEnd <= intersectionStart)
        {
            return true;
        }

        selectionStart = intersectionStart - segmentRange.Start;
        selectionEnd = intersectionEnd - segmentRange.Start;
        return true;
    }

    private ViewportRowSelectionKey? GetCurrentRowSelectionKey(int rowIndex) =>
        TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey key, out _) ? key : null;

    private bool TryFindCurrentRowIndex(ViewportRowSelectionKey? key, out int rowIndex)
    {
        rowIndex = -1;
        if (key is null || _reader is not ISelectableViewportReader selectableReader)
        {
            return false;
        }

        IReadOnlyList<ViewportRowSelectionKey> keys = selectableReader.CurrentRowSelectionKeys;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Equals(key.Value))
            {
                rowIndex = i;
                return true;
            }
        }

        return false;
    }

    private bool TryFindCurrentTextSegmentIndex(ViewportTextSegmentKey key, out int rowIndex)
    {
        rowIndex = -1;
        if (_reader is not ITextSelectionViewportReader textReader)
        {
            return false;
        }

        IReadOnlyList<ViewportTextSegmentKey> keys = textReader.CurrentTextSegmentKeys;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] == key)
            {
                rowIndex = i;
                return true;
            }
        }

        return false;
    }

    private void ActivateRowAt(int dataRowIndex)
    {
        if (_onRowActivated is null ||
            _reader is not IFileOffsetViewportReader offsetReader ||
            _reader is not ISelectableViewportReader selectableReader)
        {
            return;
        }

        IReadOnlyList<ViewportRowSelectionKey> rowKeys = selectableReader.CurrentRowSelectionKeys;
        if (dataRowIndex < 0 || dataRowIndex >= _reader.CurrentRows.Count || dataRowIndex >= rowKeys.Count)
        {
            return;
        }

        ViewportRowSelectionKey rowKey = rowKeys[dataRowIndex];
        long rowOrdinal = offsetReader.TopRowOrdinal + dataRowIndex;
        try
        {
            if (offsetReader.TryGetRowStartOffset(rowOrdinal, out long startOffset) && startOffset == rowKey.StartOffset)
            {
                _onRowActivated(this, rowKey);
            }
        }
        catch (FilteredLineStaleException)
        {
            _onStale?.Invoke(this);
        }
    }

    private void OnKeyDown(int key)
    {
        bool control = IsControlKeyDown();
        bool shift = IsShiftKeyDown();
        if (key == NativeMethods.VK_A && control)
        {
            if (IsSearchCellSelectionMode)
            {
                SelectAllGridCells();
            }
            else
            {
                SelectAllRows();
            }

            return;
        }

        if (key == NativeMethods.VK_C && control)
        {
            CopySelectionToClipboard();
            return;
        }

        if (key == NativeMethods.VK_V && control)
        {
            _onPasteRequested?.Invoke();
            return;
        }

        if (control && TryHandleControlArrowKey(key, extendSelection: shift))
        {
            return;
        }

        if (shift)
        {
            if (IsSearchCellSelectionMode)
            {
                TryHandleShiftCellSelectionKey(key);
                return;
            }

            if (TryHandleShiftSelectionKey(key))
            {
                return;
            }
        }

        if (TryHandleSearchResultKeyboardNavigation(key))
        {
            return;
        }

        switch (key)
        {
            case NativeMethods.VK_LEFT:
                SetHorizontalOffset(_xOffsetChars - 1);
                break;
            case NativeMethods.VK_RIGHT:
                SetHorizontalOffset(_xOffsetChars + 1);
                break;
            case NativeMethods.VK_UP:
                ScrollByLines(-1);
                break;
            case NativeMethods.VK_DOWN:
                ScrollByLines(1);
                break;
            case NativeMethods.VK_PRIOR:
                ScrollByLines(-VisibleDataLineCount);
                break;
            case NativeMethods.VK_NEXT:
                ScrollByLines(VisibleDataLineCount);
                break;
            case NativeMethods.VK_HOME:
                if (IsControlKeyDown())
                {
                    Scroll(NativeMethods.SB_TOP, 0);
                    break;
                }

                SetHorizontalOffset(0);
                break;
            case NativeMethods.VK_END:
                if (IsControlKeyDown())
                {
                    Scroll(NativeMethods.SB_BOTTOM, 0);
                    break;
                }

                SetHorizontalOffset(int.MaxValue);
                break;
        }
    }

    private bool TryHandleControlArrowKey(int key, bool extendSelection)
    {
        if (_reader is null ||
            (key != NativeMethods.VK_LEFT &&
            key != NativeMethods.VK_RIGHT &&
            key != NativeMethods.VK_UP &&
            key != NativeMethods.VK_DOWN))
        {
            return false;
        }

        if (IsSearchCellSelectionMode && TryHandleControlCellArrowKey(key, extendSelection))
        {
            return true;
        }

        if (key == NativeMethods.VK_UP || key == NativeMethods.VK_DOWN)
        {
            return TryHandleControlVerticalArrowKey(key, extendSelection, selectedColumn: -1);
        }

        SetHorizontalOffset(key == NativeMethods.VK_LEFT ? 0 : int.MaxValue);
        return true;
    }

    private bool TryHandleControlVerticalArrowKey(int key, bool extendSelection, int selectedColumn)
    {
        if (_reader is null || key is not (NativeMethods.VK_UP or NativeMethods.VK_DOWN))
        {
            return false;
        }

        ViewportRequestKind kind = key == NativeMethods.VK_UP
            ? ViewportRequestKind.JumpHome
            : ViewportRequestKind.JumpEnd;
        PendingSearchKeyboardSelection pendingSelection = key == NativeMethods.VK_UP
            ? PendingSearchKeyboardSelection.FirstVisibleRow
            : PendingSearchKeyboardSelection.LastVisibleRow;

        bool shouldSelectAfterLoad = extendSelection || _onRowActivated is not null || selectedColumn > 0;
        if (!shouldSelectAfterLoad)
        {
            Scroll(key == NativeMethods.VK_UP ? NativeMethods.SB_TOP : NativeMethods.SB_BOTTOM, 0d);
            return true;
        }

        if (extendSelection)
        {
            bool hasAnchor = selectedColumn > 0
                ? EnsureCellSelectionAnchorFromCurrentFocus(selectedColumn)
                : EnsureRowSelectionAnchorFromCurrentFocus();
            if (!hasAnchor)
            {
                return false;
            }
        }

        if (key == NativeMethods.VK_UP)
        {
            SuspendTailFollow();
        }
        else
        {
            _tailFollowSuspended = false;
        }

        bool activatePendingSelection = selectedColumn > 0 || (!extendSelection && _onRowActivated is not null);
        return QueueSearchKeyboardSelectionRequest(
            kind,
            pendingSelection,
            selectedColumn: selectedColumn,
            extendSelection: extendSelection,
            activate: activatePendingSelection);
    }

    private bool TryHandleControlCellArrowKey(int key, bool extendSelection)
    {
        if (_reader is not IColumnViewportReader columnReader ||
            _reader is not ISelectableViewportReader ||
            !_reader.HasContent ||
            _reader.CurrentRows.Count == 0)
        {
            return false;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (columnCount <= 1)
        {
            return false;
        }

        int currentColumn = ResolveKeyboardCellSelectionFocusColumn(columnCount);
        if (key == NativeMethods.VK_UP || key == NativeMethods.VK_DOWN)
        {
            return TryHandleControlVerticalArrowKey(key, extendSelection, currentColumn);
        }

        if (key != NativeMethods.VK_LEFT && key != NativeMethods.VK_RIGHT)
        {
            return false;
        }

        int targetColumn = key == NativeMethods.VK_LEFT ? 1 : columnCount - 1;
        if (extendSelection && !EnsureCellSelectionAnchorFromCurrentFocus(currentColumn))
        {
            return false;
        }

        if (TryGetOffscreenGridCellFocusOrdinal(out long rowOrdinal))
        {
            return QueueSearchKeyboardSelectionAtRowOrdinal(
                rowOrdinal,
                targetColumn,
                extendSelection: extendSelection,
                activate: false);
        }

        int currentRow = ResolveKeyboardCellSelectionFocusRow();
        return extendSelection
            ? ExtendGridCellSelectionTo(currentRow, targetColumn, activate: false)
            : SelectSingleGridCell(currentRow, targetColumn, activate: false);
    }

    private bool TryHandleSearchResultKeyboardNavigation(int key)
    {
        bool control = IsControlKeyDown();
        if (_onRowActivated is null ||
            _reader is null ||
            !_reader.HasContent ||
            _reader.CurrentRows.Count == 0 ||
            _reader is not ISelectableViewportReader)
        {
            return false;
        }

        if (IsSearchCellSelectionMode)
        {
            return TryHandleSearchCellKeyboardNavigation(key);
        }

        if (control)
        {
            return key switch
            {
                NativeMethods.VK_HOME => QueueSearchKeyboardSelectionRequest(
                    ViewportRequestKind.JumpHome,
                    PendingSearchKeyboardSelection.FirstVisibleRow),
                NativeMethods.VK_END => QueueSearchKeyboardSelectionRequest(
                    ViewportRequestKind.JumpEnd,
                    PendingSearchKeyboardSelection.LastVisibleRow),
                _ => false
            };
        }

        if (key == NativeMethods.VK_PRIOR)
        {
            return QueueSearchKeyboardSelectionRequest(
                ViewportRequestKind.ScrollByLines,
                PendingSearchKeyboardSelection.FirstVisibleRow,
                deltaLines: -VisibleDataLineCount);
        }

        if (key == NativeMethods.VK_NEXT)
        {
            return QueueSearchKeyboardSelectionRequest(
                ViewportRequestKind.ScrollByLines,
                PendingSearchKeyboardSelection.LastVisibleRow,
                deltaLines: VisibleDataLineCount);
        }

        if (key != NativeMethods.VK_UP && key != NativeMethods.VK_DOWN)
        {
            return false;
        }

        ClearPendingSearchKeyboardSelection();
        if (!HasSelection || _selectionFocusKey is null)
        {
            return SelectSingleCurrentRow(0, activate: true);
        }

        if (!TryFindCurrentRowIndex(_selectionFocusKey, out int current))
        {
            return SelectSingleCurrentRow(ResolveKeyboardSelectionFocusIndex(), activate: true);
        }

        int direction = key == NativeMethods.VK_UP ? -1 : 1;
        int target = FindAdjacentSelectionRowIndex(current, direction);
        if (target != current)
        {
            return SelectSingleCurrentRow(target, activate: true);
        }

        return QueueSearchKeyboardSelectionScroll(direction);
    }

    private bool TryHandleSearchCellKeyboardNavigation(int key)
    {
        if (_reader is not IColumnViewportReader columnReader)
        {
            return false;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (columnCount <= 1)
        {
            return false;
        }

        bool control = IsControlKeyDown();
        int currentColumn = ResolveKeyboardCellSelectionFocusColumn(columnCount);
        if (control)
        {
            return key switch
            {
                NativeMethods.VK_HOME => QueueSearchKeyboardSelectionRequest(
                    ViewportRequestKind.JumpHome,
                    PendingSearchKeyboardSelection.FirstVisibleRow,
                    selectedColumn: currentColumn),
                NativeMethods.VK_END => QueueSearchKeyboardSelectionRequest(
                    ViewportRequestKind.JumpEnd,
                    PendingSearchKeyboardSelection.LastVisibleRow,
                    selectedColumn: currentColumn),
                _ => false
            };
        }

        if (key == NativeMethods.VK_PRIOR)
        {
            if (TryGetOffscreenGridCellFocusOrdinal(out long rowOrdinal))
            {
                return QueueSearchKeyboardSelectionAtRowOrdinal(
                    Math.Max(0, rowOrdinal - VisibleDataLineCount),
                    currentColumn,
                    extendSelection: false,
                    activate: true);
            }

            return QueueSearchKeyboardSelectionRequest(
                ViewportRequestKind.ScrollByLines,
                PendingSearchKeyboardSelection.FirstVisibleRow,
                deltaLines: -VisibleDataLineCount,
                selectedColumn: currentColumn);
        }

        if (key == NativeMethods.VK_NEXT)
        {
            if (TryGetOffscreenGridCellFocusOrdinal(out long rowOrdinal))
            {
                return QueueSearchKeyboardSelectionAtRowOrdinal(
                    rowOrdinal + VisibleDataLineCount,
                    currentColumn,
                    extendSelection: false,
                    activate: true);
            }

            return QueueSearchKeyboardSelectionRequest(
                ViewportRequestKind.ScrollByLines,
                PendingSearchKeyboardSelection.LastVisibleRow,
                deltaLines: VisibleDataLineCount,
                selectedColumn: currentColumn);
        }

        if (key == NativeMethods.VK_LEFT || key == NativeMethods.VK_RIGHT)
        {
            int nextColumn = key == NativeMethods.VK_LEFT
                ? Math.Max(1, currentColumn - 1)
                : Math.Min(columnCount - 1, currentColumn + 1);
            if (TryGetOffscreenGridCellFocusOrdinal(out long rowOrdinal))
            {
                return QueueSearchKeyboardSelectionAtRowOrdinal(
                    rowOrdinal,
                    nextColumn,
                    extendSelection: false,
                    activate: false);
            }

            int visibleCurrentRow = ResolveKeyboardCellSelectionFocusRow();
            return SelectSingleGridCell(visibleCurrentRow, nextColumn, activate: false);
        }

        if (key != NativeMethods.VK_UP && key != NativeMethods.VK_DOWN)
        {
            return false;
        }

        if (TryGetOffscreenGridCellFocusOrdinal(out long currentRowOrdinal))
        {
            int offscreenDirection = key == NativeMethods.VK_UP ? -1 : 1;
            return QueueSearchKeyboardSelectionAtRowOrdinal(
                Math.Max(0, currentRowOrdinal + offscreenDirection),
                currentColumn,
                extendSelection: false,
                activate: true);
        }

        int currentRow = ResolveKeyboardCellSelectionFocusRow();
        int direction = key == NativeMethods.VK_UP ? -1 : 1;
        int target = FindAdjacentSelectionRowIndex(currentRow, direction);
        if (target != currentRow)
        {
            return SelectSingleGridCell(target, currentColumn, activate: true);
        }

        return QueueSearchKeyboardSelectionScroll(direction, currentColumn);
    }

    private bool SelectSingleCurrentRow(int rowIndex, bool activate)
    {
        if (!TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            return false;
        }

        _selectionAnchorDataIndex = rowIndex;
        _selectionFocusDataIndex = rowIndex;
        _selectionAnchorKey = rowKey;
        _selectionFocusKey = rowKey;

        List<ViewportRowSelectionRange> ranges = new() { new ViewportRowSelectionRange(rowKey, rowKey) };
        ReplaceSelection(selectAll: false, ranges, new HashSet<ViewportRowSelectionKey>());
        if (activate)
        {
            ActivateRowAt(rowIndex);
        }

        return true;
    }

    private bool SelectSingleGridCell(int rowIndex, int columnIndex, bool activate)
    {
        if (_reader is not IColumnViewportReader columnReader ||
            !TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _))
        {
            return false;
        }

        int maxColumn = Math.Max(1, GetGridColumnCount(columnReader) - 1);
        columnIndex = Math.Clamp(columnIndex, 1, maxColumn);
        var key = new GridCellKey(rowKey, columnIndex);
        _cellSelectionAnchorDataIndex = rowIndex;
        _cellSelectionFocusDataIndex = rowIndex;
        _cellSelectionAnchorKey = key;
        _cellSelectionFocusKey = key;
        ReplaceCellSelection(
            selectAllRows: false,
            new List<ViewportRowSelectionRange> { new(rowKey, rowKey) },
            new HashSet<ViewportRowSelectionKey>(),
            new HashSet<int> { columnIndex });
        EnsureGridColumnVisible(columnIndex);
        if (activate)
        {
            ActivateRowAt(rowIndex);
        }

        return true;
    }

    private bool EnsureRowSelectionAnchorFromCurrentFocus()
    {
        if (_reader is null ||
            _reader.CurrentRows.Count == 0 ||
            _reader is not ISelectableViewportReader)
        {
            return false;
        }

        if (_selectionAnchorKey is null)
        {
            int current = ResolveKeyboardSelectionFocusIndex();
            _selectionAnchorDataIndex = current;
            _selectionAnchorKey = GetCurrentRowSelectionKey(current);
            return _selectionAnchorKey is not null;
        }

        TryFindCurrentRowIndex(_selectionAnchorKey, out _selectionAnchorDataIndex);
        return true;
    }

    private bool EnsureCellSelectionAnchorFromCurrentFocus(int currentColumn)
    {
        if (_reader is null ||
            _reader.CurrentRows.Count == 0 ||
            _reader is not IColumnViewportReader columnReader ||
            _reader is not ISelectableViewportReader)
        {
            return false;
        }

        int columnCount = GetGridColumnCount(columnReader);
        currentColumn = Math.Clamp(currentColumn, 1, Math.Max(1, columnCount - 1));
        if (_cellSelectionAnchorKey is null)
        {
            if (_cellSelectionFocusKey is GridCellKey focusKey)
            {
                _cellSelectionAnchorKey = focusKey;
                TryFindCurrentRowIndex(focusKey.RowKey, out _cellSelectionAnchorDataIndex);
                return true;
            }

            int currentRow = ResolveKeyboardCellSelectionFocusRow();
            if (!TryGetCurrentRowSelection(currentRow, out ViewportRowSelectionKey currentRowKey, out _))
            {
                return false;
            }

            _cellSelectionAnchorDataIndex = currentRow;
            _cellSelectionAnchorKey = new GridCellKey(currentRowKey, currentColumn);
            return true;
        }

        TryFindCurrentRowIndex(_cellSelectionAnchorKey.Value.RowKey, out _cellSelectionAnchorDataIndex);
        return true;
    }

    private bool ExtendRowSelectionTo(int target, bool activate)
    {
        if (_reader is null ||
            !_reader.HasContent ||
            _reader.CurrentRows.Count == 0 ||
            _reader is not ISelectableViewportReader)
        {
            return false;
        }

        target = Math.Clamp(target, 0, _reader.CurrentRows.Count - 1);
        if (!EnsureRowSelectionAnchorFromCurrentFocus())
        {
            return false;
        }

        _selectionFocusDataIndex = target;
        _selectionFocusKey = GetCurrentRowSelectionKey(target);
        if (_selectionAnchorKey is null || _selectionFocusKey is null)
        {
            return false;
        }

        bool control = IsControlKeyDown();
        bool nextSelectAll = control && _selectionSelectAllRows;
        List<ViewportRowSelectionRange> nextRanges = control
            ? new List<ViewportRowSelectionRange>(_selectionRanges)
            : new List<ViewportRowSelectionRange>();
        HashSet<ViewportRowSelectionKey> nextExcluded = control
            ? new HashSet<ViewportRowSelectionKey>(_selectionExcludedRows)
            : new HashSet<ViewportRowSelectionKey>();
        AddRangeSelection(nextRanges, nextExcluded, _selectionAnchorKey.Value, _selectionFocusKey.Value);
        ReplaceSelection(nextSelectAll, nextRanges, nextExcluded);
        if (activate)
        {
            ActivateRowAt(target);
        }

        return true;
    }

    private void EnsureGridColumnVisible(int columnIndex)
    {
        if (_reader is not IColumnViewportReader columnReader || columnIndex <= 0)
        {
            return;
        }

        int[] widths = CalculateColumnWidths(columnReader);
        if (columnIndex >= widths.Length)
        {
            return;
        }

        int columnStart = 0;
        for (int i = 1; i < columnIndex; i++)
        {
            columnStart += Math.Max(1, widths[i]);
        }

        int columnEnd = columnStart + Math.Max(1, widths[columnIndex]);
        int visibleColumns = GetHorizontalVisibleColumnCount();
        int visibleStart = _xOffsetChars;
        int visibleEnd = visibleStart + visibleColumns;
        int columnWidth = columnEnd - columnStart;
        if (columnWidth <= visibleColumns)
        {
            if (columnStart < visibleStart)
            {
                SetHorizontalOffset(columnStart);
            }
            else if (columnEnd > visibleEnd)
            {
                SetHorizontalOffset(columnEnd - visibleColumns);
            }

            return;
        }

        if (visibleStart < columnStart)
        {
            SetHorizontalOffset(columnStart);
        }
        else if (visibleEnd > columnEnd)
        {
            SetHorizontalOffset(columnEnd - visibleColumns);
        }
    }

    private bool TryGetOffscreenGridCellFocusOrdinal(out long rowOrdinal)
    {
        rowOrdinal = 0;
        if (_cellSelectionFocusKey is not GridCellKey focusKey ||
            TryFindCurrentRowIndex(focusKey.RowKey, out _) ||
            _reader is not IRowOrdinalViewportReader ordinalReader)
        {
            return false;
        }

        return ordinalReader.TryGetRowOrdinal(focusKey.RowKey, out rowOrdinal);
    }

    private bool QueueSearchKeyboardSelectionScroll(int direction, int selectedColumn = -1)
    {
        PendingSearchKeyboardSelection pendingSelection = direction < 0
            ? PendingSearchKeyboardSelection.FirstVisibleRow
            : PendingSearchKeyboardSelection.LastVisibleRow;
        return QueueSearchKeyboardSelectionRequest(
            ViewportRequestKind.ScrollByLines,
            pendingSelection,
            deltaLines: direction,
            selectedColumn: selectedColumn);
    }

    private bool QueueSearchKeyboardSelectionAtRowOrdinal(
        long rowOrdinal,
        int selectedColumn,
        bool extendSelection,
        bool activate,
        bool synchronizeRow = false)
    {
        ClearPendingSearchKeyboardSelection();
        long requestId = QueueViewportRequest(
            ViewportRequestKind.LoadAtRowOrdinal,
            requestedRowOrdinal: rowOrdinal,
            visibleLines: VisibleDataLineCount);
        if (requestId == 0L)
        {
            return false;
        }

        _pendingSearchKeyboardSelection = synchronizeRow
            ? PendingSearchKeyboardSelection.SynchronizedRowOrdinal
            : PendingSearchKeyboardSelection.SpecificRowOrdinal;
        _pendingSearchKeyboardSelectionRequestId = requestId;
        _pendingSearchKeyboardSelectionColumn = selectedColumn;
        _pendingSearchKeyboardSelectionRowOrdinal = rowOrdinal;
        _pendingSearchKeyboardSelectionExtend = extendSelection;
        _pendingSearchKeyboardSelectionActivate = activate;
        return true;
    }

    private bool QueueSearchKeyboardSelectionRequest(
        ViewportRequestKind kind,
        PendingSearchKeyboardSelection pendingSelection,
        int deltaLines = 0,
        int selectedColumn = -1,
        bool extendSelection = false,
        bool activate = true)
    {
        ClearPendingSearchKeyboardSelection();
        long requestId = QueueViewportRequest(
            kind,
            deltaLines: deltaLines,
            visibleLines: VisibleDataLineCount);
        if (requestId == 0L)
        {
            return false;
        }

        _pendingSearchKeyboardSelection = pendingSelection;
        _pendingSearchKeyboardSelectionRequestId = requestId;
        _pendingSearchKeyboardSelectionColumn = selectedColumn;
        _pendingSearchKeyboardSelectionExtend = extendSelection;
        _pendingSearchKeyboardSelectionActivate = activate;
        return true;
    }

    private void ApplyPendingSearchKeyboardSelection(long requestId)
    {
        if (_pendingSearchKeyboardSelection == PendingSearchKeyboardSelection.None ||
            _pendingSearchKeyboardSelectionRequestId != requestId)
        {
            return;
        }

        PendingSearchKeyboardSelection pendingSelection = _pendingSearchKeyboardSelection;
        int pendingColumn = _pendingSearchKeyboardSelectionColumn;
        long pendingRowOrdinal = _pendingSearchKeyboardSelectionRowOrdinal;
        bool extendSelection = _pendingSearchKeyboardSelectionExtend;
        bool activate = _pendingSearchKeyboardSelectionActivate;
        ClearPendingSearchKeyboardSelection();
        if (_reader is null || _reader.CurrentRows.Count == 0)
        {
            return;
        }

        int rowIndex;
        if (pendingSelection is PendingSearchKeyboardSelection.SpecificRowOrdinal or PendingSearchKeyboardSelection.SynchronizedRowOrdinal)
        {
            rowIndex = 0;
            if (_reader is IFileOffsetViewportReader offsetReader)
            {
                long relativeRow = pendingRowOrdinal - offsetReader.TopRowOrdinal;
                rowIndex = (int)Math.Clamp(relativeRow, 0, _reader.CurrentRows.Count - 1);
            }
        }
        else
        {
            rowIndex = pendingSelection == PendingSearchKeyboardSelection.FirstVisibleRow
                ? 0
                : _reader.CurrentRows.Count - 1;
        }

        if (pendingSelection == PendingSearchKeyboardSelection.SynchronizedRowOrdinal)
        {
            SelectSingleCurrentRow(rowIndex, activate: false);
            return;
        }

        if (IsSearchCellSelectionMode && _reader is IColumnViewportReader columnReader)
        {
            int column = pendingColumn > 0
                ? Math.Clamp(pendingColumn, 1, Math.Max(1, GetGridColumnCount(columnReader) - 1))
                : ResolveKeyboardCellSelectionFocusColumn(GetGridColumnCount(columnReader));
            if (extendSelection)
            {
                ExtendGridCellSelectionTo(rowIndex, column, activate);
            }
            else
            {
                SelectSingleGridCell(rowIndex, column, activate);
            }

            return;
        }

        if (extendSelection)
        {
            ExtendRowSelectionTo(rowIndex, activate);
        }
        else
        {
            SelectSingleCurrentRow(rowIndex, activate);
        }
    }

    private void ClearPendingSearchKeyboardSelection()
    {
        _pendingSearchKeyboardSelection = PendingSearchKeyboardSelection.None;
        _pendingSearchKeyboardSelectionRequestId = 0L;
        _pendingSearchKeyboardSelectionColumn = -1;
        _pendingSearchKeyboardSelectionRowOrdinal = -1;
        _pendingSearchKeyboardSelectionExtend = false;
        _pendingSearchKeyboardSelectionActivate = true;
    }

    private bool TryHandleShiftSelectionKey(int key)
    {
        if (_reader is null || !_reader.HasContent || _reader.CurrentRows.Count == 0 || _reader is not ISelectableViewportReader)
        {
            return false;
        }

        int rowCount = _reader.CurrentRows.Count;
        int current = ResolveKeyboardSelectionFocusIndex();
        int target = key switch
        {
            NativeMethods.VK_UP => FindAdjacentSelectionRowIndex(current, -1),
            NativeMethods.VK_DOWN => FindAdjacentSelectionRowIndex(current, 1),
            NativeMethods.VK_PRIOR => 0,
            NativeMethods.VK_NEXT => rowCount - 1,
            NativeMethods.VK_HOME => 0,
            NativeMethods.VK_END => rowCount - 1,
            _ => -1
        };

        if (target < 0)
        {
            return false;
        }

        return ExtendRowSelectionTo(target, activate: false);
    }

    private bool TryHandleShiftCellSelectionKey(int key)
    {
        if (_reader is null ||
            !_reader.HasContent ||
            _reader.CurrentRows.Count == 0 ||
            _reader is not IColumnViewportReader columnReader ||
            _reader is not ISelectableViewportReader)
        {
            return false;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (columnCount <= 1)
        {
            return false;
        }

        int currentColumn = ResolveKeyboardCellSelectionFocusColumn(columnCount);
        if (TryGetOffscreenGridCellFocusOrdinal(out long currentRowOrdinal))
        {
            long targetRowOrdinal = currentRowOrdinal;
            int offscreenTargetColumn = currentColumn;
            bool activate = false;
            switch (key)
            {
                case NativeMethods.VK_LEFT:
                    offscreenTargetColumn = Math.Max(1, currentColumn - 1);
                    break;
                case NativeMethods.VK_RIGHT:
                    offscreenTargetColumn = Math.Min(columnCount - 1, currentColumn + 1);
                    break;
                case NativeMethods.VK_UP:
                    targetRowOrdinal = Math.Max(0, currentRowOrdinal - 1);
                    activate = true;
                    break;
                case NativeMethods.VK_DOWN:
                    targetRowOrdinal = currentRowOrdinal + 1;
                    activate = true;
                    break;
                default:
                    return false;
            }

            return QueueSearchKeyboardSelectionAtRowOrdinal(
                targetRowOrdinal,
                offscreenTargetColumn,
                extendSelection: true,
                activate: activate);
        }

        int currentRow = ResolveKeyboardCellSelectionFocusRow();
        int targetRow = currentRow;
        int targetColumn = currentColumn;
        switch (key)
        {
            case NativeMethods.VK_LEFT:
                targetColumn = Math.Max(1, currentColumn - 1);
                break;
            case NativeMethods.VK_RIGHT:
                targetColumn = Math.Min(columnCount - 1, currentColumn + 1);
                break;
            case NativeMethods.VK_UP:
                targetRow = FindAdjacentSelectionRowIndex(currentRow, -1);
                break;
            case NativeMethods.VK_DOWN:
                targetRow = FindAdjacentSelectionRowIndex(currentRow, 1);
                break;
            default:
                return false;
        }

        if (!TryGetCurrentRowSelection(targetRow, out ViewportRowSelectionKey targetRowKey, out _))
        {
            return false;
        }

        if (_cellSelectionAnchorKey is null)
        {
            if (!TryGetCurrentRowSelection(currentRow, out ViewportRowSelectionKey currentRowKey, out _))
            {
                return false;
            }

            _cellSelectionAnchorDataIndex = currentRow;
            _cellSelectionAnchorKey = new GridCellKey(currentRowKey, currentColumn);
        }
        else
        {
            TryFindCurrentRowIndex(_cellSelectionAnchorKey.Value.RowKey, out _cellSelectionAnchorDataIndex);
        }

        return ExtendGridCellSelectionTo(targetRow, targetColumn, activate: targetRow != currentRow);
    }

    private bool ExtendGridCellSelectionTo(int targetRow, int targetColumn, bool activate)
    {
        if (_reader is null ||
            !_reader.HasContent ||
            _reader.CurrentRows.Count == 0 ||
            _reader is not IColumnViewportReader columnReader ||
            _reader is not ISelectableViewportReader ||
            !TryGetCurrentRowSelection(targetRow, out ViewportRowSelectionKey targetRowKey, out _))
        {
            return false;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (columnCount <= 1)
        {
            return false;
        }

        int currentColumn = ResolveKeyboardCellSelectionFocusColumn(columnCount);
        targetColumn = Math.Clamp(targetColumn, 1, columnCount - 1);
        if (_cellSelectionAnchorKey is null)
        {
            if (_cellSelectionFocusKey is GridCellKey focusKey)
            {
                _cellSelectionAnchorKey = focusKey;
                TryFindCurrentRowIndex(focusKey.RowKey, out _cellSelectionAnchorDataIndex);
            }
            else
            {
                int currentRow = ResolveKeyboardCellSelectionFocusRow();
                if (TryGetCurrentRowSelection(currentRow, out ViewportRowSelectionKey currentRowKey, out _))
                {
                    _cellSelectionAnchorDataIndex = currentRow;
                    _cellSelectionAnchorKey = new GridCellKey(currentRowKey, currentColumn);
                }
                else
                {
                    _cellSelectionAnchorDataIndex = targetRow;
                    _cellSelectionAnchorKey = new GridCellKey(targetRowKey, targetColumn);
                }
            }
        }
        else
        {
            TryFindCurrentRowIndex(_cellSelectionAnchorKey.Value.RowKey, out _cellSelectionAnchorDataIndex);
        }

        var targetKey = new GridCellKey(targetRowKey, targetColumn);
        _cellSelectionFocusDataIndex = targetRow;
        _cellSelectionFocusKey = targetKey;
        bool control = IsControlKeyDown();
        bool nextSelectAllRows = control && _selectionSelectAllRows;
        List<ViewportRowSelectionRange> nextRanges = control
            ? new List<ViewportRowSelectionRange>(_selectionRanges)
            : new List<ViewportRowSelectionRange>();
        HashSet<ViewportRowSelectionKey> nextExcluded = control
            ? new HashSet<ViewportRowSelectionKey>(_selectionExcludedRows)
            : new HashSet<ViewportRowSelectionKey>();
        HashSet<int> nextColumns = control
            ? new HashSet<int>(_cellSelectionColumns)
            : new HashSet<int>();
        AddCellAxisRange(nextRanges, nextExcluded, nextColumns, _cellSelectionAnchorKey.Value, targetKey);
        ReplaceCellSelection(nextSelectAllRows, nextRanges, nextExcluded, nextColumns);
        EnsureGridColumnVisible(targetColumn);

        if (activate)
        {
            ActivateRowAt(targetRow);
        }

        return true;
    }

    private int ResolveKeyboardSelectionFocusIndex()
    {
        if (_reader is null || _reader.CurrentRows.Count == 0)
        {
            return 0;
        }

        if (_selectionFocusKey is not null &&
            _selectionFocusDataIndex >= 0 &&
            _selectionFocusDataIndex < _reader.CurrentRows.Count &&
            GetCurrentRowSelectionKey(_selectionFocusDataIndex) == _selectionFocusKey)
        {
            return _selectionFocusDataIndex;
        }

        if (TryFindCurrentRowIndex(_selectionFocusKey, out int currentFocusIndex))
        {
            return currentFocusIndex;
        }

        if (_selectionFocusKey is not null && _reader is ISelectableViewportReader selectableReader)
        {
            IReadOnlyList<ViewportRowSelectionKey> keys = selectableReader.CurrentRowSelectionKeys;
            if (keys.Count > 0)
            {
                if (_selectionFocusKey.Value.CompareTo(keys[0]) < 0)
                {
                    return 0;
                }

                if (_selectionFocusKey.Value.CompareTo(keys[keys.Count - 1]) > 0)
                {
                    return keys.Count - 1;
                }
            }
        }

        return _selectionFocusDataIndex >= 0
            ? Math.Clamp(_selectionFocusDataIndex, 0, _reader.CurrentRows.Count - 1)
            : 0;
    }

    private int ResolveKeyboardCellSelectionFocusRow()
    {
        if (_reader is null || _reader.CurrentRows.Count == 0)
        {
            return 0;
        }

        if (_cellSelectionFocusKey is GridCellKey focusKey)
        {
            if (_cellSelectionFocusDataIndex >= 0 &&
                _cellSelectionFocusDataIndex < _reader.CurrentRows.Count &&
                GetCurrentRowSelectionKey(_cellSelectionFocusDataIndex) == focusKey.RowKey)
            {
                return _cellSelectionFocusDataIndex;
            }

            if (TryFindCurrentRowIndex(focusKey.RowKey, out int currentFocusIndex))
            {
                return currentFocusIndex;
            }

            if (_reader is ISelectableViewportReader selectableReader)
            {
                IReadOnlyList<ViewportRowSelectionKey> keys = selectableReader.CurrentRowSelectionKeys;
                if (keys.Count > 0)
                {
                    if (focusKey.RowKey.CompareTo(keys[0]) < 0)
                    {
                        return 0;
                    }

                    if (focusKey.RowKey.CompareTo(keys[keys.Count - 1]) > 0)
                    {
                        return keys.Count - 1;
                    }
                }
            }
        }

        return _cellSelectionFocusDataIndex >= 0
            ? Math.Clamp(_cellSelectionFocusDataIndex, 0, _reader.CurrentRows.Count - 1)
            : 0;
    }

    private int ResolveKeyboardCellSelectionFocusColumn(int columnCount)
    {
        int maxColumn = Math.Max(1, columnCount - 1);
        if (_cellSelectionFocusKey is GridCellKey focusKey)
        {
            return Math.Clamp(focusKey.ColumnIndex, 1, maxColumn);
        }

        return 1;
    }

    private int FindAdjacentSelectionRowIndex(int current, int direction)
    {
        if (_reader is not ISelectableViewportReader selectableReader)
        {
            return Math.Clamp(current + direction, 0, _reader?.CurrentRows.Count - 1 ?? 0);
        }

        IReadOnlyList<ViewportRowSelectionKey> keys = selectableReader.CurrentRowSelectionKeys;
        if (keys.Count == 0)
        {
            return 0;
        }

        current = Math.Clamp(current, 0, keys.Count - 1);
        ViewportRowSelectionKey currentKey = keys[current];
        for (int i = current + direction; i >= 0 && i < keys.Count; i += direction)
        {
            if (!keys[i].Equals(currentKey))
            {
                return i;
            }
        }

        return current;
    }

    private void SelectAllRows()
    {
        if (_reader is not ISelectableViewportReader || _reader is null || !_reader.HasContent)
        {
            return;
        }

        ClearTextSelection(invalidate: false);
        _selectionSelectAllRows = true;
        _selectionRanges.Clear();
        _selectionExcludedRows.Clear();
        if (_reader.CurrentRows.Count > 0)
        {
            _selectionAnchorDataIndex = 0;
            _selectionFocusDataIndex = _reader.CurrentRows.Count - 1;
            _selectionAnchorKey = GetCurrentRowSelectionKey(_selectionAnchorDataIndex);
            _selectionFocusKey = GetCurrentRowSelectionKey(_selectionFocusDataIndex);
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void SelectAllGridCells()
    {
        if (_reader is not IColumnViewportReader columnReader ||
            _reader is not ISelectableViewportReader ||
            !_reader.HasContent ||
            GetGridColumnCount(columnReader) <= 1)
        {
            return;
        }

        _selectionSelectAllRows = true;
        _selectionRanges.Clear();
        _selectionExcludedRows.Clear();
        _selectionAnchorDataIndex = -1;
        _selectionFocusDataIndex = -1;
        _selectionAnchorKey = null;
        _selectionFocusKey = null;
        _cellSelectionColumns.Clear();
        int columnCount = GetGridColumnCount(columnReader);
        for (int column = 1; column < columnCount; column++)
        {
            _cellSelectionColumns.Add(column);
        }

        if (_reader.CurrentRows.Count > 0 && TryGetCurrentRowSelection(0, out ViewportRowSelectionKey rowKey, out _))
        {
            _cellSelectionAnchorDataIndex = 0;
            _cellSelectionFocusDataIndex = _reader.CurrentRows.Count - 1;
            _cellSelectionAnchorKey = new GridCellKey(rowKey, 1);
            ViewportRowSelectionKey focusRowKey = GetCurrentRowSelectionKey(_cellSelectionFocusDataIndex) ?? rowKey;
            _cellSelectionFocusKey = new GridCellKey(focusRowKey, columnCount - 1);
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void CopySelectionToClipboard()
    {
        if (TryCopyTextSelectionToClipboard())
        {
            return;
        }

        if (IsSearchCellSelectionMode)
        {
            CopyCellSelectionToClipboard();
            return;
        }

        if (_reader is not ISelectableViewportReader selectableReader || !HasSelection)
        {
            return;
        }

        ViewportRowSelectionKey[] excluded = new ViewportRowSelectionKey[_selectionExcludedRows.Count];
        _selectionExcludedRows.CopyTo(excluded);
        IReadOnlyList<ViewportSelectedRow> selected;
        try
        {
            selected = selectableReader.ReadSelectedRows(_selectionSelectAllRows, _selectionRanges, excluded);
        }
        catch (FilteredLineStaleException)
        {
            _onStale?.Invoke(this);
            return;
        }

        List<string> selectedRows = new(selected.Count);
        foreach (ViewportSelectedRow row in selected)
        {
            selectedRows.Add(CreateClipboardRow(row));
        }

        if (selectedRows.Count == 0)
        {
            return;
        }

        SetClipboardText(string.Join("\r\n", selectedRows));
    }

    private bool TryCopyTextSelectionToClipboard()
    {
        if (!HasTextSelectionRange)
        {
            return false;
        }

        if (_textSelectionContext is ViewportTextSelectionContext context)
        {
            int globalStart = Math.Clamp(Math.Min(_textSelectionAnchorChar, _textSelectionFocusChar), 0, context.Text.Length);
            int globalEnd = Math.Clamp(Math.Max(_textSelectionAnchorChar, _textSelectionFocusChar), 0, context.Text.Length);
            return globalEnd > globalStart &&
                SetClipboardText(NormalizeClipboardCell(context.Text.Substring(globalStart, globalEnd - globalStart)));
        }

        if (_textSelectionRowKey is null ||
            !TryFindCurrentRowIndex(_textSelectionRowKey, out int rowIndex) ||
            _reader is null ||
            rowIndex < 0 ||
            rowIndex >= _reader.CurrentRows.Count)
        {
            return false;
        }

        string text;
        if (_textSelectionColumnIndex > 0)
        {
            if (!TryGetGridCellTextAndRect(
                    rowIndex,
                    _textSelectionColumnIndex,
                    out text,
                    out _,
                    out _,
                    out _))
            {
                return false;
            }
        }
        else
        {
            text = _reader.CurrentRows[rowIndex];
        }

        int start = Math.Clamp(Math.Min(_textSelectionAnchorChar, _textSelectionFocusChar), 0, text.Length);
        int end = Math.Clamp(Math.Max(_textSelectionAnchorChar, _textSelectionFocusChar), 0, text.Length);
        if (end <= start)
        {
            return false;
        }

        return SetClipboardText(NormalizeClipboardCell(text.Substring(start, end - start)));
    }

    private void CopyCellSelectionToClipboard()
    {
        if (_reader is not IColumnViewportReader columnReader ||
            _reader is not ISelectableViewportReader selectableReader ||
            (!_selectionSelectAllRows && _selectionRanges.Count == 0) ||
            _cellSelectionColumns.Count == 0)
        {
            return;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (columnCount <= 1)
        {
            return;
        }

        List<int> selectedColumns = new();
        foreach (int column in _cellSelectionColumns)
        {
            if (column > 0 && column < columnCount)
            {
                selectedColumns.Add(column);
            }
        }

        if (selectedColumns.Count == 0)
        {
            return;
        }

        selectedColumns.Sort();
        ViewportRowSelectionKey[] excluded = new ViewportRowSelectionKey[_selectionExcludedRows.Count];
        _selectionExcludedRows.CopyTo(excluded);
        IReadOnlyList<ViewportSelectedRow> selected;
        try
        {
            selected = selectableReader.ReadSelectedRows(_selectionSelectAllRows, _selectionRanges, excluded);
        }
        catch (FilteredLineStaleException)
        {
            _onStale?.Invoke(this);
            return;
        }

        List<string[]> selectedCells = new(selected.Count);
        for (int rowIndex = 0; rowIndex < selected.Count; rowIndex++)
        {
            ViewportSelectedRow row = selected[rowIndex];
            string[] cells = new string[selectedColumns.Count];
            for (int i = 0; i < selectedColumns.Count; i++)
            {
                int column = selectedColumns[i];
                string value = string.Empty;
                if (IsGridCellSelectedForClipboard(row.Key, column))
                {
                    int copyColumn = column - 1;
                    if (row.Cells is not null && copyColumn >= 0 && copyColumn < row.Cells.Count)
                    {
                        value = row.Cells[copyColumn];
                    }
                    else if (column == 1)
                    {
                        value = row.Text;
                    }
                }

                cells[i] = NormalizeClipboardCell(value);
            }

            selectedCells.Add(cells);
        }

        string? clipboardText = BuildTrimmedTsv(selectedCells);
        if (clipboardText is null)
        {
            return;
        }

        SetClipboardText(clipboardText);
    }

    private static string? BuildTrimmedTsv(IReadOnlyList<string[]> rows)
    {
        List<int> rowIndexes = new();
        List<int> columnIndexes = new();
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            string[] row = rows[rowIndex];
            bool rowHasValue = false;
            for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                if (row[columnIndex].Length == 0)
                {
                    continue;
                }

                rowHasValue = true;
                if (!columnIndexes.Contains(columnIndex))
                {
                    columnIndexes.Add(columnIndex);
                }
            }

            if (rowHasValue)
            {
                rowIndexes.Add(rowIndex);
            }
        }

        if (rowIndexes.Count == 0 || columnIndexes.Count == 0)
        {
            return null;
        }

        columnIndexes.Sort();
        List<string> lines = new(rowIndexes.Count);
        for (int row = 0; row < rowIndexes.Count; row++)
        {
            string[] source = rows[rowIndexes[row]];
            string[] cells = new string[columnIndexes.Count];
            for (int column = 0; column < columnIndexes.Count; column++)
            {
                int columnIndex = columnIndexes[column];
                cells[column] = columnIndex < source.Length ? source[columnIndex] : string.Empty;
            }

            lines.Add(string.Join("\t", cells));
        }

        return string.Join("\r\n", lines);
    }

    private bool IsGridCellSelectedForClipboard(ViewportRowSelectionKey rowKey, int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return false;
        }

        return IsGridCellSelected(new GridCellKey(rowKey, columnIndex));
    }

    private string CreateClipboardRow(ViewportSelectedRow row)
    {
        if (_reader is not IColumnViewportReader || row.Cells is null || row.Cells.Count == 0)
        {
            return NormalizeClipboardCell(row.Text);
        }

        string[] cells = new string[row.Cells.Count];
        for (int i = 0; i < row.Cells.Count; i++)
        {
            cells[i] = NormalizeClipboardCell(row.Cells[i]);
        }

        return string.Join("\t", cells);
    }

    private static string NormalizeClipboardCell(string value) =>
        value.Replace('\t', ' ');

    private bool SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text) || !NativeMethods.OpenClipboard(_hwnd))
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

    private static bool IsControlKeyDown() =>
        (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & unchecked((short)0x8000)) != 0;

    private static bool IsShiftKeyDown() =>
        (NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & unchecked((short)0x8000)) != 0;

    private void OnPaint()
    {
        NativeMethods.PAINTSTRUCT ps;
        IntPtr hdc = NativeMethods.BeginPaint(_hwnd, out ps);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.RECT clientRect;
        NativeMethods.GetClientRect(_hwnd, out clientRect);
        int width = GetRectWidth(clientRect);
        int height = GetRectHeight(clientRect);
        int visibleNonEmptyLines = width > 0 && height > 0
            ? PaintBuffered(hdc, clientRect, width, height)
            : PaintViewportToDc(hdc, clientRect);
        NativeMethods.EndPaint(_hwnd, ref ps);

        bool useful = _reader is not null && (!_reader.HasContent || visibleNonEmptyLines > 0);
        if (useful && !_usefulPaintNotified && _onUsefulPaint is not null)
        {
            NativeMethods.DwmFlush();
            _onUsefulPaint(this);
            _usefulPaintNotified = true;
        }
    }

    private int PaintBuffered(IntPtr targetDc, NativeMethods.RECT clientRect, int width, int height)
    {
        IntPtr memoryDc = NativeMethods.CreateCompatibleDC(targetDc);
        if (memoryDc == IntPtr.Zero)
        {
            return PaintViewportToDc(targetDc, clientRect);
        }

        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;
        try
        {
            bitmap = NativeMethods.CreateCompatibleBitmap(targetDc, width, height);
            if (bitmap == IntPtr.Zero)
            {
                return PaintViewportToDc(targetDc, clientRect);
            }

            previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
            int visibleNonEmptyLines = PaintViewportToDc(memoryDc, clientRect);
            NativeMethods.BitBlt(targetDc, 0, 0, width, height, memoryDc, 0, 0, NativeMethods.SRCCOPY);
            return visibleNonEmptyLines;
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previousBitmap);
            }

            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            NativeMethods.DeleteDC(memoryDc);
        }
    }

    private int PaintViewportToDc(IntPtr hdc, NativeMethods.RECT clientRect)
    {
        IntPtr oldFont = NativeMethods.SelectObject(hdc, _font);
        try
        {
            NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
            NativeMethods.SetTextColor(hdc, NativeMethods.RGB(0, 0, 0));
            NativeMethods.FillRect(hdc, ref clientRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW));
            return PaintContent(hdc);
        }
        finally
        {
            NativeMethods.SelectObject(hdc, oldFont);
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

        if (_reader is IColumnViewportReader columnReader)
        {
            return PaintColumnContent(hdc, columnReader);
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        IReadOnlyList<string> rows = _reader.CurrentRows;
        List<NativeMethods.RECT>? textSelectionIndicatorRects = null;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            string row = rows[rowIndex];
            string displayRow = NormalizeDisplayText(row);
            bool selected = IsDataRowSelected(rowIndex);
            HighlightStyle? highlight = !selected && TryGetHighlightStyle(rowIndex, displayRow, out HighlightStyle matchedStyle)
                ? matchedStyle
                : null;
            if (selected)
            {
                NativeMethods.RECT rowRect = new()
                {
                    left = 0,
                    top = y,
                    right = clientRect.right,
                    bottom = y + _lineHeight
                };
                if (_hasFocus)
                {
                    NativeMethods.FillRect(hdc, ref rowRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT));
                    NativeMethods.SetTextColor(hdc, NativeMethods.GetSysColor(NativeMethods.COLOR_HIGHLIGHTTEXT));
                }
                else
                {
                    NativeMethods.FillRect(hdc, ref rowRect, GetInactiveSelectionBrush());
                    NativeMethods.SetTextColor(hdc, NativeMethods.GetSysColor(NativeMethods.COLOR_WINDOWTEXT));
                }
            }
            else if (highlight is HighlightStyle style)
            {
                NativeMethods.RECT rowRect = new()
                {
                    left = 0,
                    top = y,
                    right = clientRect.right,
                    bottom = y + _lineHeight
                };
                NativeMethods.FillRect(hdc, ref rowRect, GetHighlightBrush(style.BackgroundColor));
                NativeMethods.SetTextColor(hdc, style.ForegroundColor);
            }
            else
            {
                NativeMethods.SetTextColor(hdc, NativeMethods.RGB(0, 0, 0));
            }

            string visibleText = SliceVisibleText(displayRow);
            if (visibleText.Length > 0)
            {
                IntPtr rowFont = GetHighlightFont(highlight, selected);
                IntPtr previousFont = rowFont != _font
                    ? NativeMethods.SelectObject(hdc, rowFont)
                    : IntPtr.Zero;
                NativeMethods.TextOutW(hdc, x, y, visibleText, visibleText.Length);
                if (previousFont != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(hdc, previousFont);
                }

                visibleNonEmptyLines++;
            }

            PaintTextSelection(hdc, rowIndex, displayRow, y, clientRect, ref textSelectionIndicatorRects);

            y += _lineHeight;
            if (y >= clientRect.bottom)
            {
                break;
            }
        }

        PaintTextSelectionIndicatorBlocks(hdc, textSelectionIndicatorRects);
        return visibleNonEmptyLines;
    }

    private int PaintColumnContent(IntPtr hdc, IColumnViewportReader reader)
    {
        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        IReadOnlyList<string> headers = GetGridHeaders(reader);
        int[] widths = CalculateColumnWidths(reader);

        List<NativeMethods.RECT>? textSelectionIndicatorRects = null;
        PaintGridRow(hdc, clientRect, headers, widths, y: 0, isHeader: true, isRowSelected: false, dataRowIndex: -1, highlight: null, textSelectionIndicatorRects: ref textSelectionIndicatorRects);

        int y = _lineHeight;
        int visibleNonEmptyLines = 0;
        int dataRowIndex = 0;
        IReadOnlyList<string> displayRows = reader.CurrentRows;
        if (reader.ColumnHeaders.Count > 0)
        {
            foreach (IReadOnlyList<string> row in reader.CurrentCells)
            {
                HighlightStyle? highlight = TryGetGridRowHighlight(displayRows, dataRowIndex);
                PaintGridRow(hdc, clientRect, row, widths, y, isHeader: false, IsDataRowSelected(dataRowIndex), dataRowIndex, highlight, textSelectionIndicatorRects: ref textSelectionIndicatorRects);
                visibleNonEmptyLines++;
                dataRowIndex++;

                y += _lineHeight;
                if (y >= clientRect.bottom)
                {
                    break;
                }
            }
        }
        else
        {
            foreach (string row in displayRows)
            {
                HighlightStyle? highlight = TryGetHighlightStyle(dataRowIndex, NormalizeDisplayText(row), out HighlightStyle matchedStyle)
                    ? matchedStyle
                    : null;
                PaintGridRow(hdc, clientRect, new[] { row }, widths, y, isHeader: false, IsDataRowSelected(dataRowIndex), dataRowIndex, highlight, textSelectionIndicatorRects: ref textSelectionIndicatorRects);
                visibleNonEmptyLines++;
                dataRowIndex++;

                y += _lineHeight;
                if (y >= clientRect.bottom)
                {
                    break;
                }
            }
        }

        PaintTextSelectionIndicatorBlocks(hdc, textSelectionIndicatorRects);
        return visibleNonEmptyLines;
    }

    private void PaintGridRow(IntPtr hdc, NativeMethods.RECT clientRect, IReadOnlyList<string> cells, IReadOnlyList<int> widths, int y, bool isHeader, bool isRowSelected, int dataRowIndex, HighlightStyle? highlight, ref List<NativeMethods.RECT>? textSelectionIndicatorRects)
    {
        if (widths.Count == 0)
        {
            return;
        }

        int firstWidthChars = Math.Max(1, widths[0]);
        NativeMethods.RECT firstCellRect = new()
        {
            left = 0,
            top = y,
            right = firstWidthChars * _charWidth,
            bottom = y + _lineHeight
        };

        if (Intersects(firstCellRect, clientRect))
        {
            string value = cells.Count > 0 ? cells[0] : string.Empty;
            bool isFirstCellSelected = isHeader
                ? IsAllGridCellsSelected()
                : isRowSelected || IsSearchRowAxisSelected(dataRowIndex);
            PaintGridCell(hdc, firstCellRect, Intersect(firstCellRect, clientRect), value, firstWidthChars, isHeader: true, isSelected: isFirstCellSelected, highlight);
        }

        NativeMethods.RECT scrollRect = clientRect;
        scrollRect.left = Math.Max(scrollRect.left, firstCellRect.right);
        int startChars = 0;
        for (int i = 1; i < widths.Count; i++)
        {
            int widthChars = Math.Max(1, widths[i]);
            NativeMethods.RECT cellRect = new()
            {
                left = firstCellRect.right + ((startChars - _xOffsetChars) * _charWidth),
                top = y,
                right = firstCellRect.right + ((startChars + widthChars - _xOffsetChars) * _charWidth),
                bottom = y + _lineHeight
            };

            if (Intersects(cellRect, scrollRect))
            {
                string value = i < cells.Count ? cells[i] : string.Empty;
                bool isCellSelected = isRowSelected ||
                    (isHeader ? IsSearchColumnAxisSelected(i) : IsGridCellSelected(dataRowIndex, i));
                PaintGridCell(hdc, cellRect, Intersect(cellRect, scrollRect), value, widthChars, isHeader, isCellSelected, highlight);
                if (!isHeader)
                {
                    PaintGridTextSelection(hdc, dataRowIndex, i, value, cellRect, Intersect(cellRect, scrollRect), widthChars, ref textSelectionIndicatorRects);
                }
            }

            startChars += widthChars;
        }
    }

    private void PaintGridCell(IntPtr hdc, NativeMethods.RECT cellRect, NativeMethods.RECT visibleRect, string text, int widthChars, bool isHeader, bool isSelected, HighlightStyle? highlight)
    {
        if (visibleRect.right <= visibleRect.left || visibleRect.bottom <= visibleRect.top)
        {
            return;
        }

        IntPtr backgroundBrush = isSelected
            ? (_hasFocus ? NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT) : GetInactiveSelectionBrush())
            : highlight is HighlightStyle style
                ? GetHighlightBrush(style.BackgroundColor)
                : NativeMethods.GetSysColorBrush(isHeader ? NativeMethods.COLOR_3DFACE : NativeMethods.COLOR_WINDOW);
        NativeMethods.FillRect(hdc, ref visibleRect, backgroundBrush);
        int textColor = isSelected && _hasFocus
            ? NativeMethods.GetSysColor(NativeMethods.COLOR_HIGHLIGHTTEXT)
            : !isSelected && highlight is HighlightStyle highlightedStyle
                ? highlightedStyle.ForegroundColor
                : NativeMethods.GetSysColor(NativeMethods.COLOR_WINDOWTEXT);
        NativeMethods.SetTextColor(hdc, textColor);

        NativeMethods.RECT textRect = cellRect;
        textRect.left += GridCellPaddingPx;
        textRect.right -= GridCellPaddingPx;
        if (textRect.right > textRect.left)
        {
            int textCapacityChars = GetGridTextCapacityChars(widthChars);
            string visibleText = FitGridCellText(text, textCapacityChars);
            if (visibleText.Length > 0)
            {
                IntPtr textFont = GetHighlightFont(highlight, isSelected);
                IntPtr previousFont = textFont != _font
                    ? NativeMethods.SelectObject(hdc, textFont)
                    : IntPtr.Zero;
                int savedDc = NativeMethods.SaveDC(hdc);
                if (savedDc != 0)
                {
                    NativeMethods.IntersectClipRect(hdc, visibleRect.left, visibleRect.top, visibleRect.right, visibleRect.bottom);
                }

                NativeMethods.DrawTextW(
                    hdc,
                    visibleText,
                    visibleText.Length,
                    ref textRect,
                    NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);

                if (savedDc != 0)
                {
                    NativeMethods.RestoreDC(hdc, savedDc);
                }

                if (previousFont != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(hdc, previousFont);
                }
            }
        }

        NativeMethods.FrameRect(hdc, ref visibleRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DLIGHT));
        PaintGridDividers(hdc, visibleRect);
    }

    private void PaintGridDividers(IntPtr hdc, NativeMethods.RECT visibleRect)
    {
        if (visibleRect.right - visibleRect.left > GridDividerThicknessPx)
        {
            NativeMethods.RECT rightDivider = visibleRect;
            rightDivider.left = Math.Max(rightDivider.left, rightDivider.right - GridDividerThicknessPx);
            NativeMethods.FillRect(hdc, ref rightDivider, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DLIGHT));
        }

        if (visibleRect.bottom - visibleRect.top > 1)
        {
            NativeMethods.RECT bottomDivider = visibleRect;
            bottomDivider.top = Math.Max(bottomDivider.top, bottomDivider.bottom - 1);
            NativeMethods.FillRect(hdc, ref bottomDivider, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DLIGHT));
        }
    }

    private int GetGridTextCapacityChars(int widthChars)
    {
        int availableWidthPx = (Math.Max(1, widthChars) * _charWidth) - (GridCellPaddingPx * 2);
        return Math.Max(0, availableWidthPx / _charWidth);
    }

    private static string FitGridCellText(string value, int widthChars)
    {
        value = NormalizeDisplayText(value);
        widthChars = Math.Max(0, widthChars);
        if (widthChars == 0 || value.Length == 0)
        {
            return string.Empty;
        }

        if (value.Length <= widthChars)
        {
            return value;
        }

        if (widthChars == 1)
        {
            return ">";
        }

        return value.Substring(0, widthChars - 1) + ">";
    }

    private static string NormalizeDisplayText(string value) =>
        value.IndexOf('\t') >= 0 ? value.Replace('\t', ' ') : value;

    private static bool Intersects(NativeMethods.RECT a, NativeMethods.RECT b)
    {
        return a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;
    }

    private static NativeMethods.RECT Intersect(NativeMethods.RECT a, NativeMethods.RECT b)
    {
        return new NativeMethods.RECT
        {
            left = Math.Max(a.left, b.left),
            top = Math.Max(a.top, b.top),
            right = Math.Min(a.right, b.right),
            bottom = Math.Min(a.bottom, b.bottom)
        };
    }

    private int[] CalculateColumnWidths(IColumnViewportReader reader)
    {
        int[] widths = CalculateDefaultColumnWidths(reader);
        if (_manualColumnWidths is null || _manualColumnWidths.Length != widths.Length)
        {
            return widths;
        }

        for (int i = 0; i < widths.Length; i++)
        {
            if (_manualColumnWidths[i] > 0)
            {
                widths[i] = i == 0 && widths.Length > 1
                    ? Math.Max(widths[i], _manualColumnWidths[i])
                    : Math.Max(GetMinimumColumnWidth(reader, i), _manualColumnWidths[i]);
            }
        }

        return widths;
    }

    private int[] CalculateDefaultColumnWidths(IColumnViewportReader reader)
    {
        int columnCount = GetGridColumnCount(reader);
        int[] widths = new int[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            int defaultWidthPx = columnCount == 1 || i == 1
                ? DefaultTextColumnWidthPx
                : DefaultGroupColumnWidthPx;
            widths[i] = i == 0 && columnCount > 1
                ? GetDefaultLineNumberColumnWidthChars(reader)
                : Math.Max(GetMinimumColumnWidth(reader, i), PixelsToColumnWidthChars(defaultWidthPx));
        }

        return widths;
    }

    private int GetDefaultLineNumberColumnWidthChars(IColumnViewportReader reader)
    {
        int maxLength = GetGridHeaders(reader)[0].Length;
        if (reader is ILineNumberColumnViewportReader lineNumberReader && lineNumberReader.MaxLineNumber > 0)
        {
            maxLength = Math.Max(maxLength, lineNumberReader.MaxLineNumber.ToString().Length);
        }
        else
        {
            foreach (IReadOnlyList<string> row in reader.CurrentCells)
            {
                if (row.Count > 0)
                {
                    maxLength = Math.Max(maxLength, row[0].Length);
                }
            }
        }

        return Math.Max(GetMinimumColumnWidth(reader, 0), GetGridColumnWidthCharsForTextLength(maxLength));
    }

    private int[] CalculateAutoColumnWidths(IColumnViewportReader reader)
    {
        IReadOnlyList<string> headers = GetGridHeaders(reader);
        int columnCount = headers.Count;
        int[] widths = new int[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            widths[i] = NormalizeDisplayText(headers[i]).Length;
        }

        if (reader.ColumnHeaders.Count > 0)
        {
            foreach (IReadOnlyList<string> row in reader.CurrentCells)
            {
                int count = Math.Min(columnCount, row.Count);
                for (int i = 0; i < count; i++)
                {
                    widths[i] = Math.Max(widths[i], NormalizeDisplayText(row[i]).Length);
                }
            }
        }
        else
        {
            foreach (string row in reader.CurrentRows)
            {
                widths[0] = Math.Max(widths[0], NormalizeDisplayText(row).Length);
            }
        }

        for (int i = 0; i < widths.Length; i++)
        {
            widths[i] = GetGridColumnWidthCharsForTextLength(widths[i]);
        }

        return widths;
    }

    private static int GetMinimumColumnWidth(IColumnViewportReader reader, int columnIndex)
    {
        IReadOnlyList<string> headers = GetGridHeaders(reader);
        int contentMin = columnIndex <= 1 ? headers[columnIndex].Length : 1;

        return Math.Max(1, contentMin);
    }

    private int GetGridColumnWidthCharsForTextLength(int textLength)
    {
        int requiredWidthPx = (Math.Max(0, textLength) * _charWidth) + (GridCellPaddingPx * 2);
        return PixelsToColumnWidthChars(requiredWidthPx);
    }

    private int PixelsToColumnWidthChars(int pixels)
    {
        return Math.Max(1, (int)Math.Ceiling(pixels / (double)_charWidth));
    }

    private static IReadOnlyList<string> GetGridHeaders(IColumnViewportReader reader)
    {
        return reader.ColumnHeaders.Count > 0 ? reader.ColumnHeaders : s_textOnlyGridHeaders;
    }

    private static int GetGridColumnCount(IColumnViewportReader reader)
    {
        return Math.Max(1, reader.ColumnHeaders.Count);
    }

    private void ScrollByLines(int deltaLines)
    {
        if (_reader is null)
        {
            return;
        }

        ClearPendingSearchKeyboardSelection();
        if (deltaLines < 0)
        {
            SuspendTailFollow();
        }

        QueueViewportRequest(ViewportRequestKind.ScrollByLines, deltaLines, visibleLines: VisibleDataLineCount);
    }

    private void Scroll(int command, double trackPercentage)
    {
        if (_reader is null)
        {
            return;
        }

        ClearPendingSearchKeyboardSelection();
        int visible = VisibleDataLineCount;
        switch (command)
        {
            case NativeMethods.SB_LINEUP:
                SuspendTailFollow();
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, -1, visibleLines: visible);
                break;
            case NativeMethods.SB_LINEDOWN:
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, 1, visibleLines: visible);
                break;
            case NativeMethods.SB_PAGEUP:
                SuspendTailFollow();
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, -visible, visibleLines: visible);
                break;
            case NativeMethods.SB_PAGEDOWN:
                QueueViewportRequest(ViewportRequestKind.ScrollByLines, visible, visibleLines: visible);
                break;
            case NativeMethods.SB_TOP:
                SuspendTailFollow();
                QueueViewportRequest(ViewportRequestKind.JumpHome, visibleLines: visible);
                break;
            case NativeMethods.SB_BOTTOM:
                _tailFollowSuspended = false;
                QueueViewportRequest(ViewportRequestKind.JumpEnd, visibleLines: visible);
                break;
            case NativeMethods.SB_THUMBPOSITION:
            case NativeMethods.SB_THUMBTRACK:
                if (trackPercentage < 100d)
                {
                    SuspendTailFollow();
                }
                else
                {
                    _tailFollowSuspended = false;
                }

                QueueViewportRequest(ViewportRequestKind.LoadAtPercentage, requestedPercentage: trackPercentage, visibleLines: visible);
                break;
        }
    }

    private void SetHorizontalOffset(int requestedOffset)
    {
        ClearPendingSearchKeyboardSelection();
        int maxOffset = Math.Max(0, GetContentWidthChars() - GetHorizontalVisibleColumnCount());
        int nextOffset = Math.Clamp(requestedOffset, 0, maxOffset);
        if (nextOffset == _xOffsetChars)
        {
            return;
        }

        ClearTextSelection(invalidate: false);
        _xOffsetChars = nextOffset;
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private int HitTestColumnResize(int x, int y)
    {
        if (y < 0 || y >= _lineHeight || _reader is not IColumnViewportReader columnReader)
        {
            return -1;
        }

        int[] widths = CalculateColumnWidths(columnReader);
        if (widths.Length == 0)
        {
            return -1;
        }

        int fixedWidthPx = Math.Max(1, widths[0]) * _charWidth;
        if (Math.Abs(x - fixedWidthPx) <= ColumnResizeHitSlopPx)
        {
            return 0;
        }

        int boundaryChars = 0;
        for (int i = 1; i < widths.Length; i++)
        {
            boundaryChars += widths[i];
            int boundaryX = fixedWidthPx + ((boundaryChars - _xOffsetChars) * _charWidth);
            if (Math.Abs(x - boundaryX) <= ColumnResizeHitSlopPx)
            {
                return i;
            }
        }

        return -1;
    }

    private void BeginColumnResize(int columnIndex, int x)
    {
        if (_reader is not IColumnViewportReader columnReader)
        {
            return;
        }

        int[] widths = CalculateColumnWidths(columnReader);
        if (columnIndex < 0 || columnIndex >= widths.Length)
        {
            return;
        }

        _isColumnResizing = true;
        _resizingColumnIndex = columnIndex;
        _hoverResizeColumnIndex = columnIndex;
        _resizeStartX = x;
        _resizeStartWidth = widths[columnIndex];
        NativeMethods.SetCapture(_hwnd);
        SetResizeCursor();
    }

    private void UpdateColumnResize(int x)
    {
        if (!_isColumnResizing || _reader is not IColumnViewportReader columnReader)
        {
            return;
        }

        int columnCount = GetGridColumnCount(columnReader);
        if (_resizingColumnIndex < 0 || _resizingColumnIndex >= columnCount)
        {
            return;
        }

        int deltaChars = (int)Math.Round((x - _resizeStartX) / (double)_charWidth);
        int nextWidth = Math.Max(GetMinimumColumnWidth(columnReader, _resizingColumnIndex), _resizeStartWidth + deltaChars);
        int[] manualWidths = EnsureManualColumnWidths(columnCount);
        if (manualWidths[_resizingColumnIndex] == nextWidth)
        {
            return;
        }

        manualWidths[_resizingColumnIndex] = nextWidth;
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private void EndColumnResize()
    {
        _isColumnResizing = false;
        _resizingColumnIndex = -1;
        _resizeStartX = 0;
        _resizeStartWidth = 0;
        NativeMethods.ReleaseCapture();
    }

    private void AutoFitColumn(int columnIndex)
    {
        if (_reader is not IColumnViewportReader columnReader)
        {
            return;
        }

        int[] autoWidths = CalculateAutoColumnWidths(columnReader);
        if (columnIndex < 0 || columnIndex >= autoWidths.Length)
        {
            return;
        }

        int[] manualWidths = EnsureManualColumnWidths(autoWidths.Length);
        manualWidths[columnIndex] = Math.Max(GetMinimumColumnWidth(columnReader, columnIndex), autoWidths[columnIndex]);
        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private int[] EnsureManualColumnWidths(int columnCount)
    {
        if (_manualColumnWidths is null || _manualColumnWidths.Length != columnCount)
        {
            _manualColumnWidths = new int[columnCount];
        }

        return _manualColumnWidths;
    }

    private void ResetColumnResizeState(bool clearManualWidths)
    {
        if (_isColumnResizing)
        {
            NativeMethods.ReleaseCapture();
        }

        _isColumnResizing = false;
        _hoverResizeColumnIndex = -1;
        _resizingColumnIndex = -1;
        _resizeStartX = 0;
        _resizeStartWidth = 0;
        if (clearManualWidths)
        {
            _manualColumnWidths = null;
        }
    }

    private bool CanPreserveColumnState(IViewportReader reader, bool preserveColumnWidths)
    {
        if (!preserveColumnWidths || reader is not IColumnViewportReader nextColumnReader)
        {
            return false;
        }

        int nextColumnCount = GetGridColumnCount(nextColumnReader);
        if (_reader is IColumnViewportReader currentColumnReader &&
            GetGridColumnCount(currentColumnReader) != nextColumnCount)
        {
            return false;
        }

        if (_manualColumnWidths is not null && _manualColumnWidths.Length != nextColumnCount)
        {
            return false;
        }

        if (_manualColumnWidths is null && _reader is not IColumnViewportReader)
        {
            return false;
        }

        return !_isColumnResizing || (_resizingColumnIndex >= 0 && _resizingColumnIndex < nextColumnCount);
    }

    private void SetResizeCursor()
    {
        NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZEWE));
    }

    private void SetVerticalResizeCursor()
    {
        NativeMethods.SetCursor(NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_SIZENS));
    }

    private void PaintGridTextSelection(
        IntPtr hdc,
        int rowIndex,
        int columnIndex,
        string text,
        NativeMethods.RECT cellRect,
        NativeMethods.RECT visibleRect,
        int widthChars,
        ref List<NativeMethods.RECT>? textSelectionIndicatorRects)
    {
        if (_textSelectionColumnIndex != columnIndex ||
            visibleRect.right <= visibleRect.left ||
            visibleRect.bottom <= visibleRect.top)
        {
            return;
        }

        int selectionStart;
        int selectionEnd;
        bool showIndicator;
        if (_textSelectionContext is not null)
        {
            if (columnIndex != 1 ||
                !TryGetLogicalTextSelectionPaintRange(rowIndex, out selectionStart, out selectionEnd, out showIndicator))
            {
                return;
            }
        }
        else
        {
            if (_textSelectionRowKey is null ||
                !TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _) ||
                rowKey != _textSelectionRowKey.Value)
            {
                return;
            }

            selectionStart = Math.Clamp(Math.Min(_textSelectionAnchorChar, _textSelectionFocusChar), 0, text.Length);
            selectionEnd = Math.Clamp(Math.Max(_textSelectionAnchorChar, _textSelectionFocusChar), 0, text.Length);
            showIndicator = true;
        }

        if (showIndicator)
        {
            AddTextSelectionIndicatorRect(ref textSelectionIndicatorRects, visibleRect);
        }

        if (!HasTextSelectionRange)
        {
            return;
        }

        int textCapacityChars = GetGridTextCapacityChars(widthChars);
        int visibleEnd = Math.Min(text.Length, textCapacityChars);
        int paintStart = Math.Clamp(selectionStart, 0, visibleEnd);
        int paintEnd = Math.Clamp(selectionEnd, 0, visibleEnd);
        if (paintEnd <= paintStart)
        {
            return;
        }

        NativeMethods.RECT selectedRect = new()
        {
            left = cellRect.left + GridCellPaddingPx + (paintStart * _charWidth),
            top = cellRect.top,
            right = cellRect.left + GridCellPaddingPx + (paintEnd * _charWidth),
            bottom = cellRect.bottom
        };
        selectedRect = Intersect(selectedRect, visibleRect);
        if (selectedRect.right <= selectedRect.left || selectedRect.bottom <= selectedRect.top)
        {
            return;
        }

        IntPtr brush = _hasFocus
            ? NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT)
            : GetInactiveSelectionBrush();
        NativeMethods.FillRect(hdc, ref selectedRect, brush);
        NativeMethods.SetTextColor(
            hdc,
            NativeMethods.GetSysColor(_hasFocus ? NativeMethods.COLOR_HIGHLIGHTTEXT : NativeMethods.COLOR_WINDOWTEXT));

        string displayText = NormalizeDisplayText(text);
        string selectedText = displayText.Substring(paintStart, paintEnd - paintStart);
        if (selectedText.Length > 0)
        {
            int savedDc = NativeMethods.SaveDC(hdc);
            if (savedDc != 0)
            {
                NativeMethods.IntersectClipRect(hdc, visibleRect.left, visibleRect.top, visibleRect.right, visibleRect.bottom);
            }

            NativeMethods.TextOutW(
                hdc,
                cellRect.left + GridCellPaddingPx + (paintStart * _charWidth),
                cellRect.top,
                selectedText,
                selectedText.Length);

            if (savedDc != 0)
            {
                NativeMethods.RestoreDC(hdc, savedDc);
            }
        }

        NativeMethods.SetTextColor(hdc, NativeMethods.RGB(0, 0, 0));
        NativeMethods.FrameRect(hdc, ref visibleRect, NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DLIGHT));
        PaintGridDividers(hdc, visibleRect);
    }

    private void PaintTextSelection(
        IntPtr hdc,
        int rowIndex,
        string row,
        int y,
        NativeMethods.RECT clientRect,
        ref List<NativeMethods.RECT>? textSelectionIndicatorRects)
    {
        if (_textSelectionColumnIndex >= 0)
        {
            return;
        }


        int selectionStart;
        int selectionEnd;
        bool showIndicator;
        if (_textSelectionContext is not null)
        {
            if (!TryGetLogicalTextSelectionPaintRange(rowIndex, out selectionStart, out selectionEnd, out showIndicator))
            {
                return;
            }
        }
        else
        {
            if (_textSelectionRowKey is null ||
                !TryGetCurrentRowSelection(rowIndex, out ViewportRowSelectionKey rowKey, out _) ||
                rowKey != _textSelectionRowKey.Value)
            {
                return;
            }

            selectionStart = Math.Clamp(Math.Min(_textSelectionAnchorChar, _textSelectionFocusChar), 0, row.Length);
            selectionEnd = Math.Clamp(Math.Max(_textSelectionAnchorChar, _textSelectionFocusChar), 0, row.Length);
            showIndicator = true;
        }

        NativeMethods.RECT rowRect = new()
        {
            left = clientRect.left,
            top = y,
            right = clientRect.right,
            bottom = y + _lineHeight
        };
        if (showIndicator)
        {
            AddTextSelectionIndicatorRect(ref textSelectionIndicatorRects, Intersect(rowRect, clientRect));
        }

        if (!HasTextSelectionRange)
        {
            return;
        }

        int visibleStart = Math.Clamp(_xOffsetChars, 0, row.Length);
        int visibleEnd = Math.Min(row.Length, visibleStart + _visibleColumnCount);
        int paintStart = Math.Max(selectionStart, visibleStart);
        int paintEnd = Math.Min(selectionEnd, visibleEnd);
        if (paintEnd <= paintStart)
        {
            return;
        }

        NativeMethods.RECT selectedRect = new()
        {
            left = (paintStart - visibleStart) * _charWidth,
            top = y,
            right = (paintEnd - visibleStart) * _charWidth,
            bottom = y + _lineHeight
        };
        selectedRect = Intersect(selectedRect, clientRect);
        if (selectedRect.right <= selectedRect.left || selectedRect.bottom <= selectedRect.top)
        {
            return;
        }

        IntPtr brush = _hasFocus
            ? NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT)
            : GetInactiveSelectionBrush();
        NativeMethods.FillRect(hdc, ref selectedRect, brush);
        NativeMethods.SetTextColor(
            hdc,
            NativeMethods.GetSysColor(_hasFocus ? NativeMethods.COLOR_HIGHLIGHTTEXT : NativeMethods.COLOR_WINDOWTEXT));

        string selectedText = row.Substring(paintStart, paintEnd - paintStart);
        if (selectedText.Length > 0)
        {
            NativeMethods.TextOutW(hdc, (paintStart - visibleStart) * _charWidth, y, selectedText, selectedText.Length);
        }

        NativeMethods.SetTextColor(hdc, NativeMethods.RGB(0, 0, 0));
    }

    private static void AddTextSelectionIndicatorRect(ref List<NativeMethods.RECT>? rects, NativeMethods.RECT bounds)
    {
        if (bounds.right <= bounds.left || bounds.bottom <= bounds.top)
        {
            return;
        }

        rects ??= new List<NativeMethods.RECT>();
        if (rects.Count > 0)
        {
            NativeMethods.RECT previous = rects[^1];
            if (previous.left == bounds.left &&
                previous.right == bounds.right &&
                bounds.top <= previous.bottom)
            {
                previous.bottom = Math.Max(previous.bottom, bounds.bottom);
                rects[^1] = previous;
                return;
            }
        }

        rects.Add(bounds);
    }

    private void PaintTextSelectionIndicatorBlocks(IntPtr hdc, List<NativeMethods.RECT>? rects)
    {
        if (rects is null || rects.Count == 0)
        {
            return;
        }

        IntPtr brush = _hasFocus
            ? NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT)
            : NativeMethods.GetSysColorBrush(NativeMethods.COLOR_BTNSHADOW);
        for (int i = 0; i < rects.Count; i++)
        {
            NativeMethods.RECT rect = rects[i];
            NativeMethods.FrameRect(hdc, ref rect, brush);
        }
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

        int contentWidthChars = GetContentWidthChars();
        int horizontalVisibleColumns = GetHorizontalVisibleColumnCount();
        int horizontalMax = Math.Max(0, contentWidthChars - horizontalVisibleColumns);
        _xOffsetChars = Math.Clamp(_xOffsetChars, 0, horizontalMax);
        NativeMethods.SCROLLINFO horizontal = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 0,
            nMax = contentWidthChars,
            nPage = (uint)Math.Max(1, Math.Min(contentWidthChars, horizontalVisibleColumns)),
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

        int visibleDataLines = VisibleDataLineCount;
        long verticalPage = Math.Max(1, Math.Min(ScrollRange, ((long)visibleDataLines * ScrollRange) / VerticalScrollVirtualRows));
        int verticalTrackMax = GetScrollTrackMax(0, ScrollRange, (uint)verticalPage);
        NativeMethods.SCROLLINFO vertical = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 0,
            nMax = ScrollRange,
            nPage = (uint)verticalPage,
            nPos = ScrollPercentageToTrackPosition(_reader.ScrollPercentage, 0, verticalTrackMax)
        };
        NativeMethods.SetScrollInfo(_hwnd, NativeMethods.SB_VERT, ref vertical, true);
    }

    private static int GetScrollTrackMax(int minimum, int maximum, uint page)
    {
        long pageAdjustment = Math.Max(0L, (long)page - 1L);
        long trackMax = (long)maximum - pageAdjustment;
        return (int)Math.Max(minimum, Math.Min(maximum, trackMax));
    }

    private static double ScrollTrackPositionToPercentage(int trackPos, NativeMethods.SCROLLINFO scrollInfo)
    {
        int trackMax = GetScrollTrackMax(scrollInfo.nMin, scrollInfo.nMax, scrollInfo.nPage);
        int clamped = Math.Clamp(trackPos, scrollInfo.nMin, trackMax);
        if (trackMax <= scrollInfo.nMin)
        {
            return 0d;
        }

        if (clamped >= trackMax)
        {
            return 100d;
        }

        return ((clamped - scrollInfo.nMin) * 100d) / (trackMax - scrollInfo.nMin);
    }

    private static int ScrollPercentageToTrackPosition(double percentage, int minimum, int trackMax)
    {
        double clamped = Math.Clamp(percentage, 0d, 100d);
        if (trackMax <= minimum)
        {
            return minimum;
        }

        if (clamped >= 100d)
        {
            return trackMax;
        }

        return minimum + (int)Math.Round((clamped / 100d) * (trackMax - minimum));
    }

    private int GetContentWidthChars()
    {
        if (_reader is IColumnViewportReader columnReader)
        {
            int[] widths = CalculateColumnWidths(columnReader);
            int total = 0;
            int start = widths.Length > 1 ? 1 : 0;
            for (int i = start; i < widths.Length; i++)
            {
                total += Math.Max(0, widths[i]);
            }

            return Math.Max(1, total);
        }

        return ProjectedViewport.VisibleSegmentChars;
    }

    private int GetHorizontalVisibleColumnCount()
    {
        if (_reader is IColumnViewportReader columnReader)
        {
            int[] widths = CalculateColumnWidths(columnReader);
            if (widths.Length > 1)
            {
                return Math.Max(1, _visibleColumnCount - Math.Max(1, widths[0]));
            }
        }

        return _visibleColumnCount;
    }

    private bool IsFixedLineNumberColumnHit(int x)
    {
        if (_reader is not IColumnViewportReader columnReader)
        {
            return false;
        }

        int[] widths = CalculateColumnWidths(columnReader);
        return widths.Length > 1 && x >= 0 && x < Math.Max(1, widths[0]) * _charWidth;
    }

    private static int GetHeaderLineCount(IViewportReader? reader)
    {
        return reader is IColumnViewportReader ? 1 : 0;
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
            self.DisposeWindowResources();
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
            case NativeMethods.WM_SETFOCUS:
                self.OnFocusChanged(hasFocus: true);
                return IntPtr.Zero;
            case NativeMethods.WM_KILLFOCUS:
                self.OnFocusChanged(hasFocus: false);
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
            case NativeMethods.WM_MOUSEMOVE:
                self.OnMouseMove(lParam);
                return IntPtr.Zero;
            case NativeMethods.WM_KEYDOWN:
                self.OnKeyDown((int)wParam);
                return IntPtr.Zero;
            case NativeMethods.WM_LBUTTONDOWN:
                self.OnLButtonDown(lParam);
                return IntPtr.Zero;
            case NativeMethods.WM_LBUTTONUP:
                self.OnLButtonUp();
                return IntPtr.Zero;
            case NativeMethods.WM_LBUTTONDBLCLK:
                self.OnLButtonDoubleClick(lParam);
                return IntPtr.Zero;
            case NativeMethods.WM_SETCURSOR:
                if (self.OnSetCursor())
                {
                    return new IntPtr(1);
                }

                return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
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
