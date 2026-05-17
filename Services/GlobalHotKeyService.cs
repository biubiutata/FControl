using System.Diagnostics;
using System.Runtime.InteropServices;
using FControl.Models;

namespace FControl.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int FirstHotKeyId = 0x4600;
    private const int WmHotKey = 0x0312;
    private const uint NoModifiers = 0x0000;
    private const nuint SubclassId = 2;
    private const int VirtualKeyF1 = 0x70;
    private const int VirtualKeyF12 = 0x7B;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int LlkhfInjected = 0x00000010;

    private readonly nint _hwnd;
    private readonly AppConfigurationService _configurationService;
    private readonly SubclassProc _subclassProc;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly Dictionary<int, KeyMappingConfig> _registeredMappings = [];
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

    public IReadOnlyList<HotKeyRegistrationFailure> LastRegistrationFailures { get; private set; } = [];

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UnregisterAll();

        var failures = new List<HotKeyRegistrationFailure>();
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
            if (RegisterHotKey(_hwnd, hotKeyId, NoModifiers, (uint)virtualKey))
            {
                _registeredMappings[hotKeyId] = mapping.Clone();
                continue;
            }

            var errorCode = Marshal.GetLastWin32Error();
            _hookMappings[virtualKey] = mapping.Clone();
            failures.Add(new HotKeyRegistrationFailure(mapping.Key, errorCode, true));
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
        if (msg == WmHotKey && _registeredMappings.TryGetValue(unchecked((int)wParam), out var mapping))
        {
            Trigger(mapping, "RegisterHotKey");
            return 0;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private nint LowLevelKeyboardHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
        {
            var hook = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var virtualKey = unchecked((int)hook.vkCode);
            if (virtualKey is >= VirtualKeyF1 and <= VirtualKeyF12 &&
                (hook.flags & LlkhfInjected) == 0 &&
                _hookMappings.TryGetValue(virtualKey, out var mapping))
            {
                Trigger(mapping, "WH_KEYBOARD_LL fallback");
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

public sealed record HotKeyRegistrationFailure(string Key, int ErrorCode, bool FallbackEnabled = false)
{
    public string Message => ErrorCode switch
    {
        1409 when FallbackEnabled => $"{Key} 已被其他应用占用，已启用兼容模式兜底。",
        1409 => $"{Key} 已被其他应用占用，未能注册。",
        _ when FallbackEnabled => $"{Key} 注册失败（Win32 错误 {ErrorCode}），已启用兼容模式兜底。",
        _ => $"{Key} 注册失败（Win32 错误 {ErrorCode}）。"
    };
}

public sealed class HotKeyTriggeredEventArgs(KeyMappingConfig mapping) : EventArgs
{
    public KeyMappingConfig Mapping { get; } = mapping;
}
