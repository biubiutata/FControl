using System.Runtime.InteropServices;
using System.Text;
using Windows.System;

namespace FControl.Services;

public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    private static readonly IReadOnlyDictionary<string, uint> SpecialKeys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = 0x08,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        [";"] = 0xBA,
        ["="] = 0xBB,
        [","] = 0xBC,
        ["-"] = 0xBD,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        ["`"] = 0xC0,
        ["["] = 0xDB,
        ["\\"] = 0xDC,
        ["]"] = 0xDD,
        ["'"] = 0xDE
    };

    public static bool TryParse(string? text, out HotkeyDefinition definition, out string error)
    {
        definition = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空。";
            return false;
        }

        var modifiers = 0u;
        uint? virtualKey = null;
        var keyName = string.Empty;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    continue;
                case "ALT":
                    modifiers |= ModAlt;
                    continue;
                case "SHIFT":
                    modifiers |= ModShift;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    continue;
            }

            if (virtualKey is not null)
            {
                error = "快捷键只能包含一个主按键。";
                return false;
            }

            if (!TryParseMainKey(part, out var parsedVirtualKey, out keyName))
            {
                error = $"不支持的按键：{part}";
                return false;
            }

            virtualKey = parsedVirtualKey;
        }

        if (virtualKey is null)
        {
            error = "快捷键缺少主按键。";
            return false;
        }

        if (IsModifierOnlyKey(virtualKey.Value))
        {
            error = "主按键不能是 Ctrl、Alt、Shift 或 Win。";
            return false;
        }

        definition = new HotkeyDefinition(modifiers, virtualKey.Value, BuildDisplayText(modifiers, keyName));
        return true;
    }

    public static string NormalizeHotkeyText(string? text)
    {
        return TryParse(text, out var definition, out _) ? definition.DisplayText : string.Empty;
    }

    public static string BuildDisplayText(uint modifiers, uint virtualKey)
    {
        return BuildDisplayText(modifiers, GetKeyName(virtualKey));
    }

    public static bool IsReserved(HotkeyDefinition definition, out string reason)
    {
        reason = string.Empty;
        var modifiers = definition.Modifiers;
        var vk = definition.VirtualKey;
        var hasCtrl = (modifiers & ModControl) != 0;
        var hasAlt = (modifiers & ModAlt) != 0;
        var hasShift = (modifiers & ModShift) != 0;
        var hasWin = (modifiers & ModWin) != 0;

        if (hasCtrl && hasAlt && vk == 0x2E)
        {
            reason = "Ctrl+Alt+Delete 是系统安全快捷键。";
            return true;
        }

        if (hasWin && vk == 0x4C)
        {
            reason = "Win+L 是锁屏快捷键。";
            return true;
        }

        if (hasAlt && vk == 0x09)
        {
            reason = "Alt+Tab 是系统窗口切换快捷键。";
            return true;
        }

        if (hasAlt && vk == 0x73)
        {
            reason = "Alt+F4 是关闭窗口快捷键。";
            return true;
        }

        if (hasCtrl && hasShift && vk == 0x1B)
        {
            reason = "Ctrl+Shift+Esc 是任务管理器快捷键。";
            return true;
        }

        if (!hasCtrl && !hasAlt && !hasShift && !hasWin)
        {
            reason = "自定义脚本快捷键必须至少包含 Ctrl、Alt、Shift 或 Win 中的一个修饰键。";
            return true;
        }

        return false;
    }

    public static bool TryRegisterForProbe(nint hwnd, HotkeyDefinition definition, out int errorCode)
    {
        var id = unchecked((int)0x7FC1);
        var modifiers = definition.Modifiers | ModNoRepeat;
        if (RegisterHotKey(hwnd, id, modifiers, definition.VirtualKey))
        {
            _ = UnregisterHotKey(hwnd, id);
            errorCode = 0;
            return true;
        }

        errorCode = Marshal.GetLastWin32Error();
        return false;
    }

    private static string BuildDisplayText(uint modifiers, string keyName)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static bool TryParseMainKey(string text, out uint virtualKey, out string keyName)
    {
        virtualKey = 0;
        keyName = string.Empty;

        if (text.Length == 1)
        {
            var c = char.ToUpperInvariant(text[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                keyName = c.ToString();
                return true;
            }
        }

        if (text.StartsWith('F') && int.TryParse(text[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            keyName = $"F{functionKey}";
            return true;
        }

        if (text.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && int.TryParse(text[3..], out var numpadKey) && numpadKey is >= 0 and <= 9)
        {
            virtualKey = (uint)(0x60 + numpadKey);
            keyName = $"Num{numpadKey}";
            return true;
        }

        if (SpecialKeys.TryGetValue(text, out virtualKey))
        {
            keyName = text.Equals("Escape", StringComparison.OrdinalIgnoreCase) ? "Esc" : text;
            return true;
        }

        return false;
    }

    private static bool IsModifierOnlyKey(uint virtualKey)
    {
        return virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C;
    }

    private static string GetKeyName(uint virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return $"Num{virtualKey - 0x60}";
        }

        foreach (var pair in SpecialKeys)
        {
            if (pair.Value == virtualKey && pair.Key != "Escape")
            {
                return pair.Key;
            }
        }

        var scanCode = MapVirtualKey(virtualKey, 0) << 16;
        var builder = new StringBuilder(64);
        return GetKeyNameText((int)scanCode, builder, builder.Capacity) > 0 ? builder.ToString() : $"VK{virtualKey:X2}";
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int cchSize);
}

public readonly record struct HotkeyDefinition(uint Modifiers, uint VirtualKey, string DisplayText);
