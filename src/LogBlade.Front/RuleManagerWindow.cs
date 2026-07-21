using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal sealed class RuleManagerWindow
{
    private const int IdRulesList = 101;
    private const int IdAddButton = 201;
    private const int IdEditButton = 202;
    private const int IdRemoveButton = 203;
    private const int IdDuplicateButton = 204;
    private const int IdCloseButton = NativeMethods.IDCANCEL;
    private const int IdUpButton = 206;
    private const int IdDownButton = 207;
    private const int IdExportButton = 208;
    private const int IdImportButton = 209;
    private const int ExportSchemaVersion = 1;
    private const string ExportAppName = "LogBlade";

    private readonly List<DisplayParserRule> _rules;
    private readonly string _defaultRuleSample;
    private readonly Action<DisplayParserRule?>? _onPreviewChanged;
    private readonly Action? _onActiveRuleSelectionChanged;
    private string? _activeRuleName;
    private IntPtr _hwnd;
    private IntPtr _owner;
    private IntPtr _font;
    private GCHandle _selfHandle;
    private IntPtr _rulesList;
    private IntPtr _addButton;
    private IntPtr _editButton;
    private IntPtr _removeButton;
    private IntPtr _duplicateButton;
    private IntPtr _upButton;
    private IntPtr _downButton;
    private IntPtr _exportButton;
    private IntPtr _importButton;
    private IntPtr _closeButton;
    private bool _closed;
    private bool _updatingList;
    private bool _ruleEditorOpen;
    private DisplayParserRule? _openRuleEditorTarget;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public RuleManagerWindow(IReadOnlyList<DisplayParserRule> rules, string? activeRuleName)
        : this(rules, activeRuleName, defaultRuleSample: string.Empty, onPreviewChanged: null)
    {
    }

    public RuleManagerWindow(IReadOnlyList<DisplayParserRule> rules, string? activeRuleName, string defaultRuleSample)
        : this(rules, activeRuleName, defaultRuleSample, onPreviewChanged: null)
    {
    }

    public RuleManagerWindow(
        IReadOnlyList<DisplayParserRule> rules,
        string? activeRuleName,
        string defaultRuleSample,
        Action<DisplayParserRule?>? onPreviewChanged,
        Action? onActiveRuleSelectionChanged = null)
    {
        _rules = new List<DisplayParserRule>(rules);
        _activeRuleName = activeRuleName;
        _defaultRuleSample = defaultRuleSample;
        _onPreviewChanged = onPreviewChanged;
        _onActiveRuleSelectionChanged = onActiveRuleSelectionChanged;
    }

    public string? ShowModal(IntPtr owner)
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

            NativeMethods.MSG msg;
            while (!_closed && NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (TryHandleClipboardShortcut(msg))
                {
                    continue;
                }

                if (!NativeMethods.IsDialogMessageW(_hwnd, ref msg))
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessageW(ref msg);
                }
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

        return _activeRuleName;
    }

    private void RegisterClass()
    {
        if (s_registered)
        {
            return;
        }

        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        const string className = "LogBladeRuleManagerWindow";
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
            throw new InvalidOperationException("RegisterClassExW failed for rule manager.");
        }

        s_registered = true;
    }

    private void CreateWindow()
    {
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            "LogBladeRuleManagerWindow",
            "Configure Parser Rules",
            (NativeMethods.WS_OVERLAPPEDWINDOW & ~NativeMethods.WS_MINIMIZEBOX) | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            520,
            420,
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

            throw new InvalidOperationException("CreateWindowExW failed for rule manager.");
        }

        AppIcon.ApplyToWindow(_hwnd);
    }

    private static RuleManagerWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr ptr = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return ptr == IntPtr.Zero ? null : (RuleManagerWindow?)GCHandle.FromIntPtr(ptr).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, create.lpCreateParams);
        }

        RuleManagerWindow? self = FromHandle(hwnd);
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
                case NativeMethods.WM_SYSCOMMAND:
                    if (((int)wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE)
                    {
                        return IntPtr.Zero;
                    }

                    break;
                case NativeMethods.WM_CLOSE:
                    if (self._ruleEditorOpen)
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
        _rulesList = CreateListBox(IdRulesList);
        _addButton = CreateButton("Add", IdAddButton);
        _editButton = CreateButton("Edit", IdEditButton);
        _removeButton = CreateButton("Remove", IdRemoveButton);
        _duplicateButton = CreateButton("Duplicate", IdDuplicateButton);
        _upButton = CreateButton("Up", IdUpButton);
        _downButton = CreateButton("Down", IdDownButton);
        _exportButton = CreateButton("Export...", IdExportButton);
        _importButton = CreateButton("Import...", IdImportButton);
        _closeButton = CreateButton("Close", IdCloseButton);
        ReloadList(_activeRuleName);
        Layout();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);

        if (id == IdRulesList && notification == NativeMethods.LBN_SELCHANGE && !_updatingList)
        {
            UpdateActiveRuleFromSelection();
            return;
        }

        if (id == IdRulesList && notification == NativeMethods.LBN_DBLCLK && !_updatingList)
        {
            EditSelectedRule();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdAddButton)
        {
            AddRule();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdEditButton)
        {
            EditSelectedRule();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdRemoveButton)
        {
            RemoveSelectedRule();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdDuplicateButton)
        {
            DuplicateSelectedRule();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdUpButton)
        {
            MoveSelectedRule(-1);
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdDownButton)
        {
            MoveSelectedRule(1);
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdExportButton)
        {
            ExportRules();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdImportButton)
        {
            ImportRules();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdCloseButton)
        {
            Close();
        }
    }

    private void AddRule()
    {
        if (_ruleEditorOpen)
        {
            return;
        }

        ParserStageEditorWindow editor = new(
            _rules,
            _defaultRuleSample,
            string.Empty,
            _onPreviewChanged,
            TrySaveNewRule);
        DisplayParserRule? saved;
        _ruleEditorOpen = true;
        _openRuleEditorTarget = null;
        try
        {
            editor.ShowModal(_hwnd);
            saved = editor.SavedRule;
        }
        finally
        {
            _openRuleEditorTarget = null;
            _ruleEditorOpen = false;
        }

        if (saved is null)
        {
            PublishActiveRulePreview();
            return;
        }

        _rules.Add(saved);
        _activeRuleName = saved.Name;
        ReloadList(saved.Name);
        PublishActiveRulePreview();
    }

    private void EditSelectedRule()
    {
        if (_ruleEditorOpen)
        {
            return;
        }

        int index = GetSelectedRuleIndex();
        if (index < 0)
        {
            ShowError("Select a rule to edit.");
            return;
        }

        DisplayParserRule target = _rules[index];
        if (target.Stages.Count == 1)
        {
            EditSingleStageRule(target);
            return;
        }

        RuleEditorWindow editor = new(_rules, CopyRule(target), _onPreviewChanged);
        DisplayParserRule? saved;
        _ruleEditorOpen = true;
        _openRuleEditorTarget = target;
        try
        {
            saved = editor.ShowModal(_hwnd);
        }
        finally
        {
            _openRuleEditorTarget = null;
            _ruleEditorOpen = false;
        }

        if (saved is null)
        {
            PublishActiveRulePreview();
            return;
        }

        int currentIndex = _rules.IndexOf(target);
        if (currentIndex < 0)
        {
            ShowError("The edited rule is no longer available.");
            PublishActiveRulePreview();
            return;
        }

        bool editedActiveRule = _activeRuleName is not null &&
            string.Equals(target.Name, _activeRuleName, StringComparison.OrdinalIgnoreCase);
        string? nextActiveRuleName = editedActiveRule || _activeRuleName is null
            ? saved.Name
            : _activeRuleName;
        List<DisplayParserRule> nextRules = new(_rules);
        nextRules[currentIndex] = saved;
        if (!TrySaveRules(nextRules))
        {
            PublishActiveRulePreview();
            return;
        }

        _rules.Clear();
        _rules.AddRange(nextRules);
        _activeRuleName = nextActiveRuleName;
        ReloadList(saved.Name);
        PublishActiveRulePreview();
    }

    private void EditSingleStageRule(DisplayParserRule target)
    {
        ParserStageEditorWindow editor = new(
            _rules,
            CopyRule(target),
            _onPreviewChanged,
            savedRule => TrySaveEditedRule(target, savedRule));
        DisplayParserRule? saved;
        _ruleEditorOpen = true;
        _openRuleEditorTarget = target;
        try
        {
            editor.ShowModal(_hwnd);
            saved = editor.SavedRule;
        }
        finally
        {
            _openRuleEditorTarget = null;
            _ruleEditorOpen = false;
        }

        if (saved is null)
        {
            PublishActiveRulePreview();
            return;
        }

        ReloadList(saved.Name);
        PublishActiveRulePreview();
    }

    private void RemoveSelectedRule()
    {
        int index = GetSelectedRuleIndex();
        if (index < 0)
        {
            ShowError("Select a rule to remove.");
            return;
        }

        if (ReferenceEquals(_rules[index], _openRuleEditorTarget))
        {
            ShowError("Close the editor before removing this rule.");
            return;
        }

        string removedName = _rules[index].Name;
        int result = NativeMethods.MessageBoxW(
            _hwnd,
            "Remove rule '" + removedName + "'?",
            Program.AppTitle,
            NativeMethods.MB_YESNO | NativeMethods.MB_ICONQUESTION);
        if (result != NativeMethods.IDYES)
        {
            return;
        }

        string? nextActiveRuleName = _activeRuleName;
        if (nextActiveRuleName is not null &&
            string.Equals(nextActiveRuleName, removedName, StringComparison.OrdinalIgnoreCase))
        {
            nextActiveRuleName = null;
        }

        List<DisplayParserRule> nextRules = new(_rules);
        nextRules.RemoveAt(index);
        if (!TrySaveRules(nextRules))
        {
            return;
        }

        _rules.Clear();
        _rules.AddRange(nextRules);
        _activeRuleName = nextActiveRuleName;
        string? nextSelection = index < _rules.Count ? _rules[index].Name : _activeRuleName;
        ReloadList(nextSelection);
        PublishActiveRulePreview();
    }

    private void DuplicateSelectedRule()
    {
        int index = GetSelectedRuleIndex();
        if (index < 0)
        {
            ShowError("Select a rule to duplicate.");
            return;
        }

        DisplayParserRule duplicate = CopyRule(_rules[index]);
        duplicate.Name = CreateDuplicateName(duplicate.Name);

        List<DisplayParserRule> nextRules = new(_rules) { duplicate };
        if (!TrySaveRules(nextRules))
        {
            return;
        }

        _rules.Clear();
        _rules.AddRange(nextRules);
        _activeRuleName = duplicate.Name;
        ReloadList(duplicate.Name);
        PublishActiveRulePreview();
    }

    private void MoveSelectedRule(int direction)
    {
        int index = GetSelectedRuleIndex();
        int targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= _rules.Count)
        {
            return;
        }

        List<DisplayParserRule> nextRules = new(_rules);
        (nextRules[index], nextRules[targetIndex]) = (nextRules[targetIndex], nextRules[index]);
        if (!TrySaveRules(nextRules))
        {
            return;
        }

        _rules.Clear();
        _rules.AddRange(nextRules);
        _activeRuleName = _rules[targetIndex].Name;
        ReloadList(_activeRuleName);
        PublishActiveRulePreview();
    }

    private void ExportRules()
    {
        if (_ruleEditorOpen)
        {
            return;
        }

        if (!TryShowRulesSaveDialog(out string? path) || path is null)
        {
            return;
        }

        try
        {
            DisplayParserRulesExportPackage package = new()
            {
                SchemaVersion = ExportSchemaVersion,
                App = ExportAppName,
                ParserRules = CloneRules(_rules)
            };
            string json = JsonSerializer.Serialize(package, LogBladeJsonSerializerContext.Default.DisplayParserRulesExportPackage);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            ShowError("Failed to export parser rules: " + ex.Message);
        }
    }

    private void ImportRules()
    {
        if (_ruleEditorOpen)
        {
            return;
        }

        if (!TryShowRulesOpenDialog(out string? path) || path is null)
        {
            return;
        }

        DisplayParserRulesExportPackage? package;
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            package = JsonSerializer.Deserialize(json, LogBladeJsonSerializerContext.Default.DisplayParserRulesExportPackage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            ShowError("Failed to import parser rules: " + ex.Message);
            return;
        }

        if (!TryValidateImportedPackage(package, out string error))
        {
            ShowError(error);
            return;
        }

        List<DisplayParserRule> importedRules = CloneRules(package!.ParserRules);
        if (!TrySaveRules(importedRules))
        {
            return;
        }

        _rules.Clear();
        _rules.AddRange(importedRules);
        _activeRuleName = _rules.Count > 0 ? _rules[0].Name : null;
        ReloadList(_activeRuleName);
        PublishActiveRulePreview();
    }

    private bool TryHandleClipboardShortcut(NativeMethods.MSG msg)
    {
        if (_ruleEditorOpen ||
            msg.message != NativeMethods.WM_KEYDOWN ||
            !IsControlKeyDown() ||
            !IsMessageForThisWindow(msg.hwnd))
        {
            return false;
        }

        int key = msg.wParam.ToInt32();
        if (key == NativeMethods.VK_C)
        {
            CopySelectedRuleToClipboard();
            return true;
        }

        if (key == NativeMethods.VK_V)
        {
            PasteRulesFromClipboard();
            return true;
        }

        return false;
    }

    private bool IsMessageForThisWindow(IntPtr hwnd)
    {
        while (hwnd != IntPtr.Zero)
        {
            if (hwnd == _hwnd)
            {
                return true;
            }

            hwnd = NativeMethods.GetParent(hwnd);
        }

        return false;
    }

    private void CopySelectedRuleToClipboard()
    {
        int index = GetSelectedRuleIndex();
        if (index < 0)
        {
            ShowError("Select a rule to copy.");
            return;
        }

        string json = JsonSerializer.Serialize(_rules[index].Clone(), LogBladeJsonSerializerContext.Default.DisplayParserRule);
        if (!ClipboardText.TrySetText(_hwnd, json))
        {
            ShowError("Failed to copy parser rule to the clipboard.");
        }
    }

    private void PasteRulesFromClipboard()
    {
        if (!ClipboardText.TryGetText(_hwnd, out string text))
        {
            ShowError("Clipboard does not contain a parser rule.");
            return;
        }

        if (!TryParseClipboardRules(text, out List<DisplayParserRule> pastedRules, out string error))
        {
            ShowError(error);
            return;
        }

        if (pastedRules.Count == 0)
        {
            ShowError("Clipboard does not contain parser rules to paste.");
            return;
        }

        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _rules.Count; i++)
        {
            usedNames.Add(_rules[i].Name);
        }

        List<DisplayParserRule> nextRules = new(_rules);
        string? firstPastedName = null;
        for (int i = 0; i < pastedRules.Count; i++)
        {
            DisplayParserRule rule = pastedRules[i].Clone();
            rule.Name = CreateUniquePastedName(rule.Name, usedNames);
            firstPastedName ??= rule.Name;
            nextRules.Add(rule);
        }

        if (!TrySaveRules(nextRules))
        {
            return;
        }

        _rules.Clear();
        _rules.AddRange(nextRules);
        _activeRuleName = firstPastedName;
        ReloadList(_activeRuleName);
        PublishActiveRulePreview();
    }

    private void UpdateActiveRuleFromSelection()
    {
        int index = GetSelectedRuleIndex();
        string? nextActiveRuleName = index >= 0 ? _rules[index].Name : null;
        if (!string.Equals(_activeRuleName, nextActiveRuleName, StringComparison.OrdinalIgnoreCase))
        {
            _onActiveRuleSelectionChanged?.Invoke();
        }

        _activeRuleName = nextActiveRuleName;
        PublishActiveRulePreview();
    }

    private void PublishActiveRulePreview()
    {
        int index = FindRuleIndex(_activeRuleName);
        _onPreviewChanged?.Invoke(index >= 0 ? _rules[index].Clone() : null);
    }

    private void ReloadList(string? selectRuleName)
    {
        _updatingList = true;
        NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        for (int i = 0; i < _rules.Count; i++)
        {
            NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_ADDSTRING, IntPtr.Zero, _rules[i].Name);
        }

        int index = FindRuleIndex(selectRuleName);
        NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_SETCURSEL, new IntPtr(index), IntPtr.Zero);
        _updatingList = false;
    }

    private int GetSelectedRuleIndex()
    {
        int index = NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
        return index >= 0 && index < _rules.Count ? index : -1;
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

    private bool TrySaveRules(IReadOnlyList<DisplayParserRule> rules)
    {
        try
        {
            DisplayParserRuleStore.Save(rules);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            ShowError("Failed to save parser rules: " + ex.Message);
            return false;
        }
    }

    private string? TrySaveNewRule(DisplayParserRule rule)
    {
        List<DisplayParserRule> nextRules = new(_rules) { rule };
        try
        {
            DisplayParserRuleStore.Save(nextRules);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return "Failed to save parser rules: " + ex.Message;
        }
    }

    private string? TrySaveEditedRule(DisplayParserRule target, DisplayParserRule rule)
    {
        int currentIndex = _rules.IndexOf(target);
        if (currentIndex < 0)
        {
            return "The edited rule is no longer available.";
        }

        bool editedActiveRule = _activeRuleName is not null &&
            string.Equals(target.Name, _activeRuleName, StringComparison.OrdinalIgnoreCase);
        string? nextActiveRuleName = editedActiveRule || _activeRuleName is null
            ? rule.Name
            : _activeRuleName;
        List<DisplayParserRule> nextRules = new(_rules);
        nextRules[currentIndex] = rule;
        try
        {
            DisplayParserRuleStore.Save(nextRules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return "Failed to save parser rules: " + ex.Message;
        }

        _rules.Clear();
        _rules.AddRange(nextRules);
        _activeRuleName = nextActiveRuleName;
        return null;
    }

    private void Close()
    {
        if (_ruleEditorOpen)
        {
            return;
        }

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

        int width = Math.Max(360, client.right - client.left);
        int height = Math.Max(260, client.bottom - client.top);
        const int margin = 16;
        const int buttonWidth = 88;
        const int buttonHeight = 26;
        const int gap = 10;
        int buttonLeft = Math.Max(margin, width - margin - buttonWidth);
        int listWidth = Math.Max(160, buttonLeft - margin - gap);
        int listHeight = Math.Max(120, height - (margin * 2));

        Move(_rulesList, margin, margin, listWidth, listHeight);
        int y = margin;
        Move(_addButton, buttonLeft, y, buttonWidth, buttonHeight);
        y += buttonHeight + gap;
        Move(_editButton, buttonLeft, y, buttonWidth, buttonHeight);
        y += buttonHeight + gap;
        Move(_duplicateButton, buttonLeft, y, buttonWidth, buttonHeight);
        y += buttonHeight + gap;
        Move(_upButton, buttonLeft, y, buttonWidth, buttonHeight);
        y += buttonHeight + gap;
        Move(_downButton, buttonLeft, y, buttonWidth, buttonHeight);
        y += buttonHeight + gap;
        Move(_exportButton, buttonLeft, y, buttonWidth, buttonHeight);
        y += buttonHeight + gap;
        Move(_importButton, buttonLeft, y, buttonWidth, buttonHeight);
        y += buttonHeight + gap;
        Move(_removeButton, buttonLeft, y, buttonWidth, buttonHeight);
        Move(_closeButton, buttonLeft, height - margin - buttonHeight, buttonWidth, buttonHeight);
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

    private void SetControlFont(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && _font != IntPtr.Zero)
        {
            NativeMethods.SendMessageW(hwnd, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        }
    }

    private void ShowError(string message)
    {
        NativeMethods.MessageBoxW(_hwnd, message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
    }

    private bool TryShowRulesOpenDialog(out string? selectedPath)
    {
        selectedPath = null;
        return TryShowRulesFileDialog(save: false, out selectedPath);
    }

    private bool TryShowRulesSaveDialog(out string? selectedPath)
    {
        selectedPath = null;
        return TryShowRulesFileDialog(save: true, out selectedPath);
    }

    private bool TryShowRulesFileDialog(bool save, out string? selectedPath)
    {
        selectedPath = null;
        const int fileBufferChars = 32768;
        IntPtr filterBuffer = IntPtr.Zero;
        IntPtr fileBuffer = IntPtr.Zero;
        IntPtr titleBuffer = IntPtr.Zero;
        IntPtr defaultExtensionBuffer = IntPtr.Zero;
        try
        {
            filterBuffer = Marshal.StringToHGlobalUni("LogBlade parser rules (*.lbp)\0*.lbp\0All files\0*.*\0\0");
            titleBuffer = Marshal.StringToHGlobalUni(save ? "Export parser rules" : "Import parser rules");
            defaultExtensionBuffer = Marshal.StringToHGlobalUni("lbp");
            fileBuffer = Marshal.AllocHGlobal(fileBufferChars * sizeof(char));
            Marshal.Copy(new byte[fileBufferChars * sizeof(char)], 0, fileBuffer, fileBufferChars * sizeof(char));

            if (save)
            {
                string defaultName = "logblade-parsers.lbp";
                char[] defaultNameChars = defaultName.ToCharArray();
                Marshal.Copy(defaultNameChars, 0, fileBuffer, defaultNameChars.Length);
            }

            NativeMethods.OPENFILENAMEW ofn = new()
            {
                lStructSize = Marshal.SizeOf<NativeMethods.OPENFILENAMEW>(),
                hwndOwner = _hwnd,
                lpstrFilter = filterBuffer,
                lpstrFile = fileBuffer,
                nMaxFile = fileBufferChars,
                lpstrTitle = titleBuffer,
                lpstrDefExt = defaultExtensionBuffer,
                Flags = NativeMethods.OFN_EXPLORER | NativeMethods.OFN_PATHMUSTEXIST | NativeMethods.OFN_NOCHANGEDIR |
                    (save ? NativeMethods.OFN_OVERWRITEPROMPT : NativeMethods.OFN_FILEMUSTEXIST)
            };

            bool success = save
                ? NativeMethods.GetSaveFileNameW(ref ofn)
                : NativeMethods.GetOpenFileNameW(ref ofn);
            if (!success)
            {
                int dialogError = NativeMethods.CommDlgExtendedError();
                if (dialogError != 0)
                {
                    ShowError("Failed to open file dialog. Error: 0x" + dialogError.ToString("X"));
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

    private static void Move(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.MoveWindow(hwnd, x, y, width, height, true);
        }
    }

    private static bool IsControlKeyDown() =>
        (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & unchecked((short)0x8000)) != 0;

    private static DisplayParserRule CopyRule(DisplayParserRule source)
    {
        return source.Clone();
    }

    private static List<DisplayParserRule> CloneRules(IReadOnlyList<DisplayParserRule> rules)
    {
        List<DisplayParserRule> clones = new(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            clones.Add(rules[i].Clone());
        }

        return clones;
    }

    private bool TryValidateImportedPackage(DisplayParserRulesExportPackage? package, out string error)
    {
        if (package is null)
        {
            error = "Import file is not a valid LogBlade parser package.";
            return false;
        }

        if (package.SchemaVersion != ExportSchemaVersion || package.App != ExportAppName)
        {
            error = "Import file is not a supported LogBlade parser package.";
            return false;
        }

        if (package.ParserRules is null)
        {
            error = "Import file does not contain parser rules.";
            return false;
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < package.ParserRules.Count; i++)
        {
            DisplayParserRule? rule = package.ParserRules[i];
            if (rule is null)
            {
                error = $"Rule {i + 1}: rule is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                error = $"Rule {i + 1}: name is required.";
                return false;
            }

            if (!names.Add(rule.Name))
            {
                error = $"Rule {i + 1}: duplicate parser name '{rule.Name}'.";
                return false;
            }

            try
            {
                DisplayParserEvaluator.ValidateRule(rule);
            }
            catch (ArgumentException ex)
            {
                error = $"Rule {i + 1}: {ex.Message}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private bool TryParseClipboardRules(string text, out List<DisplayParserRule> rules, out string error)
    {
        rules = new List<DisplayParserRule>();
        error = string.Empty;
        string json = NormalizeClipboardJson(text);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Clipboard does not contain a parser rule.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("parserRules", out _))
            {
                DisplayParserRulesExportPackage? package = JsonSerializer.Deserialize(json, LogBladeJsonSerializerContext.Default.DisplayParserRulesExportPackage);
                if (!TryValidateImportedPackage(package, out error))
                {
                    return false;
                }

                rules = CloneRules(package!.ParserRules);
                return true;
            }

            DisplayParserRule? rule = JsonSerializer.Deserialize(json, LogBladeJsonSerializerContext.Default.DisplayParserRule);
            if (!TryValidateClipboardRule(rule, out error))
            {
                return false;
            }

            rules.Add(rule!.Clone());
            return true;
        }
        catch (JsonException ex)
        {
            error = "Clipboard does not contain valid parser rule JSON: " + ex.Message;
            return false;
        }
        catch (NotSupportedException ex)
        {
            error = "Clipboard does not contain supported parser rule JSON: " + ex.Message;
            return false;
        }
    }

    private static bool TryValidateClipboardRule(DisplayParserRule? rule, out string error)
    {
        if (rule is null)
        {
            error = "Clipboard does not contain a parser rule.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            error = "Parser rule name is required.";
            return false;
        }

        try
        {
            DisplayParserEvaluator.ValidateRule(rule);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string NormalizeClipboardJson(string text)
    {
        string json = text.Trim();
        while (json.EndsWith(",", StringComparison.Ordinal))
        {
            json = json[..^1].TrimEnd();
        }

        return json;
    }

    private static string CreateUniquePastedName(string name, HashSet<string> usedNames)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Rule" : name.Trim();
        string candidate = baseName;
        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        candidate = baseName + " Copy";
        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        for (int suffix = 2; ; suffix++)
        {
            candidate = baseName + " Copy " + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private string CreateDuplicateName(string name)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Rule" : name.Trim();
        string candidate = baseName + " Copy";
        if (FindRuleIndex(candidate) < 0)
        {
            return candidate;
        }

        for (int suffix = 2; ; suffix++)
        {
            candidate = baseName + " Copy " + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (FindRuleIndex(candidate) < 0)
            {
                return candidate;
            }
        }
    }

}
