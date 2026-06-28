using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class HighlightRuleEditorWindow
{
    private const int IdNameEdit = 101;
    private const int IdModeCombo = 102;
    private const int IdPatternEdit = 103;
    private const int IdIgnoreCase = 104;
    private const int IdEnabled = 105;
    private const int IdChooseBackgroundColor = 106;
    private const int IdChooseForegroundColor = 107;
    private const int IdSave = 201;
    private const int IdCancel = 202;
    private const string WindowClassName = "LogBladeHighlightRuleEditorWindow";

    private readonly IReadOnlyList<HighlightRule> _existingRules;
    private readonly HighlightRule? _initialRule;
    private readonly string? _originalName;
    private IntPtr _owner;
    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _nameLabel;
    private IntPtr _nameEdit;
    private IntPtr _modeLabel;
    private IntPtr _modeCombo;
    private IntPtr _patternLabel;
    private IntPtr _patternEdit;
    private IntPtr _ignoreCaseCheck;
    private IntPtr _enabledCheck;
    private IntPtr _backgroundColorLabel;
    private IntPtr _backgroundColorSwatch;
    private IntPtr _chooseBackgroundColorButton;
    private IntPtr _foregroundColorLabel;
    private IntPtr _foregroundColorSwatch;
    private IntPtr _chooseForegroundColorButton;
    private IntPtr _saveButton;
    private IntPtr _cancelButton;
    private IntPtr _backgroundSwatchBrush;
    private IntPtr _foregroundSwatchBrush;
    private int _backgroundColorRef = HighlightRuleCompiler.ToColorRef(255, 242, 168);
    private int _foregroundColorRef = HighlightRuleCompiler.ToColorRef(0, 0, 0);
    private GCHandle _selfHandle;
    private bool _closed;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public HighlightRuleEditorWindow(IReadOnlyList<HighlightRule> existingRules, HighlightRule? initialRule = null)
    {
        _existingRules = existingRules;
        _initialRule = initialRule?.Clone();
        _originalName = initialRule?.Name;
    }

    public HighlightRule? SavedRule { get; private set; }

    public HighlightRule? ShowModal(IntPtr owner)
    {
        _owner = owner;
        RegisterClass();
        CreateWindow();
        NativeMethods.EnableWindow(owner, false);
        try
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWDEFAULT);
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
            NativeMethods.EnableWindow(owner, true);
            NativeMethods.SetActiveWindow(owner);
        }

        return SavedRule;
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
            throw new InvalidOperationException("RegisterClassExW failed for highlighting rule editor.");
        }

        s_registered = true;
    }

    private void CreateWindow()
    {
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            WindowClassName,
            _initialRule is null ? "Add Highlighting Rule" : "Edit Highlighting Rule",
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            590,
            370,
            _owner,
            IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null),
            GCHandle.ToIntPtr(_selfHandle));
        if (_hwnd == IntPtr.Zero)
        {
            _selfHandle.Free();
            throw new InvalidOperationException("CreateWindowExW failed for highlighting rule editor.");
        }

        AppIcon.ApplyToWindow(_hwnd);
    }

    private static HighlightRuleEditorWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr pointer = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return pointer == IntPtr.Zero ? null : (HighlightRuleEditorWindow?)GCHandle.FromIntPtr(pointer).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, create.lpCreateParams);
        }

        HighlightRuleEditorWindow? self = FromHandle(hwnd);
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
                case NativeMethods.WM_COMMAND:
                    self.OnCommand(wParam);
                    return IntPtr.Zero;
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
        _nameLabel = CreateLabel("Name");
        _nameEdit = CreateEdit(IdNameEdit);
        _modeLabel = CreateLabel("Type");
        _modeCombo = CreateCombo(IdModeCombo);
        NativeMethods.SendMessageW(_modeCombo, NativeMethods.CB_ADDSTRING, IntPtr.Zero, "Text");
        NativeMethods.SendMessageW(_modeCombo, NativeMethods.CB_ADDSTRING, IntPtr.Zero, "Regex");
        _patternLabel = CreateLabel("Pattern");
        _patternEdit = CreateEdit(IdPatternEdit);
        _ignoreCaseCheck = CreateCheckbox("Ignore case", IdIgnoreCase);
        _enabledCheck = CreateCheckbox("Enabled", IdEnabled);
        _backgroundColorLabel = CreateLabel("Background");
        _backgroundColorSwatch = CreateSwatch();
        _chooseBackgroundColorButton = CreateButton("Choose...", IdChooseBackgroundColor);
        _foregroundColorLabel = CreateLabel("Text");
        _foregroundColorSwatch = CreateSwatch();
        _chooseForegroundColorButton = CreateButton("Choose...", IdChooseForegroundColor);
        _saveButton = CreateButton("Save", IdSave);
        _cancelButton = CreateButton("Cancel", IdCancel);

        HighlightRule initial = _initialRule ?? new HighlightRule();
        NativeMethods.SetWindowTextW(_nameEdit, initial.Name);
        NativeMethods.SetWindowTextW(_patternEdit, initial.Pattern);
        NativeMethods.SendMessageW(_modeCombo, NativeMethods.CB_SETCURSEL, new IntPtr(initial.Mode == HighlightMatchMode.Regex ? 1 : 0), IntPtr.Zero);
        SetChecked(_ignoreCaseCheck, initial.IgnoreCase);
        SetChecked(_enabledCheck, initial.Enabled);
        if (HighlightRuleCompiler.TryParseColor(initial.BackgroundColor, out int backgroundRed, out int backgroundGreen, out int backgroundBlue))
        {
            _backgroundColorRef = HighlightRuleCompiler.ToColorRef(backgroundRed, backgroundGreen, backgroundBlue);
        }

        if (HighlightRuleCompiler.TryParseColor(initial.ForegroundColor, out int foregroundRed, out int foregroundGreen, out int foregroundBlue))
        {
            _foregroundColorRef = HighlightRuleCompiler.ToColorRef(foregroundRed, foregroundGreen, foregroundBlue);
        }

        RecreateSwatchBrush(ref _backgroundSwatchBrush, _backgroundColorRef);
        RecreateSwatchBrush(ref _foregroundSwatchBrush, _foregroundColorRef);
        Layout();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);
        if (notification == NativeMethods.BN_CLICKED && id == IdChooseBackgroundColor)
        {
            ChooseBackgroundColor();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdChooseForegroundColor)
        {
            ChooseForegroundColor();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdSave)
        {
            Save();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdCancel)
        {
            Close();
        }
    }

    private void ChooseBackgroundColor()
    {
        if (TryChooseColor(_backgroundColorRef, out int selectedColor))
        {
            _backgroundColorRef = selectedColor;
            RecreateSwatchBrush(ref _backgroundSwatchBrush, _backgroundColorRef);
            NativeMethods.InvalidateRect(_backgroundColorSwatch, IntPtr.Zero, true);
        }
    }

    private void ChooseForegroundColor()
    {
        if (TryChooseColor(_foregroundColorRef, out int selectedColor))
        {
            _foregroundColorRef = selectedColor;
            RecreateSwatchBrush(ref _foregroundSwatchBrush, _foregroundColorRef);
            NativeMethods.InvalidateRect(_foregroundColorSwatch, IntPtr.Zero, true);
        }
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

    private void Save()
    {
        string name = GetText(_nameEdit).Trim();
        string pattern = GetText(_patternEdit);
        if (name.Length == 0)
        {
            ShowError("Name is required.");
            return;
        }

        for (int i = 0; i < _existingRules.Count; i++)
        {
            bool current = _originalName is not null && string.Equals(_existingRules[i].Name, _originalName, StringComparison.OrdinalIgnoreCase);
            if (!current && string.Equals(_existingRules[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                ShowError("A rule with this name already exists.");
                return;
            }
        }

        HighlightRule rule = new()
        {
            Name = name,
            Enabled = IsChecked(_enabledCheck),
            Mode = NativeMethods.SendMessageW(_modeCombo, NativeMethods.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32() == 1
                ? HighlightMatchMode.Regex
                : HighlightMatchMode.Text,
            Pattern = pattern,
            IgnoreCase = IsChecked(_ignoreCaseCheck),
            BackgroundColor = HighlightRuleCompiler.ToColorString(_backgroundColorRef),
            ForegroundColor = HighlightRuleCompiler.ToColorString(_foregroundColorRef)
        };
        if (!HighlightRuleCompiler.TryCompile(rule, out _, out string error))
        {
            ShowError(error);
            return;
        }

        SavedRule = rule;
        Close();
    }

    private void Layout()
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT client))
        {
            return;
        }

        int width = Math.Max(520, client.right);
        const int margin = 16;
        const int labelWidth = 78;
        const int rowHeight = 26;
        const int gap = 12;
        int inputLeft = margin + labelWidth + gap;
        int inputWidth = Math.Max(260, width - inputLeft - margin);
        int y = margin;
        Move(_nameLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_nameEdit, inputLeft, y, inputWidth, rowHeight);
        y += rowHeight + gap;
        Move(_modeLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_modeCombo, inputLeft, y, 160, 180);
        y += rowHeight + gap;
        Move(_patternLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_patternEdit, inputLeft, y, inputWidth, rowHeight);
        y += rowHeight + gap;
        Move(_ignoreCaseCheck, inputLeft, y, 120, rowHeight);
        Move(_enabledCheck, inputLeft + 140, y, 100, rowHeight);
        y += rowHeight + gap;
        Move(_backgroundColorLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_backgroundColorSwatch, inputLeft, y + 2, 72, rowHeight - 4);
        Move(_chooseBackgroundColorButton, inputLeft + 84, y, 96, rowHeight);
        y += rowHeight + gap;
        Move(_foregroundColorLabel, margin, y + 4, labelWidth, rowHeight);
        Move(_foregroundColorSwatch, inputLeft, y + 2, 72, rowHeight - 4);
        Move(_chooseForegroundColorButton, inputLeft + 84, y, 96, rowHeight);
        int buttonY = Math.Max(y + rowHeight + gap, client.bottom - margin - rowHeight);
        Move(_cancelButton, width - margin - 90, buttonY, 90, rowHeight);
        Move(_saveButton, width - margin - 190, buttonY, 90, rowHeight);
    }

    private static void RecreateSwatchBrush(ref IntPtr brush, int color)
    {
        if (brush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(brush);
        }

        brush = NativeMethods.CreateSolidBrush(color);
    }

    private void ReleaseNativeResources()
    {
        if (_backgroundSwatchBrush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_backgroundSwatchBrush);
            _backgroundSwatchBrush = IntPtr.Zero;
        }

        if (_foregroundSwatchBrush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_foregroundSwatchBrush);
            _foregroundSwatchBrush = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _hwnd = IntPtr.Zero;
    }

    private IntPtr CreateLabel(string text) => CreateControl("STATIC", text, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE, 0);

    private IntPtr CreateSwatch() => CreateControl("STATIC", string.Empty, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_BORDER, 0);

    private IntPtr CreateEdit(int id)
    {
        IntPtr hwnd = CreateControl("EDIT", string.Empty, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER | NativeMethods.ES_AUTOHSCROLL, id);
        EditControlShortcuts.AttachSelectAll(hwnd);
        return hwnd;
    }

    private IntPtr CreateCombo(int id) => CreateControl("COMBOBOX", string.Empty, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.CBS_DROPDOWNLIST | NativeMethods.CBS_HASSTRINGS, id);

    private IntPtr CreateCheckbox(string text, int id) => CreateControl("BUTTON", text, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_AUTOCHECKBOX, id);

    private IntPtr CreateButton(string text, int id) => CreateControl("BUTTON", text, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_PUSHBUTTON, id);

    private IntPtr CreateControl(string className, string text, int style, int id)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(0, className, text, style, 0, 0, 10, 10, _hwnd, new IntPtr(id), NativeMethods.GetModuleHandleW(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for highlighting rule control.");
        }

        NativeMethods.SendMessageW(hwnd, NativeMethods.WM_SETFONT, _font, new IntPtr(1));
        return hwnd;
    }

    private static void Move(IntPtr hwnd, int x, int y, int width, int height) => NativeMethods.MoveWindow(hwnd, x, y, width, height, true);

    private static void SetChecked(IntPtr hwnd, bool value) => NativeMethods.SendMessageW(hwnd, NativeMethods.BM_SETCHECK, new IntPtr(value ? NativeMethods.BST_CHECKED : NativeMethods.BST_UNCHECKED), IntPtr.Zero);

    private static bool IsChecked(IntPtr hwnd) => NativeMethods.SendMessageW(hwnd, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32() == NativeMethods.BST_CHECKED;

    private static string GetText(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLengthW(hwnd);
        StringBuilder value = new(length + 1);
        NativeMethods.GetWindowTextW(hwnd, value, value.Capacity);
        return value.ToString();
    }

    private void ShowError(string message) => NativeMethods.MessageBoxW(_hwnd, message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);

    private void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
        }
    }
}
