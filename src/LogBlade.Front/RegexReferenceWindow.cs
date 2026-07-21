using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal sealed class RegexReferenceWindow : IDisposable
{
    private const int MinimumWidth = 520;
    private const int MinimumHeight = 420;

    internal const string ReferenceText = """
.NET REGEX PATTERNS

This syntax belongs to .NET Regex. LogBlade uses it in Search Regex, Filter Regex,
parser Regex patterns and Regex Replace patterns.

Characters and classes
  .                 Any character except a newline
  \d  \D             Digit / non-digit
  \w  \W             Word character / non-word character
  \s  \S             Whitespace / non-whitespace
  [abc]             One listed character
  [^abc]            One character not listed
  [a-z]             Character range
  \p{L}              Unicode letter
  \.                 Literal dot
  \\                 Literal backslash

Anchors and alternation
  ^                 Start of the current input
  $                 End of the current input
  \b                Word boundary
  a|b               Either a or b

Groups
  (...)             Numbered capture group
  (?<name>...)      Named capture group
  (?:...)           Non-capturing group

Quantifiers
  *                 Zero or more
  +                 One or more
  ?                 Zero or one
  {n}               Exactly n
  {n,}              At least n
  {n,m}             Between n and m
  *?  +?  ??        Lazy quantifiers

Examples
  ^ERROR\s+(?<message>.*)$
  user=(?<user>[^\s,]+)
  (?<timestamp>\d{4}-\d{2}-\d{2}T\S+)

LOGBLADE REGEX EXECUTION

These are LogBlade rules, not Regex syntax.
  Engine                    .NET Regex with NonBacktracking and CultureInvariant
  Ignore case               Controlled by the Search or Filter option
  Search / Filter input     One explicit parser line at a time
  Search captures           Displayed as result columns

Lookarounds and backreferences in patterns are not supported by NonBacktracking.

LOGBLADE OUTPUT TEMPLATES

This is LogBlade syntax, not Regex pattern syntax or the full .NET replacement language.
It is used by Regex Display, Regex Replace and JSON Template output fields.

Regex Display and Regex Replace selectors
  $0                      Entire Regex match
  $1                      Numbered Regex group
  ${name}                 Named Regex group

JSON Template selectors
  ${name}                 JSON property
  ${State.Message}        Nested JSON path

LogBlade template operations
  ${upper:name}           Uppercase value
  ${lower:name}           Lowercase value
  $$                      Literal dollar sign
  \n  \r  \t  \\  \uFFFF   Output escapes

Regex Display renders the first match. Regex Replace renders the template for every match.
JSON Template resolves selectors from the parsed JSON value.

Complete parser example
  Input             12:34:56 error Disk full
  Regex pattern     ^(?<time>\S+)\s+(?<level>\w+)\s+(?<message>.*)$
  Display template  ${time} [${upper:level}] ${message}
  Result            12:34:56 [ERROR] Disk full
""";

    private static readonly NativeMethods.WindowProc s_wndProc = WindowProc;
    private static readonly NativeMethods.WindowProc s_childProc = ChildProc;
    private static readonly IntPtr s_childProcPointer = Marshal.GetFunctionPointerForDelegate(s_childProc);
    private static readonly Dictionary<IntPtr, ChildSubclass> s_childSubclasses = new();
    private static bool s_registered;

    private readonly Action _onClosed;
    private IntPtr _hwnd;
    private IntPtr _referenceEdit;
    private IntPtr _closeButton;
    private GCHandle _selfHandle;
    private bool _registeredAuxiliary;

    public RegexReferenceWindow(Action onClosed)
    {
        _onClosed = onClosed;
    }

    public void Show(IntPtr owner)
    {
        if (_hwnd != IntPtr.Zero)
        {
            Activate();
            return;
        }

        RegisterClass();
        CreateWindow(owner);
        AuxiliaryWindowRegistry.Register(_hwnd);
        _registeredAuxiliary = true;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNORMAL);
        NativeMethods.UpdateWindow(_hwnd);
        NativeMethods.SetFocus(_referenceEdit);
    }

    public void Activate()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);
        NativeMethods.SetActiveWindow(_hwnd);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
        }
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
            hbrBackground = NativeMethods.GetSysColorBrush(NativeMethods.COLOR_3DFACE),
            lpszClassName = "LogBladeRegexReferenceWindow",
            hIconSm = AppIcon.Small
        };

        if (NativeMethods.RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException("RegisterClassExW failed for Regex reference window.");
        }

        s_registered = true;
    }

    private void CreateWindow(IntPtr owner)
    {
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            "LogBladeRegexReferenceWindow",
            "Regex reference",
            (NativeMethods.WS_OVERLAPPEDWINDOW & ~NativeMethods.WS_MINIMIZEBOX) | NativeMethods.WS_CLIPCHILDREN,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            680,
            640,
            owner,
            IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null),
            GCHandle.ToIntPtr(_selfHandle));

        if (_hwnd != IntPtr.Zero)
        {
            AppIcon.ApplyToWindow(_hwnd);
            return;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        throw new InvalidOperationException("CreateWindowExW failed for Regex reference window.");
    }

    private static RegexReferenceWindow? FromHandle(IntPtr hwnd)
    {
        IntPtr pointer = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA);
        return pointer == IntPtr.Zero
            ? null
            : (RegexReferenceWindow?)GCHandle.FromIntPtr(pointer).Target;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_NCCREATE)
        {
            NativeMethods.CREATESTRUCTW create = Marshal.PtrToStructure<NativeMethods.CREATESTRUCTW>(lParam);
            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, create.lpCreateParams);
        }

        RegexReferenceWindow? self = FromHandle(hwnd);
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
                    SetMinimumSize(lParam);
                    return IntPtr.Zero;
                case NativeMethods.WM_COMMAND:
                    if (lParam == self._closeButton && NativeMethods.HighWord(wParam) == NativeMethods.BN_CLICKED)
                    {
                        NativeMethods.DestroyWindow(hwnd);
                    }

                    return IntPtr.Zero;
                case NativeMethods.WM_SYSCOMMAND:
                    if (((int)wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE)
                    {
                        return IntPtr.Zero;
                    }

                    break;
                case NativeMethods.WM_NCDESTROY:
                    self.OnNcDestroy(hwnd);
                    break;
            }
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnCreate(IntPtr hwnd)
    {
        _hwnd = hwnd;
        IntPtr font = NativeMethods.GetStockObject(NativeMethods.DEFAULT_GUI_FONT);
        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        _referenceEdit = NativeMethods.CreateWindowExW(
            0,
            "EDIT",
            MultilineEditText.Normalize(ReferenceText),
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP |
            NativeMethods.WS_BORDER | NativeMethods.WS_HSCROLL | NativeMethods.WS_VSCROLL |
            NativeMethods.ES_MULTILINE | NativeMethods.ES_READONLY |
            NativeMethods.ES_AUTOHSCROLL | NativeMethods.ES_AUTOVSCROLL,
            0,
            0,
            1,
            1,
            hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
        _closeButton = NativeMethods.CreateWindowExW(
            0,
            "BUTTON",
            "Close",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_TABSTOP | NativeMethods.BS_PUSHBUTTON,
            0,
            0,
            1,
            1,
            hwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_referenceEdit == IntPtr.Zero || _closeButton == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed for Regex reference controls.");
        }

        NativeMethods.SendMessageW(_referenceEdit, NativeMethods.WM_SETFONT, font, new IntPtr(1));
        NativeMethods.SendMessageW(_closeButton, NativeMethods.WM_SETFONT, font, new IntPtr(1));
        AttachChildSubclass(_referenceEdit, _closeButton, selectAll: true, closeOnEnter: false);
        AttachChildSubclass(_closeButton, _referenceEdit, selectAll: false, closeOnEnter: true);
        Layout();
    }

    private void Layout()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT client);
        const int margin = 12;
        const int gap = 10;
        const int buttonWidth = 88;
        const int buttonHeight = 28;
        int closeTop = Math.Max(margin, client.bottom - margin - buttonHeight);
        NativeMethods.MoveWindow(
            _referenceEdit,
            margin,
            margin,
            Math.Max(0, client.right - (margin * 2)),
            Math.Max(0, closeTop - gap - margin),
            true);
        NativeMethods.MoveWindow(
            _closeButton,
            Math.Max(margin, client.right - margin - buttonWidth),
            closeTop,
            buttonWidth,
            buttonHeight,
            true);
    }

    private static void SetMinimumSize(IntPtr lParam)
    {
        NativeMethods.MINMAXINFO info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        info.ptMinTrackSize.x = MinimumWidth;
        info.ptMinTrackSize.y = MinimumHeight;
        Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
    }

    private void OnNcDestroy(IntPtr hwnd)
    {
        if (_registeredAuxiliary)
        {
            AuxiliaryWindowRegistry.Unregister(hwnd);
            _registeredAuxiliary = false;
        }

        NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_USERDATA, IntPtr.Zero);
        _hwnd = IntPtr.Zero;
        _referenceEdit = IntPtr.Zero;
        _closeButton = IntPtr.Zero;
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _onClosed();
    }

    private static void AttachChildSubclass(
        IntPtr hwnd,
        IntPtr tabTarget,
        bool selectAll,
        bool closeOnEnter)
    {
        IntPtr originalProc = NativeMethods.SetWindowLongPtrW(
            hwnd,
            NativeMethods.GWLP_WNDPROC,
            s_childProcPointer);
        lock (s_childSubclasses)
        {
            s_childSubclasses[hwnd] = new ChildSubclass(
                originalProc,
                tabTarget,
                selectAll,
                closeOnEnter);
        }
    }

    private static IntPtr ChildProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        ChildSubclass subclass;
        lock (s_childSubclasses)
        {
            if (!s_childSubclasses.TryGetValue(hwnd, out subclass))
            {
                return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
            }
        }

        if (msg == NativeMethods.WM_KEYDOWN)
        {
            int key = wParam.ToInt32();
            if (key == NativeMethods.VK_TAB)
            {
                NativeMethods.SetFocus(subclass.TabTarget);
                return IntPtr.Zero;
            }

            if (key == NativeMethods.VK_ESCAPE ||
                (subclass.CloseOnEnter && key == NativeMethods.VK_RETURN))
            {
                NativeMethods.PostMessageW(
                    NativeMethods.GetParent(hwnd),
                    NativeMethods.WM_CLOSE,
                    IntPtr.Zero,
                    IntPtr.Zero);
                return IntPtr.Zero;
            }

            if (subclass.SelectAll && key == NativeMethods.VK_A && IsControlKeyDown())
            {
                NativeMethods.SendMessageW(hwnd, NativeMethods.EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
                return IntPtr.Zero;
            }
        }

        if (msg == NativeMethods.WM_NCDESTROY)
        {
            lock (s_childSubclasses)
            {
                s_childSubclasses.Remove(hwnd);
            }

            NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWLP_WNDPROC, subclass.OriginalProc);
        }

        return subclass.OriginalProc == IntPtr.Zero
            ? NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam)
            : NativeMethods.CallWindowProcW(subclass.OriginalProc, hwnd, msg, wParam, lParam);
    }

    private static bool IsControlKeyDown() =>
        (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & unchecked((short)0x8000)) != 0;

    private readonly record struct ChildSubclass(
        IntPtr OriginalProc,
        IntPtr TabTarget,
        bool SelectAll,
        bool CloseOnEnter);
}
