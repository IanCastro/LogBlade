using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class ParserStageEditorWindow
{
    private const int IdRegexMode = 101;
    private const int IdJsonMode = 102;
    private const int IdRegexReplaceMode = 103;
    private const int IdFilterMode = 109;
    private const int IdRuleEdit = 104;
    private const int IdTemplateEdit = 105;
    private const int IdPreviewEdit = 106;
    private const int IdBeforeEdit = 107;
    private const int IdNameEdit = 108;
    private const int IdSaveButton = 201;
    private const int IdCancelButton = 202;
    private const int IdAddNewStageButton = 203;
    private const int IdUndoStageButton = 204;
    private const int IdFilterRegex = 110;
    private const int IdFilterIgnoreCase = 111;
    private const int IdFilterInvertMatch = 112;

    private readonly DisplayParserStage? _initialStage;
    private readonly IReadOnlyList<DisplayParserRule>? _existingRules;
    private readonly string? _originalRuleName;
    private readonly List<DisplayParserStage> _previousStages;
    private string _sampleText;
    private readonly string? _initialRuleName;
    private readonly Dictionary<DisplayParserMode, ParserStageDraft> _drafts = new();
    private readonly Action<DisplayParserStage?>? _onPreviewChanged;
    private readonly Action<DisplayParserRule?>? _onRulePreviewChanged;
    private readonly Func<DisplayParserRule, string?>? _saveCreatedRule;
    private readonly bool _createRuleMode;
    private IntPtr _hwnd;
    private IntPtr _owner;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private IntPtr _nameLabel;
    private IntPtr _nameEdit;
    private IntPtr _typeLabel;
    private IntPtr _regexRadio;
    private IntPtr _jsonRadio;
    private IntPtr _regexReplaceRadio;
    private IntPtr _filterRadio;
    private IntPtr _ruleLabel;
    private IntPtr _ruleEdit;
    private IntPtr _templateLabel;
    private IntPtr _templateEdit;
    private IntPtr _filterRegexCheckbox;
    private IntPtr _filterIgnoreCaseCheckbox;
    private IntPtr _filterInvertMatchCheckbox;
    private IntPtr _beforeLabel;
    private IntPtr _beforeEdit;
    private IntPtr _previewLabel;
    private IntPtr _previewEdit;
    private IntPtr _saveButton;
    private IntPtr _addNewStageButton;
    private IntPtr _undoStageButton;
    private IntPtr _cancelButton;
    private DisplayParserMode _mode;
    private bool _closed;
    private bool _updatingModeControls;
    private bool _updatingPreview;

    private bool CanEditStageInput => _previousStages.Count == 0;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public ParserStageEditorWindow()
        : this(initialStage: null, Array.Empty<DisplayParserStage>(), string.Empty, stageIndex: 0, onPreviewChanged: null)
    {
    }

    public ParserStageEditorWindow(DisplayParserStage? initialStage)
        : this(initialStage, Array.Empty<DisplayParserStage>(), string.Empty, stageIndex: 0, onPreviewChanged: null)
    {
    }

    public ParserStageEditorWindow(IReadOnlyList<DisplayParserStage> stages, string sample, int stageIndex)
        : this(GetInitialStage(stages, stageIndex), stages, sample, stageIndex, onPreviewChanged: null)
    {
    }

    public ParserStageEditorWindow(
        IReadOnlyList<DisplayParserStage> stages,
        string sample,
        int stageIndex,
        Action<DisplayParserStage?>? onPreviewChanged)
        : this(GetInitialStage(stages, stageIndex), stages, sample, stageIndex, onPreviewChanged)
    {
    }

    public ParserStageEditorWindow(
        IReadOnlyList<DisplayParserRule> existingRules,
        string sample,
        string initialRuleName,
        Action<DisplayParserRule?>? onRulePreviewChanged,
        Func<DisplayParserRule, string?> saveCreatedRule)
        : this(
            initialStage: null,
            stages: Array.Empty<DisplayParserStage>(),
            sample,
            stageIndex: 0,
            onPreviewChanged: null,
            existingRules,
            originalRuleName: null,
            initialRuleName,
            onRulePreviewChanged,
            saveCreatedRule)
    {
    }

    public ParserStageEditorWindow(
        IReadOnlyList<DisplayParserRule> existingRules,
        DisplayParserRule initialRule,
        Action<DisplayParserRule?>? onRulePreviewChanged,
        Func<DisplayParserRule, string?> saveRule)
        : this(
            initialStage: initialRule.Stages.Count == 0 ? null : initialRule.Stages[0].Clone(),
            stages: initialRule.Stages,
            sample: initialRule.Sample,
            stageIndex: 0,
            onPreviewChanged: null,
            existingRules,
            originalRuleName: initialRule.Name,
            initialRuleName: initialRule.Name,
            onRulePreviewChanged,
            saveRule)
    {
    }

    private ParserStageEditorWindow(
        DisplayParserStage? initialStage,
        IReadOnlyList<DisplayParserStage> stages,
        string sample,
        int stageIndex,
        Action<DisplayParserStage?>? onPreviewChanged)
        : this(
            initialStage,
            stages,
            sample,
            stageIndex,
            onPreviewChanged,
            existingRules: null,
            originalRuleName: null,
            initialRuleName: null,
            onRulePreviewChanged: null,
            saveCreatedRule: null)
    {
    }

    private ParserStageEditorWindow(
        DisplayParserStage? initialStage,
        IReadOnlyList<DisplayParserStage> stages,
        string sample,
        int stageIndex,
        Action<DisplayParserStage?>? onPreviewChanged,
        IReadOnlyList<DisplayParserRule>? existingRules,
        string? originalRuleName,
        string? initialRuleName,
        Action<DisplayParserRule?>? onRulePreviewChanged,
        Func<DisplayParserRule, string?>? saveCreatedRule)
    {
        _initialStage = initialStage;
        _mode = initialStage?.Mode ?? DisplayParserMode.Json;
        _existingRules = existingRules;
        _originalRuleName = originalRuleName;
        _sampleText = sample;
        _initialRuleName = initialRuleName;
        _previousStages = ClonePreviousStages(stages, stageIndex);
        _onPreviewChanged = onPreviewChanged;
        _onRulePreviewChanged = onRulePreviewChanged;
        _saveCreatedRule = saveCreatedRule;
        _createRuleMode = existingRules is not null;
        if (initialStage is not null)
        {
            _drafts[initialStage.Mode] = new ParserStageDraft(
                initialStage.Rule,
                initialStage.Template,
                initialStage.UseRegex,
                initialStage.IgnoreCase,
                initialStage.InvertMatch);
        }
    }

    public DisplayParserStage? SavedStage { get; private set; }
    public DisplayParserRule? SavedRule { get; private set; }

    public DisplayParserStage? ShowModal(IntPtr owner)
    {
        _owner = owner;
        RegisterClass();
        CreateWindow();
        IntPtr modalHwnd = _hwnd;
        bool registered = false;

        try
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWDEFAULT);
            AuxiliaryWindowRegistry.Register(modalHwnd);
            registered = true;
            NativeMethods.UpdateWindow(_hwnd);
            if (_createRuleMode && _nameEdit != IntPtr.Zero)
            {
                NativeMethods.SetFocus(_nameEdit);
            }

            NativeMethods.MSG msg;
            while (!_closed && NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }
        }
        finally
        {
            if (registered)
            {
                AuxiliaryWindowRegistry.Unregister(modalHwnd);
            }

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
        const string className = "LogBladeParserStageEditorWindow";
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
            throw new InvalidOperationException("RegisterClassExW failed for parser stage editor.");
        }

        s_registered = true;
    }

    private void CreateWindow()
    {
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            "LogBladeParserStageEditorWindow",
            _initialStage is null ? "Add Parser Stage" : "Edit Parser Stage",
            (NativeMethods.WS_OVERLAPPEDWINDOW & ~NativeMethods.WS_MINIMIZEBOX) | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            720,
            _createRuleMode ? 720 : 680,
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

        AppIcon.ApplyToWindow(_hwnd);
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
                case NativeMethods.WM_GETMINMAXINFO:
                    SetMinimumWindowSize(lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_COMMAND:
                    self.OnCommand(wParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_SYSCOMMAND:
                    if (((int)wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE)
                    {
                        return IntPtr.Zero;
                    }

                    break;
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

        if (_createRuleMode)
        {
            _nameLabel = CreateLabel("Nome");
            _nameEdit = CreateEdit(IdNameEdit, multiline: false, readOnly: false);
            NativeMethods.SetWindowTextW(_nameEdit, _initialRuleName ?? string.Empty);
        }

        _typeLabel = CreateLabel("Tipo");
        _regexRadio = CreateRadio("Regex", IdRegexMode);
        _regexReplaceRadio = CreateRadio("Regex Replace", IdRegexReplaceMode);
        _jsonRadio = CreateRadio("JSON", IdJsonMode);
        _filterRadio = CreateRadio("Filter", IdFilterMode);
        _ruleLabel = CreateLabel("Regra");
        _ruleEdit = CreateEdit(IdRuleEdit, multiline: true, readOnly: false);
        _templateLabel = CreateLabel("Display");
        _templateEdit = CreateEdit(IdTemplateEdit, multiline: true, readOnly: false);
        _filterRegexCheckbox = CreateCheckbox("Regex", IdFilterRegex);
        _filterIgnoreCaseCheckbox = CreateCheckbox("Ignore case", IdFilterIgnoreCase);
        _filterInvertMatchCheckbox = CreateCheckbox("Invert match", IdFilterInvertMatch);
        _beforeLabel = CreateLabel("Before");
        _beforeEdit = CreateEdit(IdBeforeEdit, multiline: true, readOnly: !CanEditStageInput);
        _previewLabel = CreateLabel("Preview");
        _previewEdit = CreateEdit(IdPreviewEdit, multiline: true, readOnly: true);
        if (CanEditStageInput)
        {
            NativeMethods.SetWindowTextW(_beforeEdit, _sampleText);
        }

        _saveButton = CreateButton("Save", IdSaveButton);
        if (_createRuleMode)
        {
            _addNewStageButton = CreateButton("Add New Stage", IdAddNewStageButton);
            _undoStageButton = CreateButton("Undo Stage", IdUndoStageButton);
        }

        _cancelButton = CreateButton("Cancel", IdCancelButton);

        SetButtonChecked(_regexRadio, _mode == DisplayParserMode.Regex);
        SetButtonChecked(_regexReplaceRadio, _mode == DisplayParserMode.RegexReplace);
        SetButtonChecked(_jsonRadio, _mode == DisplayParserMode.Json);
        SetButtonChecked(_filterRadio, _mode == DisplayParserMode.Filter);
        LoadModeDraft(_mode);

        UpdateModeUi();
        UpdateUndoStageButton();
        UpdatePreview();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);

        if (notification == NativeMethods.BN_CLICKED && id is IdRegexMode or IdJsonMode or IdRegexReplaceMode or IdFilterMode)
        {
            DisplayParserMode nextMode = id switch
            {
                IdRegexMode => DisplayParserMode.Regex,
                IdRegexReplaceMode => DisplayParserMode.RegexReplace,
                IdFilterMode => DisplayParserMode.Filter,
                _ => DisplayParserMode.Json
            };
            if (nextMode == _mode)
            {
                return;
            }

            SaveCurrentModeDraft();
            _mode = nextMode;
            SetButtonChecked(_regexRadio, _mode == DisplayParserMode.Regex);
            SetButtonChecked(_regexReplaceRadio, _mode == DisplayParserMode.RegexReplace);
            SetButtonChecked(_jsonRadio, _mode == DisplayParserMode.Json);
            SetButtonChecked(_filterRadio, _mode == DisplayParserMode.Filter);
            LoadModeDraft(_mode);
            UpdateModeUi();
            UpdatePreview();
            return;
        }

        if (!_updatingModeControls &&
            notification == NativeMethods.BN_CLICKED &&
            id is IdFilterRegex or IdFilterIgnoreCase or IdFilterInvertMatch)
        {
            UpdatePreview();
            return;
        }

        if (!_updatingModeControls &&
            notification == NativeMethods.EN_CHANGE &&
            id is IdRuleEdit or IdTemplateEdit)
        {
            UpdatePreview();
            return;
        }

        if (!_updatingModeControls &&
            notification == NativeMethods.EN_CHANGE &&
            id == IdBeforeEdit &&
            CanEditStageInput)
        {
            UpdatePreview();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdSaveButton)
        {
            SaveStage();
            return;
        }

        if (_createRuleMode && notification == NativeMethods.BN_CLICKED && id == IdAddNewStageButton)
        {
            AddNewStage();
            return;
        }

        if (_createRuleMode && notification == NativeMethods.BN_CLICKED && id == IdUndoStageButton)
        {
            UndoStage();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdCancelButton)
        {
            Close();
        }
    }

    private void SaveStage()
    {
        if (_createRuleMode)
        {
            SaveCreatedRule();
            return;
        }

        DisplayParserStage stage = CreateStageFromControls();

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

    private void SaveCreatedRule()
    {
        if (!TryCreateRuleFromControls(out DisplayParserRule? rule))
        {
            return;
        }

        DisplayParserRule createdRule = rule!;
        string? error = _saveCreatedRule?.Invoke(createdRule);
        if (!string.IsNullOrEmpty(error))
        {
            ShowError(error);
            return;
        }

        SavedRule = createdRule;
        Close();
    }

    private void AddNewStage()
    {
        if (!TryCreateValidatedStage(out DisplayParserStage? stage))
        {
            return;
        }

        _sampleText = GetStageInput();
        _previousStages.Add(stage!.Clone());
        UpdateBeforeEditReadOnly();
        _drafts.Clear();
        _mode = DisplayParserMode.Json;
        SetButtonChecked(_regexRadio, false);
        SetButtonChecked(_regexReplaceRadio, false);
        SetButtonChecked(_jsonRadio, true);
        LoadModeDraft(_mode);
        UpdateModeUi();
        UpdateUndoStageButton();
        UpdatePreview();
    }

    private void UndoStage()
    {
        if (_previousStages.Count == 0)
        {
            return;
        }

        DisplayParserStage stage = _previousStages[^1];
        _previousStages.RemoveAt(_previousStages.Count - 1);
        UpdateBeforeEditReadOnly();
        if (CanEditStageInput)
        {
            NativeMethods.SetWindowTextW(_beforeEdit, _sampleText);
        }

        LoadStageIntoControls(stage);
        UpdateUndoStageButton();
        UpdatePreview();
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

        int width = Math.Max(_createRuleMode ? 620 : 520, client.right - client.left);
        int height = Math.Max(560, client.bottom - client.top);
        const int margin = 16;
        const int labelWidth = 78;
        const int rowHeight = 26;
        const int gap = 10;
        int inputLeft = margin + labelWidth + 12;
        int inputWidth = Math.Max(300, width - inputLeft - margin);
        int y = margin;
        bool hasTemplate = _mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace;
        bool isFilter = _mode == DisplayParserMode.Filter;
        int ruleHeight = hasTemplate ? 72 : 90;
        int templateHeight = 56;

        if (_createRuleMode)
        {
            Move(_nameLabel, margin, y + 4, labelWidth, rowHeight);
            Move(_nameEdit, inputLeft, y, inputWidth, rowHeight);
            y += rowHeight + gap;
        }

        Move(_typeLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_regexRadio, inputLeft, y, 76, rowHeight);
        Move(_regexReplaceRadio, inputLeft + 84, y, 126, rowHeight);
        Move(_jsonRadio, inputLeft + 218, y, 88, rowHeight);
        Move(_filterRadio, inputLeft + 306, y, 80, rowHeight);

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


        if (isFilter)
        {
            Move(_filterRegexCheckbox, inputLeft, y, 76, rowHeight);
            Move(_filterIgnoreCaseCheckbox, inputLeft + 84, y, 112, rowHeight);
            Move(_filterInvertMatchCheckbox, inputLeft + 204, y, 116, rowHeight);
            y += rowHeight + gap;
        }
        else
        {
            Move(_filterRegexCheckbox, 0, 0, 0, 0);
            Move(_filterIgnoreCaseCheckbox, 0, 0, 0, 0);
            Move(_filterInvertMatchCheckbox, 0, 0, 0, 0);
        }

        int availablePreviewHeight = Math.Max(144, height - y - (gap * 2) - rowHeight - margin);
        int beforeHeight = availablePreviewHeight / 2;
        int previewHeight = availablePreviewHeight - beforeHeight;

        Move(_beforeLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_beforeEdit, inputLeft, y, inputWidth, beforeHeight);
        y += beforeHeight + gap;

        Move(_previewLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_previewEdit, inputLeft, y, inputWidth, previewHeight);
        y += previewHeight + gap;

        Move(_cancelButton, width - margin - 90, y, 90, rowHeight);
        if (_createRuleMode)
        {
            Move(_addNewStageButton, width - margin - 220, y, 120, rowHeight);
            Move(_undoStageButton, width - margin - 320, y, 90, rowHeight);
            Move(_saveButton, width - margin - 420, y, 90, rowHeight);
        }
        else
        {
            Move(_saveButton, width - margin - 190, y, 90, rowHeight);
        }
    }

    private static void SetMinimumWindowSize(IntPtr lParam)
    {
        NativeMethods.MINMAXINFO info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        info.ptMinTrackSize.x = 660;
        info.ptMinTrackSize.y = 600;
        Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
    }

    private void SaveCurrentModeDraft()
    {
        _drafts[_mode] = new ParserStageDraft(
            GetWindowText(_ruleEdit),
            GetWindowText(_templateEdit),
            IsButtonChecked(_filterRegexCheckbox),
            IsButtonChecked(_filterIgnoreCaseCheckbox),
            IsButtonChecked(_filterInvertMatchCheckbox));
    }

    private void LoadModeDraft(DisplayParserMode mode)
    {
        if (!_drafts.TryGetValue(mode, out ParserStageDraft draft))
        {
            draft = CreateDefaultDraft(mode);
            _drafts.Add(mode, draft);
        }

        _updatingModeControls = true;
        try
        {
            NativeMethods.SetWindowTextW(_ruleEdit, draft.Rule);
            NativeMethods.SetWindowTextW(_templateEdit, draft.Template);
            SetButtonChecked(_filterRegexCheckbox, draft.UseRegex);
            SetButtonChecked(_filterIgnoreCaseCheckbox, draft.IgnoreCase);
            SetButtonChecked(_filterInvertMatchCheckbox, draft.InvertMatch);
        }
        finally
        {
            _updatingModeControls = false;
        }
    }

    private void LoadStageIntoControls(DisplayParserStage stage)
    {
        _drafts.Clear();
        _drafts[stage.Mode] = new ParserStageDraft(
            stage.Rule,
            stage.Template,
            stage.UseRegex,
            stage.IgnoreCase,
            stage.InvertMatch);
        _mode = stage.Mode;
        SetButtonChecked(_regexRadio, _mode == DisplayParserMode.Regex);
        SetButtonChecked(_regexReplaceRadio, _mode == DisplayParserMode.RegexReplace);
        SetButtonChecked(_jsonRadio, _mode == DisplayParserMode.Json);
        SetButtonChecked(_filterRadio, _mode == DisplayParserMode.Filter);
        LoadModeDraft(_mode);
        UpdateModeUi();
    }

    private void UpdateUndoStageButton()
    {
        if (!_createRuleMode || _undoStageButton == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.EnableWindow(_undoStageButton, _previousStages.Count > 0);
    }

    private void UpdateBeforeEditReadOnly()
    {
        if (_beforeEdit != IntPtr.Zero)
        {
            NativeMethods.SendMessageW(_beforeEdit, NativeMethods.EM_SETREADONLY, new IntPtr(CanEditStageInput ? 0 : 1), IntPtr.Zero);
        }
    }

    private ParserStageDraft CreateDefaultDraft(DisplayParserMode mode)
    {
        if (mode == DisplayParserMode.Regex)
        {
            return new ParserStageDraft("(.*)", "{0}", false, false, false);
        }

        if (mode == DisplayParserMode.RegexReplace)
        {
            return new ParserStageDraft(@"\u0001", "|", false, false, false);
        }

        if (mode == DisplayParserMode.Filter)
        {
            return new ParserStageDraft(string.Empty, string.Empty, true, false, false);
        }

        string generatedTemplate = DisplayParserEvaluator.GenerateJsonTemplateFromSample(GetStageInput());
        return new ParserStageDraft(
            generatedTemplate.Length == 0
                ? "key1 - {key1}, key2 - {key2}"
                : generatedTemplate,
            string.Empty,
            false,
            false,
            false);
    }

    private void UpdateModeUi()
    {
        bool hasTemplate = _mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace;
        bool isFilter = _mode == DisplayParserMode.Filter;
        NativeMethods.SetWindowTextW(_ruleLabel, _mode switch
        {
            DisplayParserMode.Json => "Template",
            DisplayParserMode.Filter => "Pattern",
            _ => "Regex"
        });
        NativeMethods.SetWindowTextW(_templateLabel, _mode == DisplayParserMode.RegexReplace ? "Replacement" : "Display");
        NativeMethods.ShowWindow(_templateLabel, hasTemplate ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        NativeMethods.ShowWindow(_templateEdit, hasTemplate ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        NativeMethods.ShowWindow(_filterRegexCheckbox, isFilter ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        NativeMethods.ShowWindow(_filterIgnoreCaseCheckbox, isFilter ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        NativeMethods.ShowWindow(_filterInvertMatchCheckbox, isFilter ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
        Layout();
    }

    private void UpdatePreview()
    {
        if (_updatingPreview || _beforeEdit == IntPtr.Zero || _previewEdit == IntPtr.Zero)
        {
            return;
        }

        _updatingPreview = true;
        try
        {
            string input = GetStageInput();
            if (!CanEditStageInput)
            {
                NativeMethods.SetWindowTextW(_beforeEdit, input);
            }

            DisplayParserStage stage = CreateStageFromControls();
            try
            {
                DisplayParserEvaluator.ValidateStage(stage);
            }
            catch (ArgumentException ex)
            {
                NativeMethods.SetWindowTextW(_previewEdit, ex.Message);
                PublishInvalidPreview();
                return;
            }

            string output = EvaluateStagePreview(input, stage, HasPreviousFilter);
            NativeMethods.SetWindowTextW(_previewEdit, output);
            PublishValidPreview(stage);
        }
        finally
        {
            _updatingPreview = false;
        }
    }

    private DisplayParserStage CreateStageFromControls()
    {
        string rawRule = GetWindowText(_ruleEdit);
        string rawTemplate = GetWindowText(_templateEdit);

        return new DisplayParserStage
        {
            Mode = _mode,
            Rule = rawRule,
            Template = _mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace ? rawTemplate : string.Empty,
            UseRegex = _mode == DisplayParserMode.Filter && IsButtonChecked(_filterRegexCheckbox),
            IgnoreCase = _mode == DisplayParserMode.Filter && IsButtonChecked(_filterIgnoreCaseCheckbox),
            InvertMatch = _mode == DisplayParserMode.Filter && IsButtonChecked(_filterInvertMatchCheckbox)
        };
    }

    private bool TryCreateRuleFromControls(out DisplayParserRule? rule)
    {
        rule = null;
        string name = GetWindowText(_nameEdit).Trim();
        if (name.Length == 0)
        {
            ShowError("Name is required.");
            return false;
        }

        if (_existingRules is not null)
        {
            for (int i = 0; i < _existingRules.Count; i++)
            {
                bool isCurrentRule = _originalRuleName is not null &&
                    string.Equals(_existingRules[i].Name, _originalRuleName, StringComparison.OrdinalIgnoreCase);
                if (!isCurrentRule && string.Equals(_existingRules[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    ShowError("A rule with this name already exists.");
                    return false;
                }
            }
        }

        if (!TryCreateValidatedStage(out DisplayParserStage? currentStage))
        {
            return false;
        }

        List<DisplayParserStage> stages = CloneStages(_previousStages);
        stages.Add(currentStage!.Clone());
        rule = new DisplayParserRule
        {
            Name = name,
            Stages = stages,
            Sample = GetRuleSample()
        };

        try
        {
            DisplayParserEvaluator.ValidateRule(rule);
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            rule = null;
            return false;
        }

        return true;
    }

    private bool TryCreateValidatedStage(out DisplayParserStage? stage)
    {
        stage = CreateStageFromControls();
        try
        {
            DisplayParserEvaluator.ValidateStage(stage);
            return true;
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            stage = null;
            return false;
        }
    }

    private void PublishInvalidPreview()
    {
        if (_createRuleMode)
        {
            _onRulePreviewChanged?.Invoke(null);
        }
        else
        {
            _onPreviewChanged?.Invoke(null);
        }
    }

    private void PublishValidPreview(DisplayParserStage stage)
    {
        if (!_createRuleMode)
        {
            _onPreviewChanged?.Invoke(stage.Clone());
            return;
        }

        List<DisplayParserStage> stages = CloneStages(_previousStages);
        stages.Add(stage.Clone());
        _onRulePreviewChanged?.Invoke(new DisplayParserRule
        {
            Name = GetWindowText(_nameEdit),
            Stages = stages,
            Sample = GetRuleSample()
        });
    }

    private static string EvaluateLines(string text, DisplayParserRule rule)
    {
        return DisplayParserEvaluator.EvaluateLinesOrOriginal(rule, text);
    }

    internal static string EvaluateStagePreview(
        string input,
        DisplayParserStage stage,
        bool hasPreviousFilter)
    {
        var previewRule = new DisplayParserRule
        {
            Stages = new List<DisplayParserStage> { stage.Clone() }
        };
        return hasPreviousFilter && stage.Mode != DisplayParserMode.Filter
            ? DisplayParserEvaluator.EvaluateExplicitLinesIndependently(previewRule, input)
            : EvaluateLines(input, previewRule);
    }

    private bool HasPreviousFilter
    {
        get
        {
            for (int i = 0; i < _previousStages.Count; i++)
            {
                if (_previousStages[i].Mode == DisplayParserMode.Filter)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private string GetStageInput() =>
        CanEditStageInput
            ? GetEditableStageInput()
            : EvaluateLines(_sampleText, new DisplayParserRule { Stages = CloneStages(_previousStages) });

    private string GetRuleSample() =>
        CanEditStageInput ? GetEditableStageInput() : _sampleText;

    private string GetEditableStageInput() =>
        _beforeEdit == IntPtr.Zero ? _sampleText : GetWindowText(_beforeEdit);

    private static DisplayParserStage? GetInitialStage(IReadOnlyList<DisplayParserStage> stages, int stageIndex)
    {
        return stageIndex >= 0 && stageIndex < stages.Count
            ? stages[stageIndex].Clone()
            : null;
    }

    private static List<DisplayParserStage> ClonePreviousStages(IReadOnlyList<DisplayParserStage> stages, int stageIndex)
    {
        int count = Math.Clamp(stageIndex, 0, stages.Count);
        List<DisplayParserStage> previous = new(count);
        for (int i = 0; i < count; i++)
        {
            previous.Add(stages[i].Clone());
        }

        return previous;
    }

    private static List<DisplayParserStage> CloneStages(IReadOnlyList<DisplayParserStage> stages)
    {
        List<DisplayParserStage> copy = new(stages.Count);
        for (int i = 0; i < stages.Count; i++)
        {
            copy.Add(stages[i].Clone());
        }

        return copy;
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

    private IntPtr CreateCheckbox(string text, int id)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            text,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTOCHECKBOX,
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

    private static void SetButtonChecked(IntPtr hwnd, bool checkedState)
    {
        NativeMethods.SendMessageW(
            hwnd,
            NativeMethods.BM_SETCHECK,
            new IntPtr(checkedState ? NativeMethods.BST_CHECKED : NativeMethods.BST_UNCHECKED),
            IntPtr.Zero);
    }

    private static bool IsButtonChecked(IntPtr hwnd) =>
        NativeMethods.SendMessageW(hwnd, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32() == NativeMethods.BST_CHECKED;

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

    private readonly record struct ParserStageDraft(
        string Rule,
        string Template,
        bool UseRegex,
        bool IgnoreCase,
        bool InvertMatch);
}
