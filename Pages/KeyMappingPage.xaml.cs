using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class KeyMappingPage : Page
{
    public ObservableCollection<KeyMappingItem> KeyMappings { get; } = new()
    {
        new("F1", "屏幕亮度减小"),
        new("F2", "屏幕亮度增大"),
        new("F3", "禁用"),
        new("F4", "禁用"),
        new("F5", "禁用"),
        new("F6", "禁用"),
        new("F7", "媒体回退", 2, Visibility.Visible),
        new("F8", "媒体暂停/播放"),
        new("F9", "媒体快进", 2, Visibility.Visible),
        new("F10", "静音切换"),
        new("F11", "音量减小"),
        new("F12", "音量增大")
    };

    public KeyMappingPage()
    {
        InitializeComponent();
    }
}

public sealed class KeyMappingItem(string key, string action, double seconds = 2, Visibility secondsVisibility = Visibility.Collapsed)
{
    private static readonly IReadOnlyList<string> AvailableActions =
    [
        "禁用",
        "屏幕亮度减小",
        "屏幕亮度增大",
        "音量减小",
        "音量增大",
        "静音切换",
        "媒体回退",
        "媒体暂停/播放",
        "媒体快进",
        "上一曲",
        "下一曲",
        "停止"
    ];

    public IReadOnlyList<string> Actions => AvailableActions;
    public string Key { get; } = key;
    public string Action { get; set; } = action;
    public double Seconds { get; set; } = seconds;
    public Visibility SecondsVisibility { get; } = secondsVisibility;
}
