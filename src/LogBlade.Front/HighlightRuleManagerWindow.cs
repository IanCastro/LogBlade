using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

internal sealed class HighlightRuleManagerWindow
{
    private const int IdRulesList = 101;
    private const int IdAdd = 201;
    private const int IdEdit = 202;
    private const int IdDuplicate = 203;
    private const int IdRemove = 204;
    private const int IdUp = 205;
    private const int IdDown = 206;
    private const int IdClose = 207;
    private const string WindowClassName = "LogBladeHighlightRuleManagerWindow";

    private readonly List<HighlightRule> _rules;
    private IntPtr _owner;
    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _rulesList;
    private IntPtr _addButton;
    private IntPtr _editButton;
    private IntPtr _duplicateButton;
    private IntPtr _removeButton;
    private IntPtr _upButton;
    private IntPtr _downButton;
    private IntPtr _closeButton;
    private GCHandle _selfHandle;
    private bool _closed;
    private bool _changed;

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static bool s_registered;

    public HighlightRuleManagerWindow(IReadOnlyList<HighlightRule> rules)
    {
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

        return _changed;
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
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            620,
            460,
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

                    self._hwnd = IntPtr.Zero;
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
        _rulesList = CreateControl("LISTBOX", string.Empty, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.WS_BORDER | NativeMethods.WS_VSCROLL | NativeMethods.LBS_NOTIFY, IdRulesList);
        _addButton = CreateButton("Add", IdAdd);
        _editButton = CreateButton("Edit", IdEdit);
        _duplicateButton = CreateButton("Duplicate", IdDuplicate);
        _removeButton = CreateButton("Remove", IdRemove);
        _upButton = CreateButton("Up", IdUp);
        _downButton = CreateButton("Down", IdDown);
        _closeButton = CreateButton("Close", IdClose);
        ReloadList(-1);
        Layout();
    }

    private void OnCommand(IntPtr wParam)
    {
        int id = NativeMethods.LowWord(wParam);
        int notification = NativeMethods.HighWord(wParam);
        if (id == IdRulesList && notification == NativeMethods.LBN_DBLCLK)
        {
            EditSelected();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdAdd)
        {
            Add();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdEdit)
        {
            EditSelected();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdDuplicate)
        {
            DuplicateSelected();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdRemove)
        {
            RemoveSelected();
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdUp)
        {
            MoveSelected(-1);
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdDown)
        {
            MoveSelected(1);
        }
        else if (notification == NativeMethods.BN_CLICKED && id == IdClose)
        {
            Close();
        }
    }

    private void Add()
    {
        HighlightRuleEditorWindow editor = new(_rules);
        HighlightRule? rule = editor.ShowModal(_hwnd);
        if (rule is null)
        {
            return;
        }

        List<HighlightRule> next = CloneRules();
        next.Add(rule);
        ApplySavedRules(next, next.Count - 1);
    }

    private void EditSelected()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            ShowError("Select a rule to edit.");
            return;
        }

        HighlightRuleEditorWindow editor = new(_rules, _rules[index]);
        HighlightRule? rule = editor.ShowModal(_hwnd);
        if (rule is null)
        {
            return;
        }

        List<HighlightRule> next = CloneRules();
        next[index] = rule;
        ApplySavedRules(next, index);
    }

    private void DuplicateSelected()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            ShowError("Select a rule to duplicate.");
            return;
        }

        List<HighlightRule> next = CloneRules();
        HighlightRule duplicate = _rules[index].Clone();
        duplicate.Name = CreateDuplicateName(duplicate.Name);
        next.Insert(index + 1, duplicate);
        ApplySavedRules(next, index + 1);
    }

    private void RemoveSelected()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            ShowError("Select a rule to remove.");
            return;
        }

        if (NativeMethods.MessageBoxW(_hwnd, "Remove rule '" + _rules[index].Name + "'?", Program.AppTitle, NativeMethods.MB_YESNO | NativeMethods.MB_ICONQUESTION) != NativeMethods.IDYES)
        {
            return;
        }

        List<HighlightRule> next = CloneRules();
        next.RemoveAt(index);
        ApplySavedRules(next, Math.Min(index, next.Count - 1));
    }

    private void MoveSelected(int direction)
    {
        int index = GetSelectedIndex();
        int target = index + direction;
        if (index < 0 || target < 0 || target >= _rules.Count)
        {
            return;
        }

        List<HighlightRule> next = CloneRules();
        (next[index], next[target]) = (next[target], next[index]);
        ApplySavedRules(next, target);
    }

    private void ApplySavedRules(List<HighlightRule> next, int selection)
    {
        try
        {
            HighlightRuleStore.Save(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            ShowError("Failed to save highlighting rules: " + ex.Message);
            return;
        }

        _rules.Clear();
        _rules.AddRange(next);
        _changed = true;
        ReloadList(selection);
    }

    private List<HighlightRule> CloneRules()
    {
        List<HighlightRule> copy = new(_rules.Count);
        for (int i = 0; i < _rules.Count; i++)
        {
            copy.Add(_rules[i].Clone());
        }

        return copy;
    }

    private void ReloadList(int selection)
    {
        NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        for (int i = 0; i < _rules.Count; i++)
        {
            HighlightRule rule = _rules[i];
            string label = $"[{(rule.Enabled ? 'x' : ' ')}] {rule.Name} ({rule.Mode})";
            NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_ADDSTRING, IntPtr.Zero, label);
        }

        NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_SETCURSEL, new IntPtr(selection), IntPtr.Zero);
    }

    private int GetSelectedIndex()
    {
        int index = NativeMethods.SendMessageW(_rulesList, NativeMethods.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
        return index >= 0 && index < _rules.Count ? index : -1;
    }

    private string CreateDuplicateName(string name)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Rule" : name.Trim();
        string candidate = baseName + " Copy";
        for (int suffix = 2; ContainsName(candidate); suffix++)
        {
            candidate = baseName + " Copy " + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private bool ContainsName(string name)
    {
        for (int i = 0; i < _rules.Count; i++)
        {
            if (string.Equals(_rules[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void Layout()
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT client))
        {
            return;
        }

        int width = Math.Max(420, client.right);
        int height = Math.Max(320, client.bottom);
        const int margin = 16;
        const int buttonWidth = 94;
        const int buttonHeight = 26;
        const int gap = 10;
        int buttonLeft = width - margin - buttonWidth;
        Move(_rulesList, margin, margin, Math.Max(220, buttonLeft - margin - gap), height - (margin * 2));
        IntPtr[] buttons = [_addButton, _editButton, _duplicateButton, _removeButton, _upButton, _downButton];
        int y = margin;
        for (int i = 0; i < buttons.Length; i++)
        {
            Move(buttons[i], buttonLeft, y, buttonWidth, buttonHeight);
            y += buttonHeight + gap;
        }

        Move(_closeButton, buttonLeft, height - margin - buttonHeight, buttonWidth, buttonHeight);
    }

    private IntPtr CreateButton(string text, int id) => CreateControl("BUTTON", text, NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_PUSHBUTTON, id);

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

    private static void Move(IntPtr hwnd, int x, int y, int width, int height) => NativeMethods.MoveWindow(hwnd, x, y, width, height, true);

    private void ShowError(string message) => NativeMethods.MessageBoxW(_hwnd, message, Program.AppTitle, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);

    private void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
        }
    }
}
