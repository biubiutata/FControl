using System.Diagnostics;
using System.Runtime.InteropServices;
using FControl.Models;

namespace FControl.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int FirstHotKeyId = 0x4600;
    private const int WmHotKey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const nuint SubclassId = 2;
    private const int VirtualKeyF1 = 0x70;

    private readonly nint _hwnd;
    private readonly AppConfigurationService _configurationService;
    private readonly SubclassProc _subclassProc;
    private readonly Dictionary<int, KeyMappingConfig> _registeredMappings = [];
    private bool _disposed;

    public GlobalHotKeyService(nint hwnd, AppConfigurationService configurationService)
    {
        _hwnd = hwnd;
        _configurationService = configurationService;
        _subclassProc = WindowSubclassProc;

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
            if (RegisterHotKey(_hwnd, hotKeyId, ModNoRepeat, (uint)virtualKey))
            {
                _registeredMappings[hotKeyId] = mapping.Clone();
                continue;
            }

            failures.Add(new HotKeyRegistrationFailure(mapping.Key, Marshal.GetLastWin32Error()));
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
            Debug.WriteLine($"FControl hotkey: {mapping.Key} -> {mapping.Action}");
            HotKeyTriggered?.Invoke(this, new HotKeyTriggeredEventArgs(mapping.Clone()));
            return 0;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void UnregisterAll()
    {
        foreach (var hotKeyId in _registeredMappings.Keys)
        {
            _ = UnregisterHotKey(_hwnd, hotKeyId);
        }

        _registeredMappings.Clear();
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

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}

public sealed record HotKeyRegistrationFailure(string Key, int ErrorCode)
{
    public string Message => ErrorCode switch
    {
        1409 => $"{Key} 已被其他应用占用，未能注册。",
        _ => $"{Key} 注册失败（Win32 错误 {ErrorCode}）。"
    };
}

public sealed class HotKeyTriggeredEventArgs(KeyMappingConfig mapping) : EventArgs
{
    public KeyMappingConfig Mapping { get; } = mapping;
}
