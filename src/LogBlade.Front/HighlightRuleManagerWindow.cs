using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal sealed class HighlightRuleManagerWindow
{
    private const int IdRulesList = 101;
    private const int IdPatternEdit = 103;
    private const int IdIgnoreCase = 104;
    private const int IdEnabled = 105;
    private const int IdChooseBackgroundColor = 106;
    private const int IdChooseForegroundColor = 107;
    private const int IdInvertMatch = 108;
    private const int IdBold = 109;
    private const int IdItalic = 110;
    private const int IdAdd = 201;
    private const int IdDuplicate = 202;
    private const int IdRemove = 203;
    private const int IdUp = 204;
    private const int IdDown = 205;
    private const int IdOk = NativeMethods.IDOK;
    private const int IdCancel = NativeMethods.IDCANCEL;
    private const int IdExport = 208;
    private const int IdImport = 209;
    private const int RuleListItemHeight = 24;
    private const int ExportSchemaVersion = 1;
    private const string ExportAppName = "LogBlade";
    private const string WindowClassName = "LogBladeHighlightRuleManagerWindow";

    private readonly List<HighlightRule> _rules;
    private readonly Dictionary<int, IntPtr> _listBrushes = new();
    private readonly Action<IReadOnlyList<HighlightRule>>? _onPreviewChanged;
    private IntPtr _owner;
    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _rulesList;
    private IntPtr _addButton;
    private IntPtr _duplicateButton;
    private IntPtr _removeButton;
    private IntPtr _upButton;
    private IntPtr _downButton;
    private IntPtr _exportButton;
    private IntPtr _importButton;
    private IntPtr _patternLabel;
    private IntPtr _patternEdit;
    private IntPtr _ignoreCaseCheck;
    private IntPtr _invertMatchCheck;
    private IntPtr _enabledCheck;
    private IntPtr _boldCheck;
    private IntPtr _italicCheck;
    private IntPtr _backgroundColorLabel;
    private IntPtr _backgroundColorSwatch;
    private IntPtr _chooseBackgroundColorButton;
    private IntPtr _foregroundColorLabel;
    private IntPtr _foregroundColorSwatch;
    private IntPtr _chooseForegroundColorButton;
    private IntPtr _okButton;
    private IntPtr _cancelButton;
    private IntPtr _backgroundSwatchBrush;
    private IntPtr _foregroundSwatchBrush;
    private int _backgroundColorRef = HighlightRuleCompiler.ToColorRef(255, 242, 168);
    private int _foregroundColorRef = HighlightRuleCompiler.ToColorRef(0, 0, 0);
    private int _selectedIndex = -1;
    private GCHandle _selfHandle;
    private bool _closed;
    private bool _saved;
    private bool _updatingList;
    private bool _updatingControls;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public HighlightRuleManagerWindow(IReadOnlyList<HighlightRule> rules, Action<IReadOnlyList<HighlightRule>>? onPreviewChanged = null)
    {
        _onPreviewChanged = onPreviewChanged;
        _rules = new List<HighlightRule>(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            _rules.Add(rules[i].Clone());
        }
    }

    public bool ShowModal(IntPtr owner)
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
            while (!_closed && NativeMethods.GetMessageW(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
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

            NativeMethods.SetActiveWindow(owner);
        }

        return _saved;
    }

    private static void RegisterClass()
    {
        if (s_registered)
        {
            return;
        }

        NativeMethods.WNDCLASSEXW wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = s_wndProc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            hIcon = AppIcon.Big,
            hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_ARROW),
            hbrBackground = NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW),
            lpszClassName = WindowClassName,
            hIconSm = AppIcon.Small
        };
        if (NativeMethods.RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException("RegisterClassExW failed for highlighting rule manager.");
        }

        s_registered = true;
    }

    private void CreateWindow()
    {
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            WindowClassName,
            "Configure Highlighting",
            (NativeMethods.WS_OVERLAPPEDWINDOW & ~NativeMethods.WS_MINIMIZEBOX) | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            900,
            500,
            _owner,
            IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null),
            GCHandle.ToIntPtr(_selfHandle));
        if (_hwnd == IntPtr.Zero)
        {
            _selfHandle.Free();
            throw new InvalidOperationException("CreateWindowExW failed for highlighting rule manager.");
        }

        AppIcon.ApplyToWindow(_hwnd);
    }

    private static HighlightRuleManagerWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr pointer = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return pointer == IntPtr.Zero ? null : (HighlightRuleManagerWindow?)GCHandle.FromIntPtr(pointer).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, create.lpCreateParams);
        }

        HighlightRuleManagerWindow? self = FromHandle(hwnd);
        if (self is not null)
        {
            switch (message)
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
                case NativeMethods.WM_DRAWITEM:
                    if (self.DrawRuleItem(lParam))
                    {
                        return new IntPtr(1);
                    }

                    break;
                case NativeMethods.WM_CTLCOLORSTATIC:
                    if (lParam == self._backgroundColorSwatch && self._backgroundSwatchBrush != IntPtr.Zero)
                    {
                        return self._backgroundSwatchBrush;
                    }

                    if (lParam == self._foregroundColorSwatch && self._foregroundSwatchBrush != IntPtr.Zero)
                    {
                        return self._foregroundSwatchBrush;
                    }

                    break;
                case NativeMethods.WM_DESTROY:
                    self._closed = true;
                    return IntPtr.Zero;
                case NativeMethods.WM_NCDESTROY:
                    self.ReleaseNativeResources();
                    NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, IntPtr.Zero);
                    break;
            }
        }

        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void OnCreate(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _font = NativeMethods.GetStockObject(NativeMethods.DEFAULT_GUI_FONT);

        _rulesList = CreateControl(
            "LISTBOX",
            string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER |
                NativeMethods.WS_VSCROLL | NativeMethods.LBS_NOTIFY | NativeMethods.LBS_OWNERDRAWFIXED | NativeMethods.LBS_HASSTRINGS,
            IdRulesList);
        NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_SETITEMHEIGHT, IntPtr.Zero, new IntPtr(RuleListItemHeight));

        _addButton = CreateButton("Add", IdAdd);
        _duplicateButton = CreateButton("Duplicate", IdDuplicate);
        _removeButton = CreateButton("Remove", IdRemove);
        _upButton = CreateButton("Up", IdUp);
        _downButton = CreateButton("Down", IdDown);
        _exportButton = CreateButton("Export...", IdExport);
        _importButton = CreateButton("Import...", IdImport);

        _patternLabel = CreateLabel("Pattern");
        _patternEdit = CreateEdit(IdPatternEdit);
        _ignoreCaseCheck = CreateCheckbox("Ignore case", IdIgnoreCase);
        _invertMatchCheck = CreateCheckbox("Invert match", IdInvertMatch);
        _enabledCheck = CreateCheckbox("Enabled", IdEnabled);
        _boldCheck = CreateCheckbox("Bold", IdBold);
        _italicCheck = CreateCheckbox("Italic", IdItalic);
        _backgroundColorLabel = CreateLabel("Background");
        _backgroundColorSwatch = CreateSwatch();
        _chooseBackgroundColorButton = CreateButton("Choose...", IdChooseBackgroundColor);
        _foregroundColorLabel = CreateLabel("Text");
        _foregroundColorSwatch = CreateSwatch();
        _chooseForegroundColorButton = CreateButton("Choose...", IdChooseForegroundColor);
        _okButton = CreateButton("OK", IdOk, isDefault: true);
        _cancelButton = CreateButton("Cancel", IdCancel);

        ReloadList(_rules.Count > 0 ? 0 : -1);
        Layout();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);

        if (id == IdRulesList && notification == NativeMethods.LBN_SELCHANGE && !_updatingList)
        {
            _selectedIndex = GetSelectedIndex();
            LoadSelectedRule();
            return;
        }

        if (notification == NativeMethods.BN_CLICKED && id == IdAdd)
        {
            AddRule();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdDuplicate)
        {
            DuplicateSelectedRule();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdRemove)
        {
            RemoveSelectedRule();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdUp)
        {
            MoveSelectedRule(-1);
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdDown)
        {
            MoveSelectedRule(1);
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdExport)
        {
            ExportRules();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdImport)
        {
            ImportRules();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdChooseBackgroundColor)
        {
            ChooseBackgroundColor();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdChooseForegroundColor)
        {
            ChooseForegroundColor();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdOk)
        {
            SaveAndClose();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdCancel)
        {
            Close();
        }
        else if (!_updatingControls &&
            ((id == IdPatternEdit && notification == NativeMethods.EN_CHANGE) ||
             (notification == NativeMethods.BN_CLICKED &&
              id is IdIgnoreCase or IdInvertMatch or IdEnabled or IdBold or IdItalic)))
        {
            UpdateSelectedRuleFromControls();
        }
    }

    private void AddRule()
    {
        _rules.Add(new HighlightRule());
        ReloadList(_rules.Count - 1);
        PublishPreview();
        NativeMethods.SetFocus(_patternEdit);
    }

    private void DuplicateSelectedRule()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            ShowError("Select a rule to duplicate.");
            return;
        }

        _rules.Insert(index + 1, _rules[index].Clone());
        ReloadList(index + 1);
        PublishPreview();
    }

    private void RemoveSelectedRule()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            ShowError("Select a rule to remove.");
            return;
        }

        if (NativeMethods.MessageBoxW(_hwnd, "Remove selected rule?", Program.AppTitle, NativeMethods.MB_YESNO | NativeMethods.MB_ICONQUESTION) != NativeMethods.IDYES)
        {
            return;
        }

        _rules.RemoveAt(index);
        ReloadList(Math.Min(index, _rules.Count - 1));
        PublishPreview();
    }

    private void MoveSelectedRule(int direction)
    {
        int index = GetSelectedIndex();
        int target = index + direction;
        if (index < 0 || target < 0 || target >= _rules.Count)
        {
            return;
        }

        (_rules[index], _rules[target]) = (_rules[target], _rules[index]);
        ReloadList(target);
        PublishPreview();
    }

    private void ExportRules()
    {
        UpdateSelectedRuleFromControls();
        if (!TryShowRulesSaveDialog(out string? path) || path is null)
        {
            return;
        }

        try
        {
            HighlightRulesExportPackage package = new()
            {
                SchemaVersion = ExportSchemaVersion,
                App = ExportAppName,
                HighlightRules = CloneRules(_rules)
            };
            string json = JsonSerializer.Serialize(package, LogBladeJsonSerializerContext.Default.HighlightRulesExportPackage);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            ShowError("Failed to export highlighting rules: " + ex.Message);
        }
    }

    private void ImportRules()
    {
        if (!TryShowRulesOpenDialog(out string? path) || path is null)
        {
            return;
        }

        HighlightRulesExportPackage? package;
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            package = JsonSerializer.Deserialize(json, LogBladeJsonSerializerContext.Default.HighlightRulesExportPackage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            ShowError("Failed to import highlighting rules: " + ex.Message);
            return;
        }

        if (!TryValidateImportedPackage(package, out string error))
        {
            ShowError(error);
            return;
        }

        _rules.Clear();
        _rules.AddRange(CloneRules(package!.HighlightRules));
        ReloadList(_rules.Count > 0 ? 0 : -1);
        PublishPreview();
    }

    private void SaveAndClose()
    {
        UpdateSelectedRuleFromControls();
        for (int i = 0; i < _rules.Count; i++)
        {
            if (!HighlightRuleCompiler.TryCompile(_rules[i], out _, out string error))
            {
                ReloadList(i);
                ShowError($"Rule {i + 1}: {error}");
                return;
            }
        }

        try
        {
            HighlightRuleStore.Save(_rules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            ShowError("Failed to save highlighting rules: " + ex.Message);
            return;
        }

        _saved = true;
        Close();
    }

    private void ReloadList(int selection)
    {
        _updatingList = true;
        ClearListBrushes();
        NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        for (int i = 0; i < _rules.Count; i++)
        {
            NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_ADDSTRING, IntPtr.Zero, _rules[i].Pattern);
        }

        if (selection >= _rules.Count)
        {
            selection = _rules.Count - 1;
        }

        NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_SETCURSEL, new IntPtr(selection), IntPtr.Zero);
        _selectedIndex = selection;
        _updatingList = false;
        LoadSelectedRule();
        NativeMethods.InvalidateRect(_rulesList, IntPtr.Zero, true);
    }

    private void LoadSelectedRule()
    {
        bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _rules.Count;
        _updatingControls = true;
        try
        {
            HighlightRule rule = hasSelection ? _rules[_selectedIndex] : new HighlightRule();
            NativeMethods.SetWindowTextW(_patternEdit, hasSelection ? rule.Pattern : string.Empty);
            SetChecked(_ignoreCaseCheck, hasSelection && rule.IgnoreCase);
            SetChecked(_invertMatchCheck, hasSelection && rule.InvertMatch);
            SetChecked(_enabledCheck, hasSelection && rule.Enabled);
            SetChecked(_boldCheck, hasSelection && rule.Bold);
            SetChecked(_italicCheck, hasSelection && rule.Italic);

            _backgroundColorRef = ParseColorOrDefault(rule.BackgroundColor, HighlightRuleCompiler.ToColorRef(255, 242, 168));
            _foregroundColorRef = ParseColorOrDefault(rule.ForegroundColor, HighlightRuleCompiler.ToColorRef(0, 0, 0));
            RecreateSwatchBrush(ref _backgroundSwatchBrush, _backgroundColorRef);
            RecreateSwatchBrush(ref _foregroundSwatchBrush, _foregroundColorRef);
            NativeMethods.InvalidateRect(_backgroundColorSwatch, IntPtr.Zero, true);
            NativeMethods.InvalidateRect(_foregroundColorSwatch, IntPtr.Zero, true);
            SetEditorEnabled(hasSelection);
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void UpdateSelectedRuleFromControls()
    {
        if (_updatingControls || _selectedIndex < 0 || _selectedIndex >= _rules.Count)
        {
            return;
        }

        HighlightRule rule = _rules[_selectedIndex];
        rule.Pattern = GetText(_patternEdit);
        rule.IgnoreCase = IsChecked(_ignoreCaseCheck);
        rule.InvertMatch = IsChecked(_invertMatchCheck);
        rule.Enabled = IsChecked(_enabledCheck);
        rule.Bold = IsChecked(_boldCheck);
        rule.Italic = IsChecked(_italicCheck);
        rule.BackgroundColor = HighlightRuleCompiler.ToColorString(_backgroundColorRef);
        rule.ForegroundColor = HighlightRuleCompiler.ToColorString(_foregroundColorRef);
        NativeMethods.InvalidateRect(_rulesList, IntPtr.Zero, true);
        PublishPreview();
    }

    private void ChooseBackgroundColor()
    {
        if (_selectedIndex < 0 || !TryChooseColor(_backgroundColorRef, out int selectedColor))
        {
            return;
        }

        _backgroundColorRef = selectedColor;
        RecreateSwatchBrush(ref _backgroundSwatchBrush, _backgroundColorRef);
        NativeMethods.InvalidateRect(_backgroundColorSwatch, IntPtr.Zero, true);
        ClearListBrushes();
        UpdateSelectedRuleFromControls();
    }

    private void ChooseForegroundColor()
    {
        if (_selectedIndex < 0 || !TryChooseColor(_foregroundColorRef, out int selectedColor))
        {
            return;
        }

        _foregroundColorRef = selectedColor;
        RecreateSwatchBrush(ref _foregroundSwatchBrush, _foregroundColorRef);
        NativeMethods.InvalidateRect(_foregroundColorSwatch, IntPtr.Zero, true);
        UpdateSelectedRuleFromControls();
    }

    private bool TryChooseColor(int currentColor, out int selectedColor)
    {
        selectedColor = currentColor;
        IntPtr customColors = Marshal.AllocHGlobal(sizeof(int) * 16);
        try
        {
            for (int i = 0; i < 16; i++)
            {
                Marshal.WriteInt32(customColors, i * sizeof(int), 0x00FFFFFF);
            }

            NativeMethods.CHOOSECOLORW choice = new()
            {
                lStructSize = (uint)Marshal.SizeOf<NativeMethods.CHOOSECOLORW>(),
                hwndOwner = _hwnd,
                rgbResult = currentColor,
                lpCustColors = customColors,
                Flags = NativeMethods.CC_RGBINIT | NativeMethods.CC_FULLOPEN
            };
            if (!NativeMethods.ChooseColorW(ref choice))
            {
                return false;
            }

            selectedColor = choice.rgbResult;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(customColors);
        }
    }

    private bool DrawRuleItem(IntPtr lParam)
    {
        NativeMethods.DRAWITEMSTRUCT item = Marshal.PtrToStructure<NativeMethods.DRAWITEMSTRUCT>(lParam);
        if (item.CtlID != (uint)IdRulesList || item.itemID == uint.MaxValue || item.itemID >= (uint)_rules.Count)
        {
            return false;
        }

        HighlightRule rule = _rules[(int)item.itemID];
        int background = ParseColorOrDefault(rule.BackgroundColor, NativeMethods.GetSysColor(NativeMethods.COLOR_WINDOW));
        int foreground = ParseColorOrDefault(rule.ForegroundColor, NativeMethods.GetSysColor(NativeMethods.COLOR_WINDOWTEXT));
        NativeMethods.RECT rect = item.rcItem;
        NativeMethods.FillRect(item.hDC, ref rect, GetListBrush(background));

        int savedDc = NativeMethods.SaveDC(item.hDC);
        NativeMethods.SetBkMode(item.hDC, NativeMethods.TRANSPARENT);
        NativeMethods.SetTextColor(item.hDC, foreground);
        NativeMethods.RECT textRect = rect;
        textRect.left += 6;
        textRect.right -= 4;
        string pattern = string.IsNullOrEmpty(rule.Pattern)
            ? "(empty pattern)"
            : rule.Pattern.Replace('\r', ' ').Replace('\n', ' ');
        string text = (rule.Enabled ? "[x] " : "[ ] ") + pattern;
        NativeMethods.DrawTextW(
            item.hDC,
            text,
            text.Length,
            ref textRect,
            NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_END_ELLIPSIS | NativeMethods.DT_NOPREFIX);
        if (savedDc != 0)
        {
            NativeMethods.RestoreDC(item.hDC, savedDc);
        }

        if ((item.itemState & (NativeMethods.ODS_SELECTED | NativeMethods.ODS_FOCUS)) != 0)
        {
            NativeMethods.RECT focusRect = rect;
            focusRect.left++;
            focusRect.top++;
            focusRect.right--;
            focusRect.bottom--;
            NativeMethods.DrawFocusRect(item.hDC, ref focusRect);
        }

        return true;
    }

    private IntPtr GetListBrush(int color)
    {
        if (_listBrushes.TryGetValue(color, out IntPtr brush))
        {
            return brush;
        }

        brush = NativeMethods.CreateSolidBrush(color);
        if (brush == IntPtr.Zero)
        {
            return NativeMethods.GetSysColorBrush(NativeMethods.COLOR_WINDOW);
        }

        _listBrushes.Add(color, brush);
        return brush;
    }

    private void ClearListBrushes()
    {
        foreach (IntPtr brush in _listBrushes.Values)
        {
            if (brush != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(brush);
            }
        }

        _listBrushes.Clear();
    }

    private void SetEditorEnabled(bool enabled)
    {
        NativeMethods.EnableWindow(_patternEdit, enabled);
        NativeMethods.EnableWindow(_ignoreCaseCheck, enabled);
        NativeMethods.EnableWindow(_invertMatchCheck, enabled);
        NativeMethods.EnableWindow(_enabledCheck, enabled);
        NativeMethods.EnableWindow(_boldCheck, enabled);
        NativeMethods.EnableWindow(_italicCheck, enabled);
        NativeMethods.EnableWindow(_chooseBackgroundColorButton, enabled);
        NativeMethods.EnableWindow(_chooseForegroundColorButton, enabled);
    }

    private void Layout()
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT client))
        {
            return;
        }

        int width = Math.Max(760, client.right);
        int height = Math.Max(430, client.bottom);
        const int margin = 16;
        const int gap = 10;
        const int rowHeight = 26;
        const int labelWidth = 88;
        int footerY = height - margin - rowHeight;
        int contentBottom = footerY - gap;
        int leftWidth = Math.Clamp((width * 35) / 100, 260, 360);
        int listButtonsHeight = (rowHeight * 3) + (gap * 2);
        int listHeight = Math.Max(180, contentBottom - margin - listButtonsHeight - gap);

        Move(_rulesList, margin, margin, leftWidth, listHeight);
        int firstButtonY = margin + listHeight + gap;
        int thirdWidth = Math.Max(70, (leftWidth - (gap * 2)) / 3);
        Move(_addButton, margin, firstButtonY, thirdWidth, rowHeight);
        Move(_duplicateButton, margin + thirdWidth + gap, firstButtonY, thirdWidth, rowHeight);
        Move(_removeButton, margin + ((thirdWidth + gap) * 2), firstButtonY, leftWidth - ((thirdWidth + gap) * 2), rowHeight);
        int secondButtonY = firstButtonY + rowHeight + gap;
        int halfWidth = (leftWidth - gap) / 2;
        Move(_upButton, margin, secondButtonY, halfWidth, rowHeight);
        Move(_downButton, margin + halfWidth + gap, secondButtonY, leftWidth - halfWidth - gap, rowHeight);
        int thirdButtonY = secondButtonY + rowHeight + gap;
        Move(_exportButton, margin, thirdButtonY, halfWidth, rowHeight);
        Move(_importButton, margin + halfWidth + gap, thirdButtonY, leftWidth - halfWidth - gap, rowHeight);

        int formLeft = margin + leftWidth + 24;
        int inputLeft = formLeft + labelWidth + 12;
        int inputWidth = Math.Max(220, width - inputLeft - margin);
        int y = margin;
        Move(_patternLabel, formLeft, y + 4, labelWidth, rowHeight);
        Move(_patternEdit, inputLeft, y, inputWidth, rowHeight);
        y += rowHeight + gap;
        Move(_ignoreCaseCheck, inputLeft, y, 112, rowHeight);
        Move(_invertMatchCheck, inputLeft + 120, y, 112, rowHeight);
        Move(_enabledCheck, inputLeft + 240, y, 90, rowHeight);
        y += rowHeight + gap;
        Move(_boldCheck, inputLeft, y, 80, rowHeight);
        Move(_italicCheck, inputLeft + 88, y, 80, rowHeight);
        y += rowHeight + gap;
        Move(_backgroundColorLabel, formLeft, y + 4, labelWidth, rowHeight);
        Move(_backgroundColorSwatch, inputLeft, y + 2, 72, rowHeight - 4);
        Move(_chooseBackgroundColorButton, inputLeft + 84, y, 96, rowHeight);
        y += rowHeight + gap;
        Move(_foregroundColorLabel, formLeft, y + 4, labelWidth, rowHeight);
        Move(_foregroundColorSwatch, inputLeft, y + 2, 72, rowHeight - 4);
        Move(_chooseForegroundColorButton, inputLeft + 84, y, 96, rowHeight);

        Move(_cancelButton, width - margin - 90, footerY, 90, rowHeight);
        Move(_okButton, width - margin - 190, footerY, 90, rowHeight);
    }

    private static void SetMinimumWindowSize(IntPtr lParam)
    {
        NativeMethods.MINMAXINFO info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        info.ptMinTrackSize.x = 780;
        info.ptMinTrackSize.y = 460;
        Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
    }

    private void ReleaseNativeResources()
    {
        ClearListBrushes();
        DeleteBrush(ref _backgroundSwatchBrush);
        DeleteBrush(ref _foregroundSwatchBrush);
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _hwnd = IntPtr.Zero;
    }

    private int GetSelectedIndex()
    {
        int index = NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
        return index >= 0 && index < _rules.Count ? index : -1;
    }

    private IntPtr CreateLabel(string text) => CreateControl("STATIC", text, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE, 0);

    private IntPtr CreateSwatch() => CreateControl("STATIC", string.Empty, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_BORDER, 0);

    private IntPtr CreateEdit(int id)
    {
        IntPtr hwnd = CreateControl("EDIT", string.Empty, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER | NativeMethods.ES_AUTOHSCROLL, id);
        EditControlShortcuts.AttachSelectAll(hwnd);
        return hwnd;
    }

    private IntPtr CreateCheckbox(string text, int id) => CreateControl("BUTTON", text, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTOCHECKBOX, id);

    private IntPtr CreateButton(string text, int id, bool isDefault = false) =>
        CreateControl(
            "BUTTON",
            text,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP |
                (isDefault ? NativeMethods.BS_DEFPUSHBUTTON : NativeMethods.BS_PUSHBUTTON),
            id);

    private IntPtr CreateControl(string className, string text, int style, int id)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(0, className, text, style, 0, 0, 10, 10, _hwnd, new IntPtr(id), NativeMethods.GetModuleHandleW(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for highlighting manager control.");
        }

        NativeMethods.SendMessageW(hwnd, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        return hwnd;
    }

    private static int ParseColorOrDefault(string? value, int fallback)
    {
        return HighlightRuleCompiler.TryParseColor(value, out int red, out int green, out int blue)
            ? HighlightRuleCompiler.ToColorRef(red, green, blue)
            : fallback;
    }

    private static void RecreateSwatchBrush(ref IntPtr brush, int color)
    {
        DeleteBrush(ref brush);
        brush = NativeMethods.CreateSolidBrush(color);
    }

    private static void DeleteBrush(ref IntPtr brush)
    {
        if (brush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(brush);
            brush = IntPtr.Zero;
        }
    }

    private static void SetChecked(IntPtr hwnd, bool value) => NativeMethods.SendMessageW(hwnd, NativeMethods.BM_SETCHECK, new IntPtr(value ? NativeMethods.BST_CHECKED : NativeMethods.BST_UNCHECKED), IntPtr.Zero);

    private static bool IsChecked(IntPtr hwnd) => NativeMethods.SendMessageW(hwnd, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32() == NativeMethods.BST_CHECKED;

    private static string GetText(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLengthW(hwnd);
        StringBuilder value = new(length + 1);
        NativeMethods.GetWindowTextW(hwnd, value, value.Capacity);
        return value.ToString();
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
            filterBuffer = Marshal.StringToHGlobalUni("LogBlade highlighting rules (*.lbh)\0*.lbh\0All files\0*.*\0\0");
            titleBuffer = Marshal.StringToHGlobalUni(save ? "Export highlighting rules" : "Import highlighting rules");
            defaultExtensionBuffer = Marshal.StringToHGlobalUni("lbh");
            fileBuffer = Marshal.AllocHGlobal(fileBufferChars * sizeof(char));
            Marshal.Copy(new byte[fileBufferChars * sizeof(char)], 0, fileBuffer, fileBufferChars * sizeof(char));

            if (save)
            {
                string defaultName = "logblade-highlighting.lbh";
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

    private static bool TryValidateImportedPackage(HighlightRulesExportPackage? package, out string error)
    {
        if (package is null)
        {
            error = "Import file is not a valid LogBlade highlighting package.";
            return false;
        }

        if (package.SchemaVersion != ExportSchemaVersion || package.App != ExportAppName)
        {
            error = "Import file is not a supported LogBlade highlighting package.";
            return false;
        }

        if (package.HighlightRules is null)
        {
            error = "Import file does not contain highlighting rules.";
            return false;
        }

        for (int i = 0; i < package.HighlightRules.Count; i++)
        {
            HighlightRule? rule = package.HighlightRules[i];
            if (rule is null)
            {
                error = $"Rule {i + 1}: rule is missing.";
                return false;
            }

            if (!HighlightRuleCompiler.TryCompile(rule, out _, out string ruleError))
            {
                error = $"Rule {i + 1}: {ruleError}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static List<HighlightRule> CloneRules(IReadOnlyList<HighlightRule> rules)
    {
        List<HighlightRule> clones = new(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            clones.Add(rules[i].Clone());
        }

        return clones;
    }

    private static void Move(IntPtr hwnd, int x, int y, int width, int height) => NativeMethods.MoveWindow(hwnd, x, y, width, height, true);

    private void PublishPreview() => _onPreviewChanged?.Invoke(_rules);

    private void ShowError(string message) => NativeMethods.MessageBoxW(_hwnd, message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);

    private void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
        }
    }
}
