using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using FControl.Models;
using FControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingRectangle = System.Drawing.Rectangle;

namespace FControl;

public sealed partial class DesktopKeyMappingWindow : Window
{
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int ResizeEdgeThickness = 8;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const uint DwmwaColorNone = 0xFFFFFFFE;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseMove = 0x0200;
    private const int IdcArrow = 32512;
    private const int IdcSizeNs = 32645;
    private const int IdcSizeWe = 32644;
    private const int IdcSizeNwse = 32642;
    private const int IdcSizeNesw = 32643;
    private const string NativeWindowClassName = "FControlDesktopKeyMappingNativeWindow";

    private static readonly NativeWndProc NativeWndProcInstance = NativeWindowProc;
    private static readonly Dictionary<nint, DesktopKeyMappingWindow> NativeWindows = new();
    private static ushort _nativeWindowClassAtom;

    private readonly AppConfigurationService _configurationService = AppServices.Configuration;
    private readonly nint _ownerHwnd;
    private readonly nint _nativeHwnd;
    private readonly DispatcherTimer _lockedHoverPollingTimer = new();
    private readonly DispatcherTimer _highlightTimer = new();
    private RectInt32 _bounds;
    private PointInt32 _dragStartCursor;
    private RectInt32 _operationStartBounds;
    private PointerOperation _pointerOperation;
    private ResizeHit _resizeHit;
    private PressedButton _pressedButton;
    private bool _isVisible;
    private bool _isApplying;
    private bool _disposed;
    private bool _isLockedHoverButtonVisible;
    private DateTimeOffset? _lockedHoverStartedAt;
    private string? _highlightedKey;

    public DesktopKeyMappingWindow()
    {
        InitializeComponent();
        _ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = false;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _ = ShowWindow(_ownerHwnd, SwHide);
        _nativeHwnd = CreateNativeWindow();
        NativeWindows[_nativeHwnd] = this;
        ApplyChromeSettings(_nativeHwnd);
        ApplyWindowInteractionStyle(AppServices.Configuration.Current.DesktopKeyMapping.IsLocked);

        _lockedHoverPollingTimer.Interval = TimeSpan.FromMilliseconds(100);
        _lockedHoverPollingTimer.Tick += LockedHoverPollingTimer_Tick;
        _highlightTimer.Interval = TimeSpan.FromMilliseconds(450);
        _highlightTimer.Tick += HighlightTimer_Tick;
    }

