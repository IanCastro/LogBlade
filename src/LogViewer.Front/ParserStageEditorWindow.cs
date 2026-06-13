using System;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class ParserStageEditorWindow
{
    private const int IdRegexMode = 101;
    private const int IdJsonMode = 102;
    private const int IdRegexReplaceMode = 103;
    private const int IdRuleEdit = 104;
    private const int IdTemplateEdit = 105;
    private const int IdSaveButton = 201;
    private const int IdCancelButton = 202;

    private readonly DisplayParserStage? _initialStage;
    private IntPtr _hwnd;
    private IntPtr _owner;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private IntPtr _typeLabel;
    private IntPtr _regexRadio;
    private IntPtr _jsonRadio;
    private IntPtr _regexReplaceRadio;
    private IntPtr _ruleLabel;
    private IntPtr _ruleEdit;
    private IntPtr _templateLabel;
    private IntPtr _templateEdit;
    private IntPtr _saveButton;
    private IntPtr _cancelButton;
    private DisplayParserMode _mode;
    private bool _closed;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public ParserStageEditorWindow()
        : this(initialStage: null)
    {
    }

    public ParserStageEditorWindow(DisplayParserStage? initialStage)
    {
        _initialStage = initialStage;
        _mode = initialStage?.Mode ?? DisplayParserMode.Json;
    }

    public DisplayParserStage? SavedStage { get; private set; }

    public DisplayParserStage? ShowModal(IntPtr owner)
    {
        _owner = owner;
        RegisterClass();
        CreateWindow();

        NativeMethods.EnableWindow(_owner, false);
        try
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWDEFAULT);
            NativeMethods.UpdateWindow(_hwnd);

            NativeMethods.MSG msg;
            while (!_closed && NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }
        }
        finally
        {
            NativeMethods.EnableWindow(_owner, true);
            NativeMethods.SetActiveWindow(_owner);
        }

        return SavedStage;
    }

    private void RegisterClass()
    {
        if (s_registered)
        {
            return;
        }

        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        const string className = "LogViewerParserStageEditorWindow";
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
            throw new InvalidOperationException("RegisterClassExW failed for parser stage editor.");
        }

        s_registered = true;
    }

    private void CreateWindow()
    {
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            "LogViewerParserStageEditorWindow",
            _initialStage is null ? "Add Parser Stage" : "Edit Parser Stage",
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            720,
            330,
            _owner,
            IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null),
            GCHandle.ToIntPtr(_selfHandle));

        if (_hwnd == IntPtr.Zero)
        {
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            throw new InvalidOperationException("CreateWindowExW failed for parser stage editor.");
        }
    }

    private static ParserStageEditorWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr ptr = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return ptr == IntPtr.Zero ? null : (ParserStageEditorWindow?)GCHandle.FromIntPtr(ptr).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, create.lpCreateParams);
        }

        ParserStageEditorWindow? self = FromHandle(hwnd);
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
                    self._closed = true;
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

        _typeLabel = CreateLabel("Tipo");
        _regexRadio = CreateRadio("Regex", IdRegexMode);
        _regexReplaceRadio = CreateRadio("Regex Replace", IdRegexReplaceMode);
        _jsonRadio = CreateRadio("JSON", IdJsonMode);
        _ruleLabel = CreateLabel("Regra");
        _ruleEdit = CreateEdit(IdRuleEdit, multiline: true, readOnly: false);
        _templateLabel = CreateLabel("Display");
        _templateEdit = CreateEdit(IdTemplateEdit, multiline: true, readOnly: false);
        _saveButton = CreateButton("Save", IdSaveButton);
        _cancelButton = CreateButton("Cancel", IdCancelButton);

        SetButtonChecked(_regexRadio, _mode == DisplayParserMode.Regex);
        SetButtonChecked(_regexReplaceRadio, _mode == DisplayParserMode.RegexReplace);
        SetButtonChecked(_jsonRadio, _mode == DisplayParserMode.Json);
        if (_initialStage is null)
        {
            SetDefaultJsonStage();
        }
        else
        {
            NativeMethods.SetWindowTextW(_ruleEdit, _initialStage.Rule);
            NativeMethods.SetWindowTextW(_templateEdit, _initialStage.Template);
        }

        UpdateModeUi();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);

        if (notification == NativeMethods.BN_CLICKED && id is IdRegexMode or IdJsonMode or IdRegexReplaceMode)
        {
            _mode = id switch
            {
                IdRegexMode => DisplayParserMode.Regex,
                IdRegexReplaceMode => DisplayParserMode.RegexReplace,
                _ => DisplayParserMode.Json
            };
            SetButtonChecked(_regexRadio, _mode == DisplayParserMode.Regex);
            SetButtonChecked(_regexReplaceRadio, _mode == DisplayParserMode.RegexReplace);
            SetButtonChecked(_jsonRadio, _mode == DisplayParserMode.Json);
            if (_mode == DisplayParserMode.Json)
            {
                SetDefaultJsonStage();
            }
            else if (_mode == DisplayParserMode.RegexReplace)
            {
                SetDefaultRegexReplaceStage();
            }
            else
            {
                SetDefaultRegexStage();
            }

            UpdateModeUi();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdSaveButton)
        {
            SaveStage();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdCancelButton)
        {
            Close();
        }
    }

    private void SaveStage()
    {
        string rule = GetWindowText(_ruleEdit).Trim();
        string rawTemplate = GetWindowText(_templateEdit);
        string template = _mode == DisplayParserMode.RegexReplace ? rawTemplate : rawTemplate.Trim();

        DisplayParserStage stage = new()
        {
            Mode = _mode,
            Rule = rule,
            Template = _mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace ? template : string.Empty
        };

        try
        {
            DisplayParserEvaluator.ValidateStage(stage);
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            return;
        }

        SavedStage = stage;
        Close();
    }

    private void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
        }
    }

    private void Layout()
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT client))
        {
            return;
        }

        int width = Math.Max(520, client.right - client.left);
        int height = Math.Max(240, client.bottom - client.top);
        const int margin = 16;
        const int labelWidth = 78;
        const int rowHeight = 26;
        const int gap = 10;
        int inputLeft = margin + labelWidth + 12;
        int inputWidth = Math.Max(300, width - inputLeft - margin);
        int y = margin;
        bool hasTemplate = _mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace;
        int ruleHeight = hasTemplate ? 72 : Math.Max(90, height - margin - rowHeight - gap - rowHeight - margin - gap);
        int templateHeight = 56;

        Move(_typeLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_regexRadio, inputLeft, y, 76, rowHeight);
        Move(_regexReplaceRadio, inputLeft + 84, y, 126, rowHeight);
        Move(_jsonRadio, inputLeft + 218, y, 88, rowHeight);

        y += rowHeight + gap;
        Move(_ruleLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_ruleEdit, inputLeft, y, inputWidth, ruleHeight);

        y += ruleHeight + gap;
        if (hasTemplate)
        {
            Move(_templateLabel, margin, y + 4, labelWidth, rowHeight);
            Move(_templateEdit, inputLeft, y, inputWidth, templateHeight);
            y += templateHeight + gap;
        }
        else
        {
            Move(_templateLabel, 0, 0, 0, 0);
            Move(_templateEdit, 0, 0, 0, 0);
        }

        Move(_cancelButton, width - margin - 90, y, 90, rowHeight);
        Move(_saveButton, width - margin - 190, y, 90, rowHeight);
    }

    private void SetDefaultJsonStage()
    {
        NativeMethods.SetWindowTextW(_ruleEdit, "{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}");
        NativeMethods.SetWindowTextW(_templateEdit, string.Empty);
    }

    private void SetDefaultRegexStage()
    {
        NativeMethods.SetWindowTextW(_ruleEdit, @": (?<json>.*)");
        NativeMethods.SetWindowTextW(_templateEdit, "{json}");
    }

    private void SetDefaultRegexReplaceStage()
    {
        NativeMethods.SetWindowTextW(_ruleEdit, @"\u0001");
        NativeMethods.SetWindowTextW(_templateEdit, "|");
    }

    private void UpdateModeUi()
    {
        bool hasTemplate = _mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace;
        NativeMethods.SetWindowTextW(_ruleLabel, _mode == DisplayParserMode.Json ? "Template" : "Regex");
        NativeMethods.SetWindowTextW(_templateLabel, _mode == DisplayParserMode.RegexReplace ? "Replacement" : "Display");
        NativeMethods.ShowWindow(_templateLabel, hasTemplate ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        NativeMethods.ShowWindow(_templateEdit, hasTemplate ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        Layout();
    }

    private void ShowError(string message)
    {
        NativeMethods.MessageBoxW(_hwnd, message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
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

    private IntPtr CreateRadio(string text, int id)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            text,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTORADIOBUTTON | NativeMethods.WS_GROUP,
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

    private IntPtr CreateButton(string text, int id)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            text,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_PUSHBUTTON,
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

    private static void SetButtonChecked(IntPtr hwnd, bool checkedState)
    {
        NativeMethods.SendMessageW(
            hwnd,
            NativeMethods.BM_SETCHECK,
            new IntPtr(checkedState ? NativeMethods.BST_CHECKED : NativeMethods.BST_UNCHECKED),
            IntPtr.Zero);
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
