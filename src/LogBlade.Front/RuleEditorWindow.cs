using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class RuleEditorWindow
{
    private const int IdNameEdit = 101;
    private const int IdStagesList = 102;
    private const int IdSampleEdit = 103;
    private const int IdPreviewEdit = 104;
    private const int IdAddStageButton = 201;
    private const int IdEditStageButton = 202;
    private const int IdRemoveStageButton = 203;
    private const int IdUpStageButton = 204;
    private const int IdDownStageButton = 205;
    private const int IdSaveButton = 206;
    private const int IdCancelButton = 207;

    private readonly IReadOnlyList<DisplayParserRule> _existingRules;
    private readonly DisplayParserRule? _initialRule;
    private readonly string? _originalName;
    private readonly string _defaultSample;
    private readonly List<DisplayParserStage> _stages;
    private IntPtr _hwnd;
    private IntPtr _owner;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private IntPtr _nameLabel;
    private IntPtr _nameEdit;
    private IntPtr _stagesLabel;
    private IntPtr _stagesList;
    private IntPtr _addStageButton;
    private IntPtr _editStageButton;
    private IntPtr _removeStageButton;
    private IntPtr _upStageButton;
    private IntPtr _downStageButton;
    private IntPtr _sampleLabel;
    private IntPtr _sampleEdit;
    private IntPtr _previewLabel;
    private IntPtr _previewEdit;
    private IntPtr _saveButton;
    private IntPtr _cancelButton;
    private bool _closed;
    private bool _updatingList;
    private bool _updatingPreview;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public RuleEditorWindow(IReadOnlyList<DisplayParserRule> existingRules)
        : this(existingRules, initialRule: null, defaultSample: string.Empty)
    {
    }

    public RuleEditorWindow(IReadOnlyList<DisplayParserRule> existingRules, string defaultSample)
        : this(existingRules, initialRule: null, defaultSample)
    {
    }

    public RuleEditorWindow(IReadOnlyList<DisplayParserRule> existingRules, DisplayParserRule? initialRule)
        : this(existingRules, initialRule, defaultSample: string.Empty)
    {
    }

    private RuleEditorWindow(IReadOnlyList<DisplayParserRule> existingRules, DisplayParserRule? initialRule, string defaultSample)
    {
        _existingRules = existingRules;
        _initialRule = initialRule;
        _originalName = initialRule?.Name;
        _defaultSample = defaultSample;
        _stages = initialRule is null
            ? new List<DisplayParserStage>()
            : initialRule.Stages.ConvertAll(stage => stage.Clone());
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
        const string className = "LogBladeRuleEditorWindow";
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
            "LogBladeRuleEditorWindow",
            _initialRule is null ? "Add Parser Rule" : "Edit Parser Rule",
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            820,
            680,
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

        AppIcon.ApplyToWindow(_hwnd);
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
        _stagesLabel = CreateLabel("Stages");
        _stagesList = CreateListBox(IdStagesList);
        _addStageButton = CreateButton("Add", IdAddStageButton);
        _editStageButton = CreateButton("Edit", IdEditStageButton);
        _removeStageButton = CreateButton("Remove", IdRemoveStageButton);
        _upStageButton = CreateButton("Up", IdUpStageButton);
        _downStageButton = CreateButton("Down", IdDownStageButton);
        _sampleLabel = CreateLabel("Amostra");
        _sampleEdit = CreateEdit(IdSampleEdit, multiline: true, readOnly: false);
        _previewLabel = CreateLabel("Preview");
        _previewEdit = CreateEdit(IdPreviewEdit, multiline: true, readOnly: true);
        _saveButton = CreateButton("Save", IdSaveButton);
        _cancelButton = CreateButton("Cancel", IdCancelButton);

        if (_initialRule is null)
        {
            NativeMethods.SetWindowTextW(_sampleEdit, _defaultSample.Length == 0 ? DefaultJsonSample() : _defaultSample);
        }
        else
        {
            NativeMethods.SetWindowTextW(_nameEdit, _initialRule.Name);
            NativeMethods.SetWindowTextW(_sampleEdit, _initialRule.Sample);
        }

        ReloadStagesList(_stages.Count == 0 ? -1 : 0);
        Layout();
        UpdatePreview();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);

        if (id == IdStagesList && notification == NativeMethods.LBN_DBLCLK && !_updatingList)
        {
            EditStage();
            return;
        }

        if (notification == NativeMethods.EN_CHANGE && id == IdSampleEdit)
        {
            UpdatePreview();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdAddStageButton)
        {
            AddStage();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdEditStageButton)
        {
            EditStage();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdRemoveStageButton)
        {
            RemoveStage();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdUpStageButton)
        {
            MoveStage(-1);
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdDownStageButton)
        {
            MoveStage(1);
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

    private void AddStage()
    {
        ParserStageEditorWindow editor = new(_stages, GetWindowText(_sampleEdit), _stages.Count);
        DisplayParserStage? saved = editor.ShowModal(_hwnd);
        if (saved is null)
        {
            return;
        }

        _stages.Add(saved);
        ReloadStagesList(_stages.Count - 1);
        UpdatePreview();
    }

    private void EditStage()
    {
        int index = GetSelectedStageIndex();
        if (index < 0)
        {
            ShowError("Select a stage to edit.");
            return;
        }

        ParserStageEditorWindow editor = new(_stages, GetWindowText(_sampleEdit), index);
        DisplayParserStage? saved = editor.ShowModal(_hwnd);
        if (saved is null)
        {
            return;
        }

        _stages[index] = saved;
        ReloadStagesList(index);
        UpdatePreview();
    }

    private void RemoveStage()
    {
        int index = GetSelectedStageIndex();
        if (index < 0)
        {
            ShowError("Select a stage to remove.");
            return;
        }

        _stages.RemoveAt(index);
        ReloadStagesList(Math.Min(index, _stages.Count - 1));
        UpdatePreview();
    }

    private void MoveStage(int direction)
    {
        int index = GetSelectedStageIndex();
        int next = index + direction;
        if (index < 0 || next < 0 || next >= _stages.Count)
        {
            return;
        }

        (_stages[index], _stages[next]) = (_stages[next], _stages[index]);
        ReloadStagesList(next);
        UpdatePreview();
    }

    private void SaveRule()
    {
        string name = GetWindowText(_nameEdit).Trim();
        string sample = GetWindowText(_sampleEdit);

        if (name.Length == 0)
        {
            ShowError("Name is required.");
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

        DisplayParserRule rule = new()
        {
            Name = name,
            Stages = CloneStages(),
            Sample = sample
        };

        try
        {
            DisplayParserEvaluator.ValidateRule(rule);
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            return;
        }

        SavedRule = rule;
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

        int width = Math.Max(560, client.right - client.left);
        int height = Math.Max(460, client.bottom - client.top);
        const int margin = 16;
        const int labelWidth = 78;
        const int rowHeight = 26;
        const int gap = 10;
        const int stageButtonWidth = 88;
        int inputLeft = margin + labelWidth + 12;
        int inputWidth = Math.Max(320, width - inputLeft - margin);
        int y = margin;

        Move(_nameLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_nameEdit, inputLeft, y, inputWidth, rowHeight);

        y += rowHeight + gap;
        int buttonsHeight = 34;
        int availableForContent = Math.Max(240, height - y - buttonsHeight - margin - gap);
        int stageButtonsHeight = (rowHeight * 5) + (gap * 4);
        int stagesHeight = Math.Max(stageButtonsHeight, Math.Min(190, availableForContent / 2));
        int stageListWidth = Math.Max(180, inputWidth - stageButtonWidth - gap);
        int stageButtonLeft = inputLeft + stageListWidth + gap;

        Move(_stagesLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_stagesList, inputLeft, y, stageListWidth, stagesHeight);
        Move(_addStageButton, stageButtonLeft, y, stageButtonWidth, rowHeight);
        Move(_editStageButton, stageButtonLeft, y + rowHeight + gap, stageButtonWidth, rowHeight);
        Move(_removeStageButton, stageButtonLeft, y + ((rowHeight + gap) * 2), stageButtonWidth, rowHeight);
        Move(_upStageButton, stageButtonLeft, y + ((rowHeight + gap) * 3), stageButtonWidth, rowHeight);
        Move(_downStageButton, stageButtonLeft, y + ((rowHeight + gap) * 4), stageButtonWidth, rowHeight);

        y += stagesHeight + gap;
        int remaining = Math.Max(160, height - y - buttonsHeight - margin - gap);
        int sampleHeight = Math.Max(70, (remaining - gap) / 2);
        int previewHeight = Math.Max(70, remaining - sampleHeight - gap);

        Move(_sampleLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_sampleEdit, inputLeft, y, inputWidth, sampleHeight);

        y += sampleHeight + gap;
        Move(_previewLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_previewEdit, inputLeft, y, inputWidth, previewHeight);

        y += previewHeight + gap;
        Move(_cancelButton, width - margin - 90, y, 90, rowHeight);
        Move(_saveButton, width - margin - 190, y, 90, rowHeight);
    }

    private void ReloadStagesList(int selectIndex)
    {
        _updatingList = true;
        NativeMethods.SendMessageW(_stagesList, NativeMethods.LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        for (int i = 0; i < _stages.Count; i++)
        {
            NativeMethods.SendMessageW(_stagesList, NativeMethods.LB_ADDSTRING, IntPtr.Zero, FormatStageLabel(i, _stages[i]));
        }

        if (selectIndex >= _stages.Count)
        {
            selectIndex = _stages.Count - 1;
        }

        NativeMethods.SendMessageW(_stagesList, NativeMethods.LB_SETCURSEL, new IntPtr(selectIndex), IntPtr.Zero);
        _updatingList = false;
    }

    private int GetSelectedStageIndex()
    {
        int index = NativeMethods.SendMessageW(_stagesList, NativeMethods.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
        return index >= 0 && index < _stages.Count ? index : -1;
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
            Stages = CloneStages(),
            Sample = GetWindowText(_sampleEdit)
        };
        string result = EvaluateSample(previewRule);
        NativeMethods.SetWindowTextW(_previewEdit, result);
        _updatingPreview = false;
    }

    private List<DisplayParserStage> CloneStages()
    {
        return _stages.ConvertAll(stage => stage.Clone());
    }

    private static string EvaluateSample(DisplayParserRule rule)
    {
        if (rule.Stages is not null && rule.Stages.Count > 0)
        {
            try
            {
                DisplayParserEvaluator.ValidateRule(rule);
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
        }

        if (rule.Sample.Length == 0)
        {
            return string.Empty;
        }

        return DisplayParserEvaluator.EvaluateLinesOrOriginal(rule, rule.Sample);
    }

    private static string FormatStageLabel(int index, DisplayParserStage stage)
    {
        string rule = stage.Rule.Replace("\r", " ").Replace("\n", " ");
        if (rule.Length > 92)
        {
            rule = rule.Substring(0, 89) + "...";
        }

        return $"{index + 1}. {stage.Mode} - {rule}";
    }

    private static string DefaultJsonSample()
    {
        return "{ \"Timestamp\": \"2025-09-12 14:50:48.637060\", \"Level\": \"Info\", \"Logger\": \"EventScheduler\", \"Message\": \"Strategy task JT67_48_250912145048_00064 is running\" }";
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

    private IntPtr CreateListBox(int id)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "LISTBOX",
            string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER | NativeMethods.WS_VSCROLL | NativeMethods.LBS_NOTIFY,
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
        EditControlShortcuts.AttachSelectAll(hwnd);
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