    public void ShowOrUpdate()
    {
        if (_disposed) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            ApplyConfiguration();
            Render();
            if (!_isVisible)
            {
                _ = ShowWindow(_nativeHwnd, SwShowNoActivate);
                _isVisible = true;
            }
        });
    }

    public void HideWindow()
    {
        if (_disposed) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            _lockedHoverPollingTimer.Stop();
            _lockedHoverStartedAt = null;
            _isLockedHoverButtonVisible = false;
            _ = ShowWindow(_nativeHwnd, SwHide);
            _isVisible = false;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lockedHoverPollingTimer.Stop();
        _lockedHoverPollingTimer.Tick -= LockedHoverPollingTimer_Tick;
        _highlightTimer.Stop();
        _highlightTimer.Tick -= HighlightTimer_Tick;
        NativeWindows.Remove(_nativeHwnd);
        _ = DestroyWindow(_nativeHwnd);
        Close();
    }

    public void HighlightKey(string key)
    {
        if (_disposed) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            _highlightedKey = key;
            Render();
            _highlightTimer.Stop();
            _highlightTimer.Start();
        });
    }

    private void ApplyConfiguration()
    {
        _isApplying = true;
        var config = _configurationService.Current.DesktopKeyMapping;
        var width = Math.Clamp(config.Width, 320, 1600);
        var height = Math.Clamp(config.Height, 90, 900);
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var x = Math.Clamp(config.X, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - width));
        var y = Math.Clamp(config.Y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - height));

        _bounds = new RectInt32(x, y, width, height);
        _ = SetWindowPos(_nativeHwnd, 0, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SwpNoActivate | SwpShowWindow);
        _isLockedHoverButtonVisible = false;
        _lockedHoverStartedAt = null;
        ApplyWindowInteractionStyle(config.IsLocked);
        UpdateLockedHoverPolling(config.IsLocked);
        _isApplying = false;
    }

    private void Render()
    {
        if (_disposed) return;
        var width = Math.Max(_bounds.Width, 1);
        var height = Math.Max(_bounds.Height, 1);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(DrawingColor.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            DrawContent(graphics, width, height);
        }

        ApplyBitmap(bitmap, _bounds.X, _bounds.Y);
    }

    private void DrawContent(Graphics graphics, int width, int height)
    {
        var config = _configurationService.Current.DesktopKeyMapping;
        var backgroundColor = ParseDrawingColor(config.BackgroundColor, DrawingColor.FromArgb(32, 32, 32));
        var textColor = ParseDrawingColor(config.TextColor, DrawingColor.White);
        var iconColor = ParseDrawingColor(config.IconColor, DrawingColor.White);
        var highlightColor = ParseDrawingColor(config.HighlightColor, DrawingColor.FromArgb(0, 120, 212));
        using var backgroundBrush = new SolidBrush(DrawingColor.FromArgb(ToAlpha(config.OpacityPercent), backgroundColor.R, backgroundColor.G, backgroundColor.B));
        using var keyFont = new DrawingFont("Microsoft YaHei UI", 15, DrawingFontStyle.Bold, GraphicsUnit.Pixel);
        using var actionFont = new DrawingFont("Microsoft YaHei UI", 12, DrawingFontStyle.Regular, GraphicsUnit.Pixel);
        using var iconFont = new DrawingFont("Segoe MDL2 Assets", 22, DrawingFontStyle.Regular, GraphicsUnit.Pixel);
        using var buttonFont = new DrawingFont("Segoe MDL2 Assets", 15, DrawingFontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(DrawingColor.FromArgb(ToAlpha(config.TextOpacityPercent), textColor.R, textColor.G, textColor.B));
        using var iconBrush = new SolidBrush(DrawingColor.FromArgb(ToAlpha(config.IconOpacityPercent), iconColor.R, iconColor.G, iconColor.B));
        using var highlightBrush = new SolidBrush(DrawingColor.FromArgb(178, highlightColor.R, highlightColor.G, highlightColor.B));

        graphics.FillRectangle(backgroundBrush, 0, 0, width, height);
        if (config.IsVertical)
        {
            DrawVerticalMappings(graphics, width, height, keyFont, actionFont, iconFont, textBrush, iconBrush, highlightBrush);
        }
        else
        {
            DrawHorizontalMappings(graphics, width, height, keyFont, actionFont, iconFont, textBrush, iconBrush, highlightBrush);
        }

        if (!config.IsLocked || _isLockedHoverButtonVisible)
        {
            DrawWindowButtons(graphics, width, buttonFont, iconBrush, config.IsLocked);
        }
    }

    private void DrawHorizontalMappings(Graphics graphics, int width, int height, DrawingFont keyFont, DrawingFont actionFont, DrawingFont iconFont, Brush textBrush, Brush iconBrush, Brush highlightBrush)
    {
        var mappings = _configurationService.Current.KeyMappings;
        var padding = 16;
        var itemWidth = Math.Max(1, (width - padding * 2) / Math.Max(mappings.Count, 1));
        var centerY = height / 2;
        using var centerFormat = CreateStringFormat(StringAlignment.Center, StringAlignment.Center);
        for (var i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            var x = padding + i * itemWidth;
            var itemRect = new DrawingRectangle(x, Math.Max(4, centerY - 48), itemWidth, Math.Min(96, Math.Max(1, height - 8)));
            DrawHighlightIfNeeded(graphics, mapping.Key, itemRect, highlightBrush);
            graphics.DrawString(mapping.Key, keyFont, textBrush, new RectangleF(x, centerY - 43, itemWidth, 20), centerFormat);
            if (mapping.Action != HotKeyAction.Disabled)
            {
                graphics.DrawString(GetGlyph(mapping.Action), iconFont, iconBrush, new RectangleF(x, centerY - 18, itemWidth, 28), centerFormat);
                graphics.DrawString(GetShortName(mapping.Action), actionFont, textBrush, new RectangleF(x, centerY + 20, itemWidth, 22), centerFormat);
            }
        }
    }

    private void DrawVerticalMappings(Graphics graphics, int width, int height, DrawingFont keyFont, DrawingFont actionFont, DrawingFont iconFont, Brush textBrush, Brush iconBrush, Brush highlightBrush)
    {
        var mappings = _configurationService.Current.KeyMappings;
        var padding = 16;
        var rowHeight = Math.Max(1, (height - padding * 2) / Math.Max(mappings.Count, 1));
        using var centerFormat = CreateStringFormat(StringAlignment.Center, StringAlignment.Center);
        using var leftFormat = CreateStringFormat(StringAlignment.Near, StringAlignment.Center);
        for (var i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            var y = padding + i * rowHeight;
            var itemRect = new DrawingRectangle(8, y, Math.Max(1, width - 16), rowHeight);
            DrawHighlightIfNeeded(graphics, mapping.Key, itemRect, highlightBrush);
            graphics.DrawString(mapping.Key, keyFont, textBrush, new RectangleF(48, y, 52, rowHeight), centerFormat);
            if (mapping.Action != HotKeyAction.Disabled)
            {
                graphics.DrawString(GetGlyph(mapping.Action), iconFont, iconBrush, new RectangleF(118, y, 46, rowHeight), centerFormat);
                graphics.DrawString(GetShortName(mapping.Action), actionFont, textBrush, new RectangleF(174, y, Math.Max(1, width - 190), rowHeight), leftFormat);
            }
        }
    }

    private void DrawHighlightIfNeeded(Graphics graphics, string key, DrawingRectangle itemRect, Brush highlightBrush)
    {
        if (!string.Equals(_highlightedKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        using var path = CreateRoundedRectanglePath(itemRect, 8);
        graphics.FillPath(highlightBrush, path);
    }

    private void DrawWindowButtons(Graphics graphics, int width, DrawingFont buttonFont, Brush iconBrush, bool isLocked)
    {
        using var centerFormat = CreateStringFormat(StringAlignment.Center, StringAlignment.Center);
        graphics.DrawString(isLocked ? "\uE72E" : "\uE785", buttonFont, iconBrush, ToRectangleF(GetLockButtonRect(width, isLocked)), centerFormat);
        if (!isLocked)
        {
            graphics.DrawString("\uE711", buttonFont, iconBrush, ToRectangleF(GetCloseButtonRect(width)), centerFormat);
        }
    }

    private void LockedHoverPollingTimer_Tick(object? sender, object e)
    {
        if (_disposed) return;
        if (!_isVisible || !_configurationService.Current.DesktopKeyMapping.IsLocked)
        {
            _lockedHoverStartedAt = null;
            return;
        }
        _ = GetCursorPos(out var cursor);
        var isInside = cursor.X >= _bounds.X && cursor.X < _bounds.X + _bounds.Width && cursor.Y >= _bounds.Y && cursor.Y < _bounds.Y + _bounds.Height;
        if (!isInside)
        {
            _lockedHoverStartedAt = null;
            if (_isLockedHoverButtonVisible) HideLockButtonForLockedWindow();
            return;
        }
        if (_isLockedHoverButtonVisible) return;
        _lockedHoverStartedAt ??= DateTimeOffset.Now;
        if (DateTimeOffset.Now - _lockedHoverStartedAt.Value >= TimeSpan.FromMilliseconds(1500)) ShowLockButton();
    }

    private void HighlightTimer_Tick(object? sender, object e)
    {
        if (_disposed) return;
        _highlightTimer.Stop();
        _highlightedKey = null;
        Render();
    }

    private nint HandleNativeMessage(uint message, nuint wParam, nint lParam)
    {
        if (_disposed) return DefWindowProc(_nativeHwnd, message, wParam, lParam);
        switch (message)
        {
            case WmLButtonDown: OnLeftButtonDown(GetX(lParam), GetY(lParam)); return 0;
            case WmMouseMove: OnMouseMove(GetX(lParam), GetY(lParam)); return 0;
            case WmLButtonUp: OnLeftButtonUp(GetX(lParam), GetY(lParam)); return 0;
            default: return DefWindowProc(_nativeHwnd, message, wParam, lParam);
        }
    }

    private void OnLeftButtonDown(int x, int y)
    {
        var config = _configurationService.Current.DesktopKeyMapping;
        if (config.IsLocked)
        {
            if (_isLockedHoverButtonVisible && GetLockButtonRect(_bounds.Width, true).Contains(x, y))
            {
                _pressedButton = PressedButton.Lock;
                _ = SetCapture(_nativeHwnd);
            }
            return;
        }
        if (GetLockButtonRect(_bounds.Width, false).Contains(x, y)) { _pressedButton = PressedButton.Lock; _ = SetCapture(_nativeHwnd); return; }
        if (GetCloseButtonRect(_bounds.Width).Contains(x, y)) { _pressedButton = PressedButton.Close; _ = SetCapture(_nativeHwnd); return; }
        _ = GetCursorPos(out _dragStartCursor);
        _operationStartBounds = _bounds;
        _resizeHit = GetResizeHit(x, y);
        _pointerOperation = _resizeHit == ResizeHit.None ? PointerOperation.Drag : PointerOperation.Resize;
        _ = SetCapture(_nativeHwnd);
    }

    private void OnMouseMove(int x, int y)
    {
        if (_pointerOperation == PointerOperation.None)
        {
            if (!_configurationService.Current.DesktopKeyMapping.IsLocked) ApplyResizeCursor(GetResizeHit(x, y));
            return;
        }
        _ = GetCursorPos(out var cursor);
        if (_pointerOperation == PointerOperation.Drag)
        {
            MoveAndRender(new RectInt32(_operationStartBounds.X + cursor.X - _dragStartCursor.X, _operationStartBounds.Y + cursor.Y - _dragStartCursor.Y, _operationStartBounds.Width, _operationStartBounds.Height));
            return;
        }
        ResizeAndRender(cursor);
    }

    private void OnLeftButtonUp(int x, int y)
    {
        if (_pressedButton != PressedButton.None)
        {
            var pressed = _pressedButton;
            _pressedButton = PressedButton.None;
            _ = ReleaseCapture();
            if (pressed == PressedButton.Lock && GetLockButtonRect(_bounds.Width, _configurationService.Current.DesktopKeyMapping.IsLocked).Contains(x, y)) ToggleLocked();
            else if (pressed == PressedButton.Close && GetCloseButtonRect(_bounds.Width).Contains(x, y)) _configurationService.SetDesktopKeyMappingEnabled(false);
            return;
        }
        if (_pointerOperation == PointerOperation.None) return;
        _pointerOperation = PointerOperation.None;
        _resizeHit = ResizeHit.None;
        _ = ReleaseCapture();
        SaveCurrentConfig();
    }

    private void MoveAndRender(RectInt32 bounds)
    {
        _bounds = bounds;
        _ = SetWindowPos(_nativeHwnd, 0, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SwpNoActivate | SwpShowWindow);
        Render();
    }

    private void ResizeAndRender(PointInt32 cursor)
    {
        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;
        var x = _operationStartBounds.X;
        var y = _operationStartBounds.Y;
        var width = _operationStartBounds.Width;
        var height = _operationStartBounds.Height;
        if (_resizeHit is ResizeHit.Left or ResizeHit.TopLeft or ResizeHit.BottomLeft) { x += dx; width -= dx; }
        else if (_resizeHit is ResizeHit.Right or ResizeHit.TopRight or ResizeHit.BottomRight) width += dx;
        if (_resizeHit is ResizeHit.Top or ResizeHit.TopLeft or ResizeHit.TopRight) { y += dy; height -= dy; }
        else if (_resizeHit is ResizeHit.Bottom or ResizeHit.BottomLeft or ResizeHit.BottomRight) height += dy;
        if (width < 320) { if (_resizeHit is ResizeHit.Left or ResizeHit.TopLeft or ResizeHit.BottomLeft) x -= 320 - width; width = 320; }
        if (height < 90) { if (_resizeHit is ResizeHit.Top or ResizeHit.TopLeft or ResizeHit.TopRight) y -= 90 - height; height = 90; }
        MoveAndRender(new RectInt32(x, y, Math.Min(width, 1600), Math.Min(height, 900)));
    }

    private void ToggleLocked()
    {
        var config = _configurationService.Current.DesktopKeyMapping.Clone();
        config.IsLocked = !config.IsLocked;
        _configurationService.SetDesktopKeyMappingDisplayConfig(config);
        ApplyConfiguration();
        Render();
    }

    private void ShowLockButton()
    {
        _isLockedHoverButtonVisible = true;
        ApplyWindowInteractionStyle(false);
        Render();
    }

    private void HideLockButtonForLockedWindow()
    {
        _isLockedHoverButtonVisible = false;
        ApplyWindowInteractionStyle(true);
        Render();
    }

    private void UpdateLockedHoverPolling(bool isLocked)
    {
        if (isLocked) _lockedHoverPollingTimer.Start();
        else { _lockedHoverPollingTimer.Stop(); _lockedHoverStartedAt = null; _isLockedHoverButtonVisible = false; }
    }

    private void SaveCurrentConfig()
    {
        if (_isApplying) return;
        var config = _configurationService.Current.DesktopKeyMapping.Clone();
        config.X = _bounds.X; config.Y = _bounds.Y; config.Width = _bounds.Width; config.Height = _bounds.Height;
        _configurationService.SetDesktopKeyMappingDisplayConfig(config);
    }

    private void ApplyBitmap(Bitmap bitmap, int x, int y)
    {
        var screenDc = nint.Zero;
        var memoryDc = nint.Zero;
        var bitmapHandle = nint.Zero;
        var oldBitmap = nint.Zero;

        try
        {
            screenDc = GetDC(0);
            memoryDc = CreateCompatibleDC(screenDc);
            bitmapHandle = bitmap.GetHbitmap(DrawingColor.FromArgb(0));
            oldBitmap = SelectObject(memoryDc, bitmapHandle);

            var destination = new NativePoint { X = x, Y = y };
            var size = new NativeSize { Cx = bitmap.Width, Cy = bitmap.Height };
            var source = new NativePoint { X = 0, Y = 0 };
            var blend = new BlendFunction { BlendOp = AcSrcOver, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            _ = UpdateLayeredWindow(_nativeHwnd, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (oldBitmap != nint.Zero && memoryDc != nint.Zero) _ = SelectObject(memoryDc, oldBitmap);
            if (bitmapHandle != nint.Zero) _ = DeleteObject(bitmapHandle);
            if (memoryDc != nint.Zero) _ = DeleteDC(memoryDc);
            if (screenDc != nint.Zero) _ = ReleaseDC(0, screenDc);
        }
    }

    private static nint CreateNativeWindow()
    {
        RegisterNativeWindowClass();
        var hInstance = GetModuleHandle(null);
        return CreateWindowEx(WsExToolWindow | WsExLayered | WsExNoActivate, NativeWindowClassName, string.Empty, WsPopup, 40, 40, 320, 90, 0, 0, hInstance, 0);
    }

    private static void RegisterNativeWindowClass()
    {
        if (_nativeWindowClassAtom != 0) return;
        var windowClass = new WindowClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            Style = 0,
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(NativeWndProcInstance),
            CbClsExtra = 0,
            CbWndExtra = 0,
            HInstance = GetModuleHandle(null),
            HIcon = 0,
            HCursor = LoadCursor(0, IdcArrow),
            HbrBackground = 0,
            LpszMenuName = null,
            LpszClassName = NativeWindowClassName,
            HIconSm = 0
        };
        _nativeWindowClassAtom = RegisterClassEx(ref windowClass);
    }

    private static nint NativeWindowProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        return NativeWindows.TryGetValue(hwnd, out var window) ? window.HandleNativeMessage(message, wParam, lParam) : DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ApplyWindowInteractionStyle(bool isLocked)
    {
        var style = GetWindowLong(_nativeHwnd, GwlExStyle);
        style |= WsExToolWindow | WsExLayered | WsExNoActivate;
        if (isLocked) style |= WsExTransparent;
        else style &= ~WsExTransparent;
        _ = SetWindowLong(_nativeHwnd, GwlExStyle, style);
    }

    private static void ApplyChromeSettings(nint hwnd)
    {
        var cornerPreference = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        var noneColor = unchecked((int)DwmwaColorNone);
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref noneColor, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref noneColor, sizeof(int));
    }

    private ResizeHit GetResizeHit(int x, int y)
    {
        var left = x <= ResizeEdgeThickness;
        var right = x >= _bounds.Width - ResizeEdgeThickness;
        var top = y <= ResizeEdgeThickness;
        var bottom = y >= _bounds.Height - ResizeEdgeThickness;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => ResizeHit.TopLeft,
            (_, true, true, _) => ResizeHit.TopRight,
            (true, _, _, true) => ResizeHit.BottomLeft,
            (_, true, _, true) => ResizeHit.BottomRight,
            (true, _, _, _) => ResizeHit.Left,
            (_, true, _, _) => ResizeHit.Right,
            (_, _, true, _) => ResizeHit.Top,
            (_, _, _, true) => ResizeHit.Bottom,
            _ => ResizeHit.None
        };
    }

    private static void ApplyResizeCursor(ResizeHit hit)
    {
        var cursorId = hit switch
        {
            ResizeHit.Left or ResizeHit.Right => IdcSizeWe,
            ResizeHit.Top or ResizeHit.Bottom => IdcSizeNs,
            ResizeHit.TopLeft or ResizeHit.BottomRight => IdcSizeNwse,
            ResizeHit.TopRight or ResizeHit.BottomLeft => IdcSizeNesw,
            _ => IdcArrow
        };
        _ = SetCursor(LoadCursor(0, cursorId));
    }

    private static DrawingRectangle GetLockButtonRect(int width, bool isLocked) => isLocked ? new DrawingRectangle(Math.Max(0, width - 40), 8, 28, 28) : new DrawingRectangle(Math.Max(0, width - 72), 8, 28, 28);
    private static DrawingRectangle GetCloseButtonRect(int width) => new(Math.Max(0, width - 40), 8, 28, 28);

    private static GraphicsPath CreateRoundedRectanglePath(DrawingRectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static StringFormat CreateStringFormat(StringAlignment horizontal, StringAlignment vertical) => new() { Alignment = horizontal, LineAlignment = vertical, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
    private static RectangleF ToRectangleF(DrawingRectangle rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    private static byte ToAlpha(int opacityPercent) => (byte)Math.Clamp(Math.Round(Math.Clamp(opacityPercent, 0, 100) / 100.0 * 255), 0, 255);

    private static DrawingColor ParseDrawingColor(string value, DrawingColor fallback)
    {
        return HexColorHelper.TryParseRgb(value, out var red, out var green, out var blue)
            ? DrawingColor.FromArgb(red, green, blue)
            : fallback;
    }

    private static string GetGlyph(HotKeyAction action) => action switch
    {
        HotKeyAction.BrightnessDown or HotKeyAction.BrightnessUp => "\uE706",
        HotKeyAction.VolumeDown or HotKeyAction.VolumeUp => "\uE767",
        HotKeyAction.MuteToggle => "\uE74F",
        HotKeyAction.MediaPlayPause => "\uE769",
        HotKeyAction.MediaRewind => "\uEB9E",
        HotKeyAction.MediaFastForward => "\uEB9D",
        HotKeyAction.MediaPrevious => "\uE892",
        HotKeyAction.MediaNext => "\uE893",
        HotKeyAction.MediaStop => "\uE71A",
        _ => string.Empty
    };

    private static string GetShortName(HotKeyAction action) => action switch
    {
        HotKeyAction.BrightnessDown => "亮度-",
        HotKeyAction.BrightnessUp => "亮度+",
        HotKeyAction.VolumeDown => "音量-",
        HotKeyAction.VolumeUp => "音量+",
        HotKeyAction.MuteToggle => "静音",
        HotKeyAction.MediaPlayPause => "播放",
        HotKeyAction.MediaRewind => "回退",
        HotKeyAction.MediaFastForward => "快进",
        HotKeyAction.MediaPrevious => "上曲",
        HotKeyAction.MediaNext => "下曲",
        HotKeyAction.MediaStop => "停止",
        _ => string.Empty
    };

    private static int GetX(nint lParam) => unchecked((short)((long)lParam & 0xFFFF));
    private static int GetY(nint lParam) => unchecked((short)(((long)lParam >> 16) & 0xFFFF));

    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint hWnd);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClassEx lpwcx);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointInt32 lpPoint);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetCapture(nint hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint LoadCursor(nint hInstance, int lpCursorName);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint hCursor);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hWnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint hObject);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref NativePoint pptDst, ref NativeSize psize, nint hdcSrc, ref NativePoint pptSrc, uint crKey, ref BlendFunction pblend, uint dwFlags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint GetModuleHandle(string? lpModuleName);
    [DllImport("dwmapi.dll", SetLastError = true)] private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private delegate nint NativeWndProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClassEx { public uint CbSize; public uint Style; public nint LpfnWndProc; public int CbClsExtra; public int CbWndExtra; public nint HInstance; public nint HIcon; public nint HCursor; public nint HbrBackground; public string? LpszMenuName; public string LpszClassName; public nint HIconSm; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Cx; public int Cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
    private enum PointerOperation { None, Drag, Resize }
    private enum PressedButton { None, Lock, Close }
    private enum ResizeHit { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
}
