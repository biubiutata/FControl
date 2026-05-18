using System.Diagnostics;
using System.Runtime.InteropServices;
using FControl.Models;

namespace FControl.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int FirstHotKeyId = 0x4600;
    private const int FirstCustomHotKeyId = 0x4700;
    private const int WmHotKey = 0x0312;
    private const uint NoModifiers = 0x0000;
    private const nuint SubclassId = 2;
    private const int VirtualKeyF1 = 0x70;
    private const int VirtualKeyF12 = 0x7B;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int LlkhfInjected = 0x00000010;

    private readonly nint _hwnd;
    private readonly AppConfigurationService _configurationService;
    private readonly SubclassProc _subclassProc;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly Dictionary<int, HotkeyRegistration> _registeredMappings = [];
    private readonly Dictionary<int, KeyMappingConfig> _hookMappings = [];
    private nint _keyboardHook;
    private bool _disposed;

    public GlobalHotKeyService(nint hwnd, AppConfigurationService configurationService)
    {
        _hwnd = hwnd;
        _configurationService = configurationService;
        _subclassProc = WindowSubclassProc;
        _keyboardProc = LowLevelKeyboardHookProc;

        if (!SetWindowSubclass(_hwnd, _subclassProc, SubclassId, 0))
        {
            throw new InvalidOperationException("Unable to attach global hotkey window subclass.");
        }

        Refresh();
    }

    public event EventHandler<IReadOnlyList<HotKeyRegistrationFailure>>? RegistrationChanged;
    public event EventHandler<HotKeyTriggeredEventArgs>? HotKeyTriggered;
    public event EventHandler<CustomHotkeyTriggeredEventArgs>? CustomHotkeyTriggered;

    public IReadOnlyList<HotKeyRegistrationFailure> LastRegistrationFailures { get; private set; } = [];

    public IReadOnlyList<HotkeyConflict> ValidateCustomHotkeys(IEnumerable<CustomHotkeyConfig> hotkeys)
    {
        UnregisterAll();
        try
        {
            return ValidateCustomHotkeysCore(hotkeys);
        }
        finally
        {
            Refresh();
        }
    }

    private IReadOnlyList<HotkeyConflict> ValidateCustomHotkeysCore(IEnumerable<CustomHotkeyConfig> hotkeys)
    {
        var conflicts = new List<HotkeyConflict>();
        var enabledHotkeys = hotkeys.Where(static hotkey => hotkey.Enabled).Select(static hotkey => hotkey.Clone()).ToList();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hotkey in enabledHotkeys)
        {
            if (!HotkeyParser.TryParse(hotkey.Hotkey, out var definition, out var parseError))
            {
                conflicts.Add(new HotkeyConflict(hotkey.Id, hotkey.Hotkey, parseError));
                continue;
            }

            hotkey.Hotkey = definition.DisplayText;
            if (HotkeyParser.IsReserved(definition, out var reservedReason))
            {
                conflicts.Add(new HotkeyConflict(hotkey.Id, hotkey.Hotkey, reservedReason));
            }

            if (seen.TryGetValue(definition.DisplayText, out var existingName))
            {
                conflicts.Add(new HotkeyConflict(hotkey.Id, hotkey.Hotkey, $"与“{existingName}”重复。"));
            }
            else
            {
                seen[definition.DisplayText] = hotkey.Name;
            }

            foreach (var mapping in _configurationService.Current.KeyMappings.Where(static mapping => mapping.Action != HotKeyAction.Disabled))
            {
                var functionNumber = GetFunctionKeyNumber(mapping.Key);
                if (functionNumber is null || definition.Modifiers != 0)
                {
                    continue;
                }

                if (definition.VirtualKey == VirtualKeyF1 + functionNumber.Value - 1)
                {
                    conflicts.Add(new HotkeyConflict(hotkey.Id, hotkey.Hotkey, $"与 {mapping.Key} 按键映射冲突。"));
                }
            }

            if (!conflicts.Any(conflict => conflict.HotkeyId == hotkey.Id) && !HotkeyParser.TryRegisterForProbe(_hwnd, definition, out var errorCode))
            {
                conflicts.Add(new HotkeyConflict(hotkey.Id, hotkey.Hotkey, $"该快捷键可能已被系统或其他应用占用（Win32 错误 {errorCode}）。"));
            }
        }

        return conflicts;
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UnregisterAll();

        var failures = new List<HotKeyRegistrationFailure>();
        var forceCompatibilityMode = _configurationService.Current.CompatibilityModeEnabled;
        foreach (var mapping in _configurationService.Current.KeyMappings)
        {
            if (mapping.Action == HotKeyAction.Disabled)
            {
                continue;
            }

            var functionKeyNumber = GetFunctionKeyNumber(mapping.Key);
            if (functionKeyNumber is null)
            {
                continue;
            }

            var hotKeyId = FirstHotKeyId + functionKeyNumber.Value;
            var virtualKey = VirtualKeyF1 + functionKeyNumber.Value - 1;
            if (forceCompatibilityMode)
            {
                _hookMappings[virtualKey] = mapping.Clone();
                continue;
            }

            if (RegisterHotKey(_hwnd, hotKeyId, NoModifiers, (uint)virtualKey))
            {
                _registeredMappings[hotKeyId] = HotkeyRegistration.ForKeyMapping(mapping.Clone());
                continue;
            }

            var errorCode = Marshal.GetLastWin32Error();
            _hookMappings[virtualKey] = mapping.Clone();
            failures.Add(new HotKeyRegistrationFailure(mapping.Key, errorCode, true));
        }

        var customIndex = 0;
        foreach (var hotkey in _configurationService.Current.CustomHotkeys.Where(static hotkey => hotkey.Enabled))
        {
            if (!HotkeyParser.TryParse(hotkey.Hotkey, out var definition, out var parseError))
            {
                failures.Add(new HotKeyRegistrationFailure(hotkey.Hotkey, 0, false, parseError));
                continue;
            }

            if (HotkeyParser.IsReserved(definition, out var reservedReason))
            {
                failures.Add(new HotKeyRegistrationFailure(definition.DisplayText, 0, false, reservedReason));
                continue;
            }

            var hotKeyId = FirstCustomHotKeyId + customIndex++;
            if (RegisterHotKey(_hwnd, hotKeyId, definition.Modifiers | HotkeyParser.ModNoRepeat, definition.VirtualKey))
            {
                var clone = hotkey.Clone();
                clone.Hotkey = definition.DisplayText;
                _registeredMappings[hotKeyId] = HotkeyRegistration.ForCustomHotkey(clone);
                continue;
            }

            failures.Add(new HotKeyRegistrationFailure(definition.DisplayText, Marshal.GetLastWin32Error()));
        }

        if (_hookMappings.Count > 0)
        {
            EnsureKeyboardHook();
        }

        LastRegistrationFailures = failures;
        RegistrationChanged?.Invoke(this, LastRegistrationFailures);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        _ = RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
        _disposed = true;
    }

    private nint WindowSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (msg == WmHotKey && _registeredMappings.TryGetValue(unchecked((int)wParam), out var registration))
        {
            if (registration.KeyMapping is not null)
            {
                Trigger(registration.KeyMapping, "RegisterHotKey");
                return 0;
            }

            if (registration.CustomHotkey is not null)
            {
                Trigger(registration.CustomHotkey, "RegisterHotKey");
                return 0;
            }
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private nint LowLevelKeyboardHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown || wParam == WmKeyUp || wParam == WmSysKeyUp))
        {
            var hook = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var virtualKey = unchecked((int)hook.vkCode);
            if (virtualKey is >= VirtualKeyF1 and <= VirtualKeyF12 &&
                (hook.flags & LlkhfInjected) == 0 &&
                _hookMappings.TryGetValue(virtualKey, out var mapping))
            {
                if (wParam == WmKeyDown || wParam == WmSysKeyDown)
                {
                    Trigger(mapping, "WH_KEYBOARD_LL fallback");
                }

                return 1;
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void Trigger(KeyMappingConfig mapping, string source)
    {
        Debug.WriteLine($"FControl hotkey ({source}): {mapping.Key} -> {mapping.Action}");
        AppServices.Log.Info($"热键触发（{source}）：{mapping.Key} -> {mapping.Action}");
        HotKeyTriggered?.Invoke(this, new HotKeyTriggeredEventArgs(mapping.Clone()));
    }

    private void Trigger(CustomHotkeyConfig hotkey, string source)
    {
        Debug.WriteLine($"FControl custom hotkey ({source}): {hotkey.Hotkey} -> {hotkey.Name}");
        AppServices.Log.Info($"自定义快捷键触发（{source}）：{hotkey.Hotkey} -> {hotkey.Name}");
        CustomHotkeyTriggered?.Invoke(this, new CustomHotkeyTriggeredEventArgs(hotkey.Clone()));
    }

    private void EnsureKeyboardHook()
    {
        if (_keyboardHook != 0)
        {
            return;
        }

        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
        if (_keyboardHook == 0)
        {
            var errorCode = Marshal.GetLastWin32Error();
            foreach (var mapping in _hookMappings.Values)
            {
                Debug.WriteLine($"FControl keyboard hook failed for {mapping.Key}: {errorCode}");
            }

            _hookMappings.Clear();
        }
    }

    private void UnregisterAll()
    {
        foreach (var hotKeyId in _registeredMappings.Keys)
        {
            _ = UnregisterHotKey(_hwnd, hotKeyId);
        }

        _registeredMappings.Clear();
        _hookMappings.Clear();

        if (_keyboardHook != 0)
        {
            _ = UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private static int? GetFunctionKeyNumber(string key)
    {
        if (key.Length < 2 || key[0] != 'F')
        {
            return null;
        }

        if (!int.TryParse(key[1..], out var functionKeyNumber))
        {
            return null;
        }

        return functionKeyNumber is >= 1 and <= 12 ? functionKeyNumber : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public int flags;
        public uint time;
        public nint dwExtraInfo;
    }

    private sealed record HotkeyRegistration(KeyMappingConfig? KeyMapping, CustomHotkeyConfig? CustomHotkey)
    {
        public static HotkeyRegistration ForKeyMapping(KeyMappingConfig mapping)
        {
            return new HotkeyRegistration(mapping, null);
        }

        public static HotkeyRegistration ForCustomHotkey(CustomHotkeyConfig hotkey)
        {
            return new HotkeyRegistration(null, hotkey);
        }
    }

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);
    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}

public sealed record HotKeyRegistrationFailure(string Key, int ErrorCode, bool FallbackEnabled = false, string? CustomMessage = null)
{
    public string Message
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomMessage))
            {
                return $"{Key}：{CustomMessage}";
            }

            return ErrorCode switch
            {
                1409 when FallbackEnabled => $"{Key} 已被其他应用占用，已启用兼容模式兜底。",
                1409 => $"{Key} 已被其他应用占用，未能注册。",
                _ when FallbackEnabled => $"{Key} 注册失败（Win32 错误 {ErrorCode}），已启用兼容模式兜底。",
                _ => $"{Key} 注册失败（Win32 错误 {ErrorCode}）。"
            };
        }
    }
}

public sealed record HotkeyConflict(string HotkeyId, string Hotkey, string Message);

public sealed class HotKeyTriggeredEventArgs(KeyMappingConfig mapping) : EventArgs
{
    public KeyMappingConfig Mapping { get; } = mapping;
}

public sealed class CustomHotkeyTriggeredEventArgs(CustomHotkeyConfig hotkey) : EventArgs
{
    public CustomHotkeyConfig Hotkey { get; } = hotkey;
}

