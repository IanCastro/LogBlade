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
    RefreshTailIfAtEnd
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
}

internal sealed class ViewportPaneWindow : IDisposable
{
    private enum PendingSearchKeyboardSelection
    {
        None,
        FirstVisibleRow,
        LastVisibleRow
    }

    private const int WheelLinesPerNotch = 3;
    private const int ScrollRange = 1_000_000;
    private const int VerticalScrollVirtualRows = 4000;
    private const int ColumnResizeHitSlopPx = 4;
    private const int DefaultTextColumnWidthPx = 900;
    private const int DefaultGroupColumnWidthPx = 200;
    private const int GridCellPaddingPx = 4;
    private const int GridDividerThicknessPx = 1;
    private const int InactiveSelectionColor = 0x00FAF0E8;
    private const string WindowClassName = "LogViewerViewportPaneWindow";

    private static readonly object s_registrationSync = new();
    private static bool s_registered;
    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static readonly string[] s_textOnlyGridHeaders = ["Text"];

    private readonly IntPtr _font;
    private readonly int _lineHeight;
    private readonly int _charWidth;
    private readonly Action<ViewportPaneWindow>? _onUsefulPaint;
    private readonly Action<ViewportPaneWindow>? _onStale;
    private readonly Action<ViewportPaneWindow, long>? _onRowActivated;

    private IntPtr _hwnd;
    private IntPtr _inactiveSelectionBrush;
    private GCHandle _selfHandle;
    private int _visibleColumnCount = 1;
    private int _visibleLineCount = 1;
    private int _xOffsetChars;
    private bool _isVisible = true;
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
    private bool _selectionDragMoved;
    private bool _selectionMouseStartedWithControl;
    private bool _selectionMouseStartedWithShift;
    private PendingSearchKeyboardSelection _pendingSearchKeyboardSelection;
    private long _pendingSearchKeyboardSelectionRequestId;
    private int _selectionMouseDownX;
    private int _selectionMouseDownY;
    private int _selectionAnchorDataIndex = -1;
    private int _selectionFocusDataIndex = -1;
    private ViewportRowSelectionKey? _selectionAnchorKey;
    private ViewportRowSelectionKey? _selectionFocusKey;

