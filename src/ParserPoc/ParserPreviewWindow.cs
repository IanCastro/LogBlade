using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class ParserPreviewWindow
{
    private const int IdRuleCombo = 101;
    private const int IdInputEdit = 202;
    private const int IdPreviewEdit = 203;
    private const string ConfigureRuleText = "Configure";

    private IntPtr _hwnd;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private IntPtr _ruleLabel;
    private IntPtr _ruleCombo;
    private IntPtr _inputLabel;
    private IntPtr _inputEdit;
    private IntPtr _previewLabel;
    private IntPtr _previewEdit;
    private List<ParserRule> _rules = new();
    private int _selectedRuleIndex = -1;
    private bool _updatingPreview;
    private bool _updatingCombo;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;

    public void Run()
    {
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        const string className = "LogParserPocWindow";

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

        if (NativeMethods.RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException("RegisterClassExW failed.");
        }

        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            className,
            "Parser POC",
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            940,
            640,
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
    }

    private static ParserPreviewWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr ptr = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return ptr == IntPtr.Zero ? null : (ParserPreviewWindow?)GCHandle.FromIntPtr(ptr).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, create.lpCreateParams);
        }

        ParserPreviewWindow? self = FromHandle(hwnd);
        if (self is not null)
        {
            switch (msg)
            {
                case NativeMethods.WM_CREATE:
                    self.OnCreate(hwnd);
                    return IntPtr.Zero;
                case NativeMethods.WM_SIZE:
                    self.Layout();
                    return IntPtr.Zero;
                case NativeMethods.WM_COMMAND:
                    self.OnCommand(wParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_DESTROY:
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;
                case NativeMethods.WM_NCDESTROY:
                    if (self._selfHandle.IsAllocated)
                    {
                        self._selfHandle.Free();
                    }

                    NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, IntPtr.Zero);
                    break;
            }
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnCreate(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _font = NativeMethods.GetStockObject(NativeMethods.DEFAULT_GUI_FONT);

        _ruleLabel = CreateLabel("Regra");
        _ruleCombo = CreateCombo(IdRuleCombo);
        _inputLabel = CreateLabel("Input");
        _inputEdit = CreateEdit(IdInputEdit, multiline: true, readOnly: false);
        _previewLabel = CreateLabel("Preview");
        _previewEdit = CreateEdit(IdPreviewEdit, multiline: true, readOnly: true);

        ReloadRules(selectRuleName: null);
        Layout();
        UpdatePreview();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);

        if (id == IdRuleCombo && notification == NativeMethods.CBN_SELCHANGE && !_updatingCombo)
        {
            OnRuleSelectionChanged();
            return;
        }

        if (id == IdInputEdit && notification == NativeMethods.EN_CHANGE)
        {
            UpdatePreview();
        }
    }

    private void OnRuleSelectionChanged()
    {
        int selected = NativeMethods.SendMessageW(_ruleCombo, NativeMethods.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (selected == _rules.Count)
        {
            OpenRuleManager();
            return;
        }

        _selectedRuleIndex = selected >= 0 && selected < _rules.Count ? selected : -1;
        UpdatePreview();
    }

    private void OpenRuleManager()
    {
        string? activeRuleName = SelectedRule?.Name;
        RuleManagerWindow manager = new(_rules, activeRuleName);
        string? selectedRuleName = manager.ShowModal(_hwnd);
        ReloadRules(selectedRuleName);
        UpdatePreview();
    }

    private void ReloadRules(string? selectRuleName)
    {
        _rules = ParserRuleStore.Load();
        _updatingCombo = true;
        NativeMethods.SendMessageW(_ruleCombo, NativeMethods.CB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        for (int i = 0; i < _rules.Count; i++)
        {
            NativeMethods.SendMessageW(_ruleCombo, NativeMethods.CB_ADDSTRING, IntPtr.Zero, _rules[i].Name);
        }

        NativeMethods.SendMessageW(_ruleCombo, NativeMethods.CB_ADDSTRING, IntPtr.Zero, ConfigureRuleText);
        _selectedRuleIndex = FindRuleIndex(selectRuleName);
        SetComboSelection(_selectedRuleIndex);
        _updatingCombo = false;
    }

    private int FindRuleIndex(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        for (int i = 0; i < _rules.Count; i++)
        {
            if (string.Equals(_rules[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void SetComboSelection(int index)
    {
        _updatingCombo = true;
        NativeMethods.SendMessageW(_ruleCombo, NativeMethods.CB_SETCURSEL, new IntPtr(index), IntPtr.Zero);
        _updatingCombo = false;
    }

    private ParserRule? SelectedRule => _selectedRuleIndex >= 0 && _selectedRuleIndex < _rules.Count
        ? _rules[_selectedRuleIndex]
        : null;

    private void Layout()
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT client))
        {
            return;
        }

        int width = Math.Max(520, client.right - client.left);
        int height = Math.Max(360, client.bottom - client.top);
        const int margin = 16;
        const int labelWidth = 76;
        const int rowHeight = 26;
        const int gap = 12;
        int inputLeft = margin + labelWidth + 12;
        int inputWidth = Math.Max(280, width - inputLeft - margin);
        int y = margin;

        Move(_ruleLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_ruleCombo, inputLeft, y, inputWidth, 220);

        y += rowHeight + gap;
        int available = Math.Max(180, height - y - margin);
        int inputHeight = Math.Max(90, (available - gap) / 2);
        int previewHeight = Math.Max(90, available - inputHeight - gap);

        Move(_inputLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_inputEdit, inputLeft, y, inputWidth, inputHeight);

        y += inputHeight + gap;
        Move(_previewLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_previewEdit, inputLeft, y, inputWidth, previewHeight);
    }

    private void UpdatePreview()
    {
        if (_updatingPreview || _previewEdit == IntPtr.Zero)
        {
            return;
        }

        _updatingPreview = true;
        string input = GetWindowText(_inputEdit);
        string result = ParserEvaluator.EvaluateLines(SelectedRule, input);
        NativeMethods.SetWindowTextW(_previewEdit, result);
        _updatingPreview = false;
    }

    private IntPtr CreateLabel(string text)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "STATIC",
            text,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE,
            0,
            0,
            10,
            10,
            _hwnd,
            IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null),
            IntPtr.Zero);
        SetControlFont(hwnd);
        return hwnd;
    }

    private IntPtr CreateCombo(int id)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "COMBOBOX",
            string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_VSCROLL | NativeMethods.CBS_DROPDOWNLIST | NativeMethods.CBS_HASSTRINGS,
            0,
            0,
            10,
            10,
            _hwnd,
            new IntPtr(id),
            NativeMethods.GetModuleHandleW(null),
            IntPtr.Zero);
        SetControlFont(hwnd);
        return hwnd;
    }

    private IntPtr CreateEdit(int id, bool multiline, bool readOnly)
    {
        int style = NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER;
        style |= multiline
            ? NativeMethods.ES_MULTILINE | NativeMethods.ES_AUTOVSCROLL | NativeMethods.ES_WANTRETURN | NativeMethods.WS_VSCROLL
            : NativeMethods.ES_AUTOHSCROLL;
        if (readOnly)
        {
            style |= NativeMethods.ES_READONLY;
        }

        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "EDIT",
            string.Empty,
            style,
            0,
            0,
            10,
            10,
            _hwnd,
            new IntPtr(id),
            NativeMethods.GetModuleHandleW(null),
            IntPtr.Zero);
        SetControlFont(hwnd);
        return hwnd;
    }

    private void SetControlFont(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && _font != IntPtr.Zero)
        {
            NativeMethods.SendMessageW(hwnd, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        }
    }

    private static void Move(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.MoveWindow(hwnd, x, y, width, height, true);
        }
    }

    private static string GetWindowText(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(length + 1);
        NativeMethods.GetWindowTextW(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }
}
