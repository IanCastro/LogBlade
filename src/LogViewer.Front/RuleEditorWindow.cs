using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class RuleEditorWindow
{
    private const int IdNameEdit = 101;
    private const int IdRegexMode = 102;
    private const int IdJsonMode = 103;
    private const int IdRuleEdit = 104;
    private const int IdSampleEdit = 105;
    private const int IdPreviewEdit = 106;
    private const int IdTemplateEdit = 107;
    private const int IdSaveButton = 201;
    private const int IdCancelButton = 202;

    private readonly IReadOnlyList<DisplayParserRule> _existingRules;
    private readonly DisplayParserRule? _initialRule;
    private readonly string? _originalName;
    private IntPtr _hwnd;
    private IntPtr _owner;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private IntPtr _nameLabel;
    private IntPtr _nameEdit;
    private IntPtr _typeLabel;
    private IntPtr _regexRadio;
    private IntPtr _jsonRadio;
    private IntPtr _ruleLabel;
    private IntPtr _ruleEdit;
    private IntPtr _templateLabel;
    private IntPtr _templateEdit;
    private IntPtr _sampleLabel;
    private IntPtr _sampleEdit;
    private IntPtr _previewLabel;
    private IntPtr _previewEdit;
    private IntPtr _saveButton;
    private IntPtr _cancelButton;
    private DisplayParserMode _mode = DisplayParserMode.Json;
    private bool _closed;
    private bool _updatingPreview;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public RuleEditorWindow(IReadOnlyList<DisplayParserRule> existingRules)
        : this(existingRules, initialRule: null)
    {
    }

    public RuleEditorWindow(IReadOnlyList<DisplayParserRule> existingRules, DisplayParserRule? initialRule)
    {
        _existingRules = existingRules;
        _initialRule = initialRule;
        _originalName = initialRule?.Name;
        _mode = initialRule?.Mode ?? DisplayParserMode.Json;
    }

    public DisplayParserRule? SavedRule { get; private set; }

    public DisplayParserRule? ShowModal(IntPtr owner)
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

        return SavedRule;
    }

    private void RegisterClass()
    {
        if (s_registered)
        {
            return;
        }

        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        const string className = "LogViewerRuleEditorWindow";
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
            throw new InvalidOperationException("RegisterClassExW failed for rule editor.");
        }

        s_registered = true;
    }

    private void CreateWindow()
    {
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            "LogViewerRuleEditorWindow",
            _initialRule is null ? "Add Parser Rule" : "Edit Parser Rule",
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            780,
            640,
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

            throw new InvalidOperationException("CreateWindowExW failed for rule editor.");
        }
    }

    private static RuleEditorWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr ptr = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return ptr == IntPtr.Zero ? null : (RuleEditorWindow?)GCHandle.FromIntPtr(ptr).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, create.lpCreateParams);
        }

        RuleEditorWindow? self = FromHandle(hwnd);
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

        _nameLabel = CreateLabel("Nome");
        _nameEdit = CreateEdit(IdNameEdit, multiline: false, readOnly: false);
        _typeLabel = CreateLabel("Tipo");
        _regexRadio = CreateRadio("Regex", IdRegexMode);
        _jsonRadio = CreateRadio("JSON", IdJsonMode);
        _ruleLabel = CreateLabel("Regra");
        _ruleEdit = CreateEdit(IdRuleEdit, multiline: true, readOnly: false);
        _templateLabel = CreateLabel("Display");
        _templateEdit = CreateEdit(IdTemplateEdit, multiline: true, readOnly: false);
        _sampleLabel = CreateLabel("Amostra");
        _sampleEdit = CreateEdit(IdSampleEdit, multiline: true, readOnly: false);
        _previewLabel = CreateLabel("Preview");
        _previewEdit = CreateEdit(IdPreviewEdit, multiline: true, readOnly: true);
        _saveButton = CreateButton("Save", IdSaveButton);
        _cancelButton = CreateButton("Cancel", IdCancelButton);

        SetButtonChecked(_regexRadio, _mode == DisplayParserMode.Regex);
        SetButtonChecked(_jsonRadio, _mode == DisplayParserMode.Json);
        if (_initialRule is null)
        {
            SetDefaultJsonExample();
        }
        else
        {
            NativeMethods.SetWindowTextW(_nameEdit, _initialRule.Name);
            NativeMethods.SetWindowTextW(_ruleEdit, _initialRule.Rule);
            NativeMethods.SetWindowTextW(_templateEdit, _initialRule.Template);
            NativeMethods.SetWindowTextW(_sampleEdit, _initialRule.Sample);
        }

        UpdateModeUi();
        UpdatePreview();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);

        if (notification == NativeMethods.BN_CLICKED && id is IdRegexMode or IdJsonMode)
        {
            _mode = id == IdRegexMode ? DisplayParserMode.Regex : DisplayParserMode.Json;
            SetButtonChecked(_regexRadio, _mode == DisplayParserMode.Regex);
            SetButtonChecked(_jsonRadio, _mode == DisplayParserMode.Json);
            if (_mode == DisplayParserMode.Json)
            {
                SetDefaultJsonExample();
            }
            else
            {
                SetDefaultRegexExample();
            }

            UpdateModeUi();
            UpdatePreview();
            return;
        }

        if (notification == NativeMethods.EN_CHANGE && id is IdRuleEdit or IdTemplateEdit or IdSampleEdit)
        {
            UpdatePreview();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdSaveButton)
        {
            SaveRule();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdCancelButton)
        {
            Close();
        }
    }

    private void SaveRule()
    {
        string name = GetWindowText(_nameEdit).Trim();
        string rule = GetWindowText(_ruleEdit).Trim();
        string template = GetWindowText(_templateEdit).Trim();
        string sample = GetWindowText(_sampleEdit);

        if (name.Length == 0)
        {
            ShowError("Name is required.");
            return;
        }

        if (rule.Length == 0)
        {
            ShowError("Rule is required.");
            return;
        }

        for (int i = 0; i < _existingRules.Count; i++)
        {
            bool isCurrentRule = _originalName is not null &&
                string.Equals(_existingRules[i].Name, _originalName, StringComparison.OrdinalIgnoreCase);
            if (!isCurrentRule && string.Equals(_existingRules[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                ShowError("A rule with this name already exists.");
                return;
            }
        }

        if (_mode == DisplayParserMode.Regex)
        {
            try
            {
                DisplayParserEvaluator.ValidateRule(new DisplayParserRule
                {
                    Mode = _mode,
                    Rule = rule,
                    Template = template
                });
            }
            catch (ArgumentException ex)
            {
                ShowError("Invalid regex: " + ex.Message);
                return;
            }
        }

        SavedRule = new DisplayParserRule
        {
            Name = name,
            Mode = _mode,
            Rule = rule,
            Template = _mode == DisplayParserMode.Regex ? template : string.Empty,
            Sample = sample
        };
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
        int height = Math.Max(420, client.bottom - client.top);
        const int margin = 16;
        const int labelWidth = 78;
        const int rowHeight = 26;
        const int gap = 10;
        int inputLeft = margin + labelWidth + 12;
        int inputWidth = Math.Max(280, width - inputLeft - margin);
        int y = margin;
        bool isRegex = _mode == DisplayParserMode.Regex;
        int ruleHeight = isRegex ? 58 : 78;
        int templateHeight = isRegex ? 48 : 0;

        Move(_nameLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_nameEdit, inputLeft, y, inputWidth, rowHeight);

        y += rowHeight + gap;
        Move(_typeLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_regexRadio, inputLeft, y, 88, rowHeight);
        Move(_jsonRadio, inputLeft + 96, y, 88, rowHeight);

        y += rowHeight + gap;
        Move(_ruleLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_ruleEdit, inputLeft, y, inputWidth, ruleHeight);

        y += ruleHeight + gap;
        if (isRegex)
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

        int buttonsHeight = 34;
        int remaining = Math.Max(160, height - y - buttonsHeight - margin - gap);
        int sampleHeight = Math.Max(80, (remaining - gap) / 2);
        int previewHeight = Math.Max(80, remaining - sampleHeight - gap);

        Move(_sampleLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_sampleEdit, inputLeft, y, inputWidth, sampleHeight);

        y += sampleHeight + gap;
        Move(_previewLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_previewEdit, inputLeft, y, inputWidth, previewHeight);

        y += previewHeight + gap;
        Move(_cancelButton, width - margin - 90, y, 90, rowHeight);
        Move(_saveButton, width - margin - 190, y, 90, rowHeight);
    }

    private void UpdatePreview()
    {
        if (_updatingPreview || _previewEdit == IntPtr.Zero)
        {
            return;
        }

        _updatingPreview = true;
        DisplayParserRule previewRule = new()
        {
            Name = GetWindowText(_nameEdit),
            Mode = _mode,
            Rule = GetWindowText(_ruleEdit),
            Template = GetWindowText(_templateEdit),
            Sample = GetWindowText(_sampleEdit)
        };
        string result = EvaluateSample(previewRule);
        NativeMethods.SetWindowTextW(_previewEdit, result);
        _updatingPreview = false;
    }

    private static string EvaluateSample(DisplayParserRule rule)
    {
        if (rule.Sample.Length == 0)
        {
            return string.Empty;
        }

        string normalized = rule.Sample.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        string[] output = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            output[i] = line.Length == 0
                ? string.Empty
                : DisplayParserEvaluator.EvaluateOrOriginal(rule, line);
        }

        return string.Join(Environment.NewLine, output);
    }

    private void SetDefaultJsonExample()
    {
        NativeMethods.SetWindowTextW(_ruleEdit, "{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}");
        NativeMethods.SetWindowTextW(_templateEdit, string.Empty);
        NativeMethods.SetWindowTextW(
            _sampleEdit,
            "{ \"Timestamp\": \"2025-09-12 14:50:48.637060\", \"Level\": \"Info\", \"Logger\": \"EventScheduler\", \"Message\": \"Strategy task JT67_48_250912145048_00064 is running\" }");
    }

    private void SetDefaultRegexExample()
    {
        NativeMethods.SetWindowTextW(
            _ruleEdit,
            @"(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+).*?\[(?<Logger>[^\]]+)\].*?(?<Level>Trace|Debug|Info|Warn|Error|Fatal|Information).*? - (?<Message>.*)");
        NativeMethods.SetWindowTextW(_templateEdit, "{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}");
        NativeMethods.SetWindowTextW(_sampleEdit, "2025-09-12 14:50:48.637060 [EventScheduler] Info EventScheduler - Strategy task JT67_48_250912145048_00064 is running");
    }

    private void UpdateModeUi()
    {
        bool isRegex = _mode == DisplayParserMode.Regex;
        NativeMethods.SetWindowTextW(_ruleLabel, isRegex ? "Regex" : "Template");
        NativeMethods.SetWindowTextW(_templateLabel, "Display");
        NativeMethods.ShowWindow(_templateLabel, isRegex ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        NativeMethods.ShowWindow(_templateEdit, isRegex ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
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

    private static bool IsButtonChecked(IntPtr hwnd)
    {
        return NativeMethods.SendMessageW(hwnd, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32() == NativeMethods.BST_CHECKED;
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