    public ViewportPaneWindow(IntPtr font, int lineHeight, int charWidth, Action<ViewportPaneWindow>? onUsefulPaint = null, Action<ViewportPaneWindow>? onStale = null, Action<ViewportPaneWindow, long>? onRowActivated = null)
    {
        _font = font;
        _lineHeight = Math.Max(1, lineHeight);
        _charWidth = Math.Max(1, charWidth);
        _inactiveSelectionBrush = NativeMethods.CreateSolidBrush(InactiveSelectionColor);
        _onUsefulPaint = onUsefulPaint;
        _onStale = onStale;
        _onRowActivated = onRowActivated;
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

    public void SetStatus(string statusText, bool disposeReader = true, bool preserveColumnWidths = false)
    {
        ResetColumnResizeState(clearManualWidths: !preserveColumnWidths);
        ClearSelection(invalidate: false);
        if (disposeReader)
        {
            _reader?.Dispose();
            _reader = null;
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
        _statusText = string.Empty;
        ResetViewportAsyncState();
        UpdateScrollBar();
        if (VisibleDataLineCount != Math.Max(1, preloadedVisibleLines))
        {
            QueueViewportRequest(
                ViewportRequestKind.LoadAtPercentage,
                requestedPercentage: _reader.ScrollPercentage,
                visibleLines: VisibleDataLineCount);
        }

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

    public void QueueTailRefreshIfAtEnd()
    {
        if (_reader is not VisualRowReader visualReader || _hwnd == IntPtr.Zero)
        {
            _tailRefreshPending = false;
            _fileSizeRefreshPending = false;
            return;
        }

        if (!visualReader.IsAtKnownEnd || _tailFollowSuspended)
        {
            _tailRefreshPending = false;
            if (_viewportWorkerRunning || _pendingViewportRequest is not null)
            {
                _fileSizeRefreshPending = true;
                return;
            }

            RefreshKnownFileSize();
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

    public void Dispose()
    {
        ResetColumnResizeState(clearManualWidths: true);
        ClearSelection(invalidate: false);
        _reader?.Dispose();
        _reader = null;
        if (_inactiveSelectionBrush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_inactiveSelectionBrush);
            _inactiveSelectionBrush = IntPtr.Zero;
        }
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

    private void QueuePendingTailRefreshIfReady()
    {
        if (!_tailRefreshPending || _viewportWorkerRunning || _pendingViewportRequest is not null)
        {
            return;
        }

        if (_reader is not VisualRowReader)
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
        RefreshKnownFileSize();
    }

    private void RefreshKnownFileSize()
    {
        if (_reader is not VisualRowReader visualReader)
        {
            _fileSizeRefreshPending = false;
            return;
        }

        if (!visualReader.RefreshFileSize(out long previousSize, out long currentSize, out bool wasAtEnd))
        {
            return;
        }

        UpdateScrollBar();
        NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        if (currentSize < previousSize)
        {
            _tailRefreshPending = false;
            _fileSizeRefreshPending = false;
            _tailFollowSuspended = false;
            QueueViewportRequest(
                ViewportRequestKind.LoadAtPercentage,
                requestedPercentage: 0d,
                visibleLines: VisibleDataLineCount);
            return;
        }

        if (wasAtEnd && !_tailFollowSuspended)
        {
            _tailRefreshPending = true;
        }
    }

    private void SuspendTailFollow()
    {
        _tailRefreshPending = false;
        _tailFollowSuspended = true;
    }

    private void ResumeTailFollowIfAtEnd()
    {
        if (_reader is VisualRowReader { IsAtKnownEnd: true })
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
                        if (workerReader is FilteredVisualRowReader filteredReader)
                        {
                            filteredReader.ReadFromRowOrdinal(request.RequestedRowOrdinal, request.VisibleLines);
                        }
                        else
                        {
                            workerReader.ReadFromPercentage(request.RequestedPercentage, request.VisibleLines);
                        }

                        break;
                    case ViewportRequestKind.LoadAtOffset:
                        if (workerReader is VisualRowReader offsetReader)
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
                        if (workerReader is VisualRowReader visualReader)
                        {
                            visualReader.RefreshTail(request.VisibleLines);
                        }

                        break;
                }

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

        if (result.RequestId == _latestViewportRequestId && result.Success && result.Reader is not null)
        {
            _reader?.Dispose();
            _reader = result.Reader;
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

            if (result.IsStale)
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
            if (_reader is FilteredVisualRowReader filteredReader)
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
                        requestedRowOrdinal: filteredReader.TopRowOrdinal,
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

        int maxOffset = Math.Max(0, GetContentWidthChars() - _visibleColumnCount);
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

        if (_isSelectingRows)
        {
            UpdateRowSelection(x, y);
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
        Focus();
        ClearPendingSearchKeyboardSelection();
        int x = NativeMethods.LowWord(lParam);
        int y = NativeMethods.HighWord(lParam);
        int resizeColumn = HitTestColumnResize(x, y);
        if (resizeColumn >= 0)
        {
            BeginColumnResize(resizeColumn, x);
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

        if (_isSelectingRows)
        {
            EndRowSelection();
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
        if (_isColumnResizing || _hoverResizeColumnIndex >= 0)
        {
            SetResizeCursor();
            return true;
        }

        return false;
    }

    private bool BeginRowSelection(int x, int y)
    {
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

    private void ReplaceSelection(bool selectAll, List<ViewportRowSelectionRange> ranges, HashSet<ViewportRowSelectionKey> excluded)
    {
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
        bool hadSelection = _isSelectingRows || HasSelection;
        if (_isSelectingRows)
        {
            NativeMethods.ReleaseCapture();
        }

        _isSelectingRows = false;
        _selectionDragMoved = false;
        _selectionMouseStartedWithControl = false;
        _selectionMouseStartedWithShift = false;
        _selectionDragBaseRanges = null;
        _selectionDragBaseExcludedRows = null;
        _selectionDragBaseSelectAllRows = false;
        _selectionMouseDownX = 0;
        _selectionMouseDownY = 0;
        _selectionAnchorDataIndex = -1;
        _selectionFocusDataIndex = -1;
        _selectionAnchorKey = null;
        _selectionFocusKey = null;
        _selectionSelectAllRows = false;
        _selectionRanges.Clear();
        _selectionExcludedRows.Clear();

        if (hadSelection && invalidate && _hwnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private bool IsDataRowSelected(int dataRowIndex) =>
        TryGetCurrentRowSelection(dataRowIndex, out ViewportRowSelectionKey key, out _) &&
        IsSelectionKeySelected(key);

    private bool IsSelectionKeySelected(ViewportRowSelectionKey key) =>
        IsSelectionKeySelected(_selectionSelectAllRows, _selectionRanges, _selectionExcludedRows, key);

    private IntPtr GetInactiveSelectionBrush() =>
        _inactiveSelectionBrush != IntPtr.Zero
            ? _inactiveSelectionBrush
            : NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DFACE);

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

    private bool HasSelection => _selectionSelectAllRows || _selectionRanges.Count > 0;

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

    private void ActivateRowAt(int dataRowIndex)
    {
        if (_onRowActivated is null || _reader is not IFileOffsetViewportReader offsetReader)
        {
            return;
        }

        if (dataRowIndex < 0 || dataRowIndex >= _reader.CurrentRows.Count)
        {
            return;
        }

        long rowOrdinal = offsetReader.TopRowOrdinal + dataRowIndex;
        try
        {
            if (offsetReader.TryGetRowStartOffset(rowOrdinal, out long startOffset))
            {
                _onRowActivated(this, startOffset);
            }
        }
        catch (FilteredLineStaleException)
        {
            _onStale?.Invoke(this);
        }
    }

    private void OnKeyDown(int key)
    {
        if (key == NativeMethods.VK_A && IsControlKeyDown())
        {
            SelectAllRows();
            return;
        }

        if (key == NativeMethods.VK_C && IsControlKeyDown())
        {
            CopySelectionToClipboard();
            return;
        }

        if (IsShiftKeyDown() && TryHandleShiftSelectionKey(key))
        {
            return;
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

    private bool QueueSearchKeyboardSelectionScroll(int direction)
    {
        PendingSearchKeyboardSelection pendingSelection = direction < 0
            ? PendingSearchKeyboardSelection.FirstVisibleRow
            : PendingSearchKeyboardSelection.LastVisibleRow;
        return QueueSearchKeyboardSelectionRequest(
            ViewportRequestKind.ScrollByLines,
            pendingSelection,
            deltaLines: direction);
    }

    private bool QueueSearchKeyboardSelectionRequest(ViewportRequestKind kind, PendingSearchKeyboardSelection pendingSelection, int deltaLines = 0)
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
        ClearPendingSearchKeyboardSelection();
        if (_reader is null || _reader.CurrentRows.Count == 0)
        {
            return;
        }

        int rowIndex = pendingSelection == PendingSearchKeyboardSelection.FirstVisibleRow
            ? 0
            : _reader.CurrentRows.Count - 1;
        SelectSingleCurrentRow(rowIndex, activate: true);
    }

    private void ClearPendingSearchKeyboardSelection()
    {
        _pendingSearchKeyboardSelection = PendingSearchKeyboardSelection.None;
        _pendingSearchKeyboardSelectionRequestId = 0L;
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

        if (_selectionAnchorKey is null)
        {
            _selectionAnchorDataIndex = current;
            _selectionAnchorKey = GetCurrentRowSelectionKey(current);
        }
        else
        {
            TryFindCurrentRowIndex(_selectionAnchorKey, out _selectionAnchorDataIndex);
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

    private void CopySelectionToClipboard()
    {
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
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

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

        if (_reader is IColumnViewportReader columnReader)
        {
            return PaintColumnContent(hdc, columnReader);
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        IReadOnlyList<string> rows = _reader.CurrentRows;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            string row = rows[rowIndex];
            bool selected = IsDataRowSelected(rowIndex);
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
            else
            {
                NativeMethods.SetTextColor(hdc, NativeMethods.RGB(0, 0, 0));
            }

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

    private int PaintColumnContent(IntPtr hdc, IColumnViewportReader reader)
    {
        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT clientRect);
        IReadOnlyList<string> headers = GetGridHeaders(reader);
        int[] widths = CalculateColumnWidths(reader);

        PaintGridRow(hdc, clientRect, headers, widths, y: 0, isHeader: true, isSelected: false);

        int y = _lineHeight;
        int visibleNonEmptyLines = 0;
        int dataRowIndex = 0;
        if (reader.ColumnHeaders.Count > 0)
        {
            foreach (IReadOnlyList<string> row in reader.CurrentCells)
            {
                PaintGridRow(hdc, clientRect, row, widths, y, isHeader: false, IsDataRowSelected(dataRowIndex));
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
            foreach (string row in reader.CurrentRows)
            {
                PaintGridRow(hdc, clientRect, new[] { row }, widths, y, isHeader: false, IsDataRowSelected(dataRowIndex));
                visibleNonEmptyLines++;
                dataRowIndex++;

                y += _lineHeight;
                if (y >= clientRect.bottom)
                {
                    break;
                }
            }
        }

        return visibleNonEmptyLines;
    }

    private void PaintGridRow(IntPtr hdc, NativeMethods.RECT clientRect, IReadOnlyList<string> cells, IReadOnlyList<int> widths, int y, bool isHeader, bool isSelected)
    {
        int startChars = 0;
        for (int i = 0; i < widths.Count; i++)
        {
            int widthChars = Math.Max(1, widths[i]);
            NativeMethods.RECT cellRect = new()
            {
                left = (startChars - _xOffsetChars) * _charWidth,
                top = y,
                right = (startChars + widthChars - _xOffsetChars) * _charWidth,
                bottom = y + _lineHeight
            };

            if (Intersects(cellRect, clientRect))
            {
                string value = i < cells.Count ? cells[i] : string.Empty;
                PaintGridCell(hdc, cellRect, Intersect(cellRect, clientRect), value, widthChars, isHeader, isSelected);
            }

            startChars += widthChars;
        }
    }

    private void PaintGridCell(IntPtr hdc, NativeMethods.RECT cellRect, NativeMethods.RECT visibleRect, string text, int widthChars, bool isHeader, bool isSelected)
    {
        if (visibleRect.right <= visibleRect.left || visibleRect.bottom <= visibleRect.top)
        {
            return;
        }

        IntPtr backgroundBrush = isSelected
            ? (_hasFocus ? NativeMethods.GetSysColorBrush(NativeMethods.COLOR_HIGHLIGHT) : GetInactiveSelectionBrush())
            : NativeMethods.GetSysColorBrush(isHeader ? NativeMethods.COLOR_3DFACE : NativeMethods.COLOR_WINDOW);
        NativeMethods.FillRect(hdc, ref visibleRect, backgroundBrush);
        NativeMethods.SetTextColor(
            hdc,
            NativeMethods.GetSysColor(isSelected && _hasFocus ? NativeMethods.COLOR_HIGHLIGHTTEXT : NativeMethods.COLOR_WINDOWTEXT));

        NativeMethods.RECT textRect = cellRect;
        textRect.left += GridCellPaddingPx;
        textRect.right -= GridCellPaddingPx;
        if (textRect.right > textRect.left)
        {
            int textCapacityChars = GetGridTextCapacityChars(widthChars);
            string visibleText = FitGridCellText(text, textCapacityChars);
            if (visibleText.Length > 0)
            {
                NativeMethods.DrawTextW(
                    hdc,
                    visibleText,
                    visibleText.Length,
                    ref textRect,
                    NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);
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
                widths[i] = Math.Max(GetMinimumColumnWidth(reader, i), _manualColumnWidths[i]);
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
            int defaultWidthPx = i == 0 ? DefaultTextColumnWidthPx : DefaultGroupColumnWidthPx;
            widths[i] = Math.Max(GetMinimumColumnWidth(reader, i), PixelsToColumnWidthChars(defaultWidthPx));
        }

        return widths;
    }

    private int[] CalculateAutoColumnWidths(IColumnViewportReader reader)
    {
        IReadOnlyList<string> headers = GetGridHeaders(reader);
        int columnCount = headers.Count;
        int[] widths = new int[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            widths[i] = headers[i].Length;
        }

        if (reader.ColumnHeaders.Count > 0)
        {
            foreach (IReadOnlyList<string> row in reader.CurrentCells)
            {
                int count = Math.Min(columnCount, row.Count);
                for (int i = 0; i < count; i++)
                {
                    widths[i] = Math.Max(widths[i], row[i].Length);
                }
            }
        }
        else
        {
            foreach (string row in reader.CurrentRows)
            {
                widths[0] = Math.Max(widths[0], row.Length);
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
        int contentMin = columnIndex == 0 ? headers[columnIndex].Length : 1;

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
        int maxOffset = Math.Max(0, GetContentWidthChars() - _visibleColumnCount);
        int nextOffset = Math.Clamp(requestedOffset, 0, maxOffset);
        if (nextOffset == _xOffsetChars)
        {
            return;
        }

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
        int boundaryChars = 0;
        for (int i = 0; i < widths.Length; i++)
        {
            boundaryChars += widths[i];
            int boundaryX = (boundaryChars - _xOffsetChars) * _charWidth;
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
        int horizontalMax = Math.Max(0, contentWidthChars - _visibleColumnCount);
        _xOffsetChars = Math.Clamp(_xOffsetChars, 0, horizontalMax);
        NativeMethods.SCROLLINFO horizontal = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS,
            nMin = 0,
            nMax = contentWidthChars,
            nPage = (uint)Math.Max(1, Math.Min(contentWidthChars, _visibleColumnCount)),
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
            for (int i = 0; i < widths.Length; i++)
            {
                total += Math.Max(0, widths[i]);
            }

            return Math.Max(1, total);
        }

        return VisualRowReader.VisibleSegmentChars;
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
            self.ResetColumnResizeState(clearManualWidths: true);
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
