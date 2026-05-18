using FControl.Models;
using FControl.Pages;
using FControl.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace FControl;

public sealed partial class CustomHotkeyEditorWindow : Window
{
    private readonly CustomHotkeyItem _item;
    private readonly Func<CustomHotkeyConfig, IReadOnlyList<HotkeyConflict>> _validateConflicts;
    private readonly TaskCompletionSource<bool> _completion = new();
    private bool _isRecording;

    public CustomHotkeyEditorWindow(
        CustomHotkeyItem item,
        string title,
        Func<CustomHotkeyConfig, IReadOnlyList<HotkeyConflict>> validateConflicts)
    {
        InitializeComponent();
        _item = item;
        _validateConflicts = validateConflicts;
        Title = title;
        TitleText.Text = title;
        AppWindow.Resize(new SizeInt32(860, 760));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsModal = false;
            presenter.IsResizable = true;
        }

        Closed += (_, _) => _completion.TrySetResult(false);
        LoadItem();
    }

    public Task<bool> ShowEditorAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void LoadItem()
    {
        NameBox.Text = _item.Name;
        EnabledSwitch.IsOn = _item.Enabled;
        HotkeyBox.Text = _item.Hotkey;
        TypeBox.ItemsSource = ScriptTypeMetadata.AvailableNames;
        TypeBox.SelectedItem = _item.ScriptTypeName;
        ModeBox.ItemsSource = ScriptModeMetadata.AvailableNames;
        ModeBox.SelectedItem = _item.ScriptModeName;
        PathBox.Text = _item.ScriptPath;
        InlineBox.Text = _item.InlineCode;
        ArgumentsBox.Text = _item.Arguments;
        WorkingDirectoryBox.Text = _item.WorkingDirectory;
        WindowBox.ItemsSource = RunWindowModeMetadata.AvailableNames;
        WindowBox.SelectedItem = _item.RunWindowModeName;
        TimeoutBox.Value = _item.TimeoutSeconds;
        ConcurrencyBox.ItemsSource = ConcurrencyPolicyMetadata.AvailableNames;
        ConcurrencyBox.SelectedItem = _item.ConcurrencyPolicyName;
        OverlaySwitch.IsOn = _item.ShowOverlay;
        UpdateModeVisibility();
    }

    private void HotkeyBox_Tapped(object sender, TappedRoutedEventArgs e)
    {
        StartRecording();
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        StartRecording();
    }

    private void StartRecording()
    {
        _isRecording = true;
        HotkeyBox.Text = string.Empty;
        HotkeyBox.PlaceholderText = "请按下快捷键组合...";
        HotkeyBox.Focus(FocusState.Programmatic);
        EditorInfoBar.Severity = InfoBarSeverity.Informational;
        EditorInfoBar.Message = "正在录制快捷键，请按下组合键。";
        EditorInfoBar.IsOpen = true;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecording)
        {
            e.Handled = true;
            return;
        }

        var key = e.Key == VirtualKey.Menu ? VirtualKey.None : e.Key;
        if (key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
            VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
            VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
            VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            e.Handled = true;
            return;
        }

        var modifiers = GetCurrentModifiers();
        var text = HotkeyParser.BuildDisplayText(modifiers, (uint)key);
        if (HotkeyParser.TryParse(text, out var definition, out var error))
        {
            HotkeyBox.Text = definition.DisplayText;
            _isRecording = false;
            EditorInfoBar.Severity = InfoBarSeverity.Success;
            EditorInfoBar.Message = $"已录制：{definition.DisplayText}";
            EditorInfoBar.IsOpen = true;
        }
        else
        {
            ShowError(error);
        }

        e.Handled = true;
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateModeVisibility();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyParser.TryParse(HotkeyBox.Text, out var definition, out var error))
        {
            ShowError(error);
            return;
        }

        var candidate = BuildConfig(definition.DisplayText);
        var conflicts = _validateConflicts(candidate);
        if (conflicts.Count > 0)
        {
            ShowError(string.Join(Environment.NewLine, conflicts.Select(static conflict => $"{conflict.Hotkey}：{conflict.Message}")));
            return;
        }

        _item.CopyFrom(CustomHotkeyItem.FromConfig(candidate));
        _completion.TrySetResult(true);
        Close();
    }

    private CustomHotkeyConfig BuildConfig(string hotkey)
    {
        return new CustomHotkeyConfig
        {
            Id = _item.Id,
            Enabled = EnabledSwitch.IsOn,
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名脚本" : NameBox.Text.Trim(),
            Hotkey = hotkey,
            ScriptType = ScriptTypeMetadata.FromDisplayName((string?)TypeBox.SelectedItem),
            ScriptMode = ScriptModeMetadata.FromDisplayName((string?)ModeBox.SelectedItem),
            ScriptPath = PathBox.Text.Trim(),
            InlineCode = InlineBox.Text,
            Arguments = ArgumentsBox.Text.Trim(),
            WorkingDirectory = WorkingDirectoryBox.Text.Trim(),
            RunWindowMode = RunWindowModeMetadata.FromDisplayName((string?)WindowBox.SelectedItem),
            TimeoutSeconds = double.IsNaN(TimeoutBox.Value) ? 30 : (int)Math.Clamp(Math.Round(TimeoutBox.Value), 1, 3600),
            ConcurrencyPolicy = ConcurrencyPolicyMetadata.FromDisplayName((string?)ConcurrencyBox.SelectedItem),
            ShowOverlay = OverlaySwitch.IsOn
        };
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _completion.TrySetResult(false);
        Close();
    }

    private void UpdateModeVisibility()
    {
        var mode = ScriptModeMetadata.FromDisplayName((string?)ModeBox.SelectedItem);
        PathBox.Visibility = mode == ScriptMode.File ? Visibility.Visible : Visibility.Collapsed;
        InlineBox.Visibility = mode == ScriptMode.Inline ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        EditorInfoBar.Message = message;
        EditorInfoBar.Severity = InfoBarSeverity.Error;
        EditorInfoBar.IsOpen = true;
    }

    private static uint GetCurrentModifiers()
    {
        var keyboard = InputKeyboardSource.GetKeyStateForCurrentThread;
        var modifiers = 0u;
        if (IsDown(keyboard(VirtualKey.Control)) || IsDown(keyboard(VirtualKey.LeftControl)) || IsDown(keyboard(VirtualKey.RightControl)))
        {
            modifiers |= HotkeyParser.ModControl;
        }

        if (IsDown(keyboard(VirtualKey.Menu)) || IsDown(keyboard(VirtualKey.LeftMenu)) || IsDown(keyboard(VirtualKey.RightMenu)))
        {
            modifiers |= HotkeyParser.ModAlt;
        }

        if (IsDown(keyboard(VirtualKey.Shift)) || IsDown(keyboard(VirtualKey.LeftShift)) || IsDown(keyboard(VirtualKey.RightShift)))
        {
            modifiers |= HotkeyParser.ModShift;
        }

        if (IsDown(keyboard(VirtualKey.LeftWindows)) || IsDown(keyboard(VirtualKey.RightWindows)))
        {
            modifiers |= HotkeyParser.ModWin;
        }

        return modifiers;
    }

    private static bool IsDown(CoreVirtualKeyStates state)
    {
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }
}
