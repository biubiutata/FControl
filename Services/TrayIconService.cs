using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace FControl.Services;

internal sealed class TrayIconService : IDisposable
{
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint WmApp = 0x8000;
    private const uint WmTrayIcon = WmApp + 1;
    private const int WmContextMenu = 0x007B;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;
    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmReturNcmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint AdvancedSettingsCommand = 1001;
    private const uint ExitCommand = 1002;
    private const nuint SubclassId = 1;

    private readonly nint _hwnd;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action _openWindow;
    private readonly Action _openAdvancedSettings;
    private readonly Action _exit;
    private readonly SubclassProc _subclassProc;
    private nint _iconHandle;
    private bool _disposed;

    public TrayIconService(
        nint hwnd,
        DispatcherQueue dispatcherQueue,
        Action openWindow,
        Action openAdvancedSettings,
        Action exit)
    {
        _hwnd = hwnd;
        _dispatcherQueue = dispatcherQueue;
        _openWindow = openWindow;
        _openAdvancedSettings = openAdvancedSettings;
        _exit = exit;
        _subclassProc = WindowSubclassProc;

        if (!SetWindowSubclass(_hwnd, _subclassProc, SubclassId, 0))
        {
            throw new InvalidOperationException("Unable to attach tray window subclass.");
        }

        AddIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var data = CreateNotifyIconData();
        _ = Shell_NotifyIcon(NimDelete, ref data);
        _ = RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);

        if (_iconHandle != 0)
        {
            _ = DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }

        _disposed = true;
    }

    private void AddIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _iconHandle = LoadImage(0, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        if (_iconHandle == 0)
        {
            _iconHandle = LoadIcon(0, new IntPtr(32512));
        }

        var data = CreateNotifyIconData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = WmTrayIcon;
        data.hIcon = _iconHandle;
        data.szTip = "FControl";

        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            throw new InvalidOperationException("Unable to create system tray icon.");
        }

        data.uVersion = NotifyIconVersion4;
        _ = Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private NOTIFYICONDATA CreateNotifyIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1
        };
    }

    private nint WindowSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (msg == WmTrayIcon)
        {
            var mouseMessage = unchecked((int)lParam);
            if (mouseMessage is WmLButtonUp or WmLButtonDblClk or NinSelect or NinKeySelect)
            {
                _dispatcherQueue.TryEnqueue(() => _openWindow());
                return 0;
            }

            if (mouseMessage is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
                return 0;
            }
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = AppendMenu(menu, MfString, AdvancedSettingsCommand, "高级设置");
            _ = AppendMenu(menu, MfSeparator, 0, null);
            _ = AppendMenu(menu, MfString, ExitCommand, "退出");

            if (!GetCursorPos(out var point))
            {
                return;
            }

            _ = SetForegroundWindow(_hwnd);
            var command = TrackPopupMenu(menu, TpmReturNcmd | TpmRightButton, point.X, point.Y, 0, _hwnd, 0);
            switch (command)
            {
                case AdvancedSettingsCommand:
                    _dispatcherQueue.TryEnqueue(() => _openAdvancedSettings());
                    break;
                case ExitCommand:
                    _dispatcherQueue.TryEnqueue(() => _exit());
                    break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hInst, string name, int type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadIcon(nint hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);
}
