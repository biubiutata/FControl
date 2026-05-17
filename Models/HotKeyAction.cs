using Microsoft.UI.Xaml;

namespace FControl.Models;

public enum HotKeyAction
{
    Disabled,
    BrightnessDown,
    BrightnessUp,
    VolumeDown,
    VolumeUp,
    MuteToggle,
    MediaRewind,
    MediaPlayPause,
    MediaFastForward,
    MediaPrevious,
    MediaNext,
    MediaStop
}

public static class HotKeyActionMetadata
{
    private static readonly IReadOnlyDictionary<HotKeyAction, string> ActionNames =
        new Dictionary<HotKeyAction, string>
        {
            [HotKeyAction.Disabled] = "禁用",
            [HotKeyAction.BrightnessDown] = "屏幕亮度减小",
            [HotKeyAction.BrightnessUp] = "屏幕亮度增大",
            [HotKeyAction.VolumeDown] = "音量减小",
            [HotKeyAction.VolumeUp] = "音量增大",
            [HotKeyAction.MuteToggle] = "静音切换",
            [HotKeyAction.MediaRewind] = "媒体回退",
            [HotKeyAction.MediaPlayPause] = "媒体暂停/播放",
            [HotKeyAction.MediaFastForward] = "媒体快进",
            [HotKeyAction.MediaPrevious] = "上一曲",
            [HotKeyAction.MediaNext] = "下一曲",
            [HotKeyAction.MediaStop] = "停止"
        };

    public static IReadOnlyList<string> AvailableActionNames { get; } = ActionNames.Values.ToArray();

    public static string GetDisplayName(HotKeyAction action)
    {
        return ActionNames.TryGetValue(action, out var name) ? name : ActionNames[HotKeyAction.Disabled];
    }

    public static HotKeyAction FromDisplayName(string? displayName)
    {
        foreach (var pair in ActionNames)
        {
            if (pair.Value == displayName)
            {
                return pair.Key;
            }
        }

        return HotKeyAction.Disabled;
    }

    public static Visibility GetSecondsVisibility(HotKeyAction action)
    {
        return RequiresSeconds(action) ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool RequiresSeconds(HotKeyAction action)
    {
        return action is HotKeyAction.MediaRewind or HotKeyAction.MediaFastForward;
    }
}
