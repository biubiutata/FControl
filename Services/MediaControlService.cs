using System.Runtime.InteropServices;
using FControl.Models;
using Windows.Media.Control;

namespace FControl.Services;

public sealed class MediaControlService
{
    private const ushort VkMediaNextTrack = 0xB0;
    private const ushort VkMediaPreviousTrack = 0xB1;
    private const ushort VkMediaStop = 0xB2;
    private const ushort VkMediaPlayPause = 0xB3;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const int WmAppCommand = 0x0319;
    private const int AppCommandMediaNextTrack = 11;
    private const int AppCommandMediaPreviousTrack = 12;
    private const int AppCommandMediaStop = 13;
    private const int AppCommandMediaPlayPause = 14;
    private const int AppCommandMediaFastForward = 49;
    private const int AppCommandMediaRewind = 50;
    private const int FAppCommandKey = 0;

    public async Task<MediaControlResult> ExecuteAsync(HotKeyAction action, int seekSeconds)
    {
        try
        {
            if (action is HotKeyAction.MediaPlayPause or HotKeyAction.MediaPrevious or HotKeyAction.MediaNext or HotKeyAction.MediaStop)
            {
                var mediaKeyResult = TryExecuteWithLegacyMediaCommand(action);
                if (mediaKeyResult.Succeeded)
                {
                    return mediaKeyResult;
                }
            }

            var gsmtcResult = await TryExecuteWithSystemMediaTransportControlsAsync(action, seekSeconds);
            if (gsmtcResult.Succeeded)
            {
                return gsmtcResult;
            }

            var fallbackResult = TryExecuteWithLegacyMediaCommand(action);
            if (fallbackResult.Succeeded)
            {
                return fallbackResult;
            }

            return MediaControlResult.Failure($"{gsmtcResult.Message}；通用媒体键兜底也失败：{fallbackResult.Message}");
        }
        catch (Exception ex)
        {
            return MediaControlResult.Failure(ex.Message);
        }
    }

    private static async Task<MediaControlResult> TryExecuteWithSystemMediaTransportControlsAsync(HotKeyAction action, int seekSeconds)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var sessions = new List<GlobalSystemMediaTransportControlsSession>();
        var currentSession = manager.GetCurrentSession();
        if (currentSession is not null)
        {
            sessions.Add(currentSession);
        }

        sessions.AddRange(manager.GetSessions()
            .Where(session => currentSession is null || !IsSameSession(session, currentSession))
            .OrderByDescending(static session => session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing));

        if (sessions.Count == 0)
        {
            return MediaControlResult.Failure("未发现系统媒体会话");
        }

        var failures = new List<string>();
        foreach (var session in sessions)
        {
            var result = await TryExecuteWithSessionAsync(session, action, seekSeconds);
            if (result.Succeeded)
            {
                return result;
            }

            failures.Add($"{GetSessionName(session)}：{result.Message}");
        }

        return MediaControlResult.Failure(string.Join("；", failures));
    }

    private static async Task<MediaControlResult> TryExecuteWithSessionAsync(
        GlobalSystemMediaTransportControlsSession session,
        HotKeyAction action,
        int seekSeconds)
    {
        var controls = session.GetPlaybackInfo().Controls;

        return action switch
        {
            HotKeyAction.MediaPlayPause => await TryTogglePlayPauseAsync(session, controls),
            HotKeyAction.MediaPrevious => await TryControlAsync(controls.IsPreviousEnabled, session.TrySkipPreviousAsync, "当前媒体会话不支持上一曲"),
            HotKeyAction.MediaNext => await TryControlAsync(controls.IsNextEnabled, session.TrySkipNextAsync, "当前媒体会话不支持下一曲"),
            HotKeyAction.MediaStop => await TryControlAsync(controls.IsStopEnabled, session.TryStopAsync, "当前媒体会话不支持停止"),
            HotKeyAction.MediaRewind => await TrySeekAsync(session, -Math.Clamp(seekSeconds, 1, 60)),
            HotKeyAction.MediaFastForward => await TrySeekAsync(session, Math.Clamp(seekSeconds, 1, 60)),
            _ => MediaControlResult.Failure($"{action} 不是媒体控制动作")
        };
    }

    private static bool IsSameSession(
        GlobalSystemMediaTransportControlsSession left,
        GlobalSystemMediaTransportControlsSession right)
    {
        return string.Equals(left.SourceAppUserModelId, right.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSessionName(GlobalSystemMediaTransportControlsSession session)
    {
        return string.IsNullOrWhiteSpace(session.SourceAppUserModelId)
            ? "未知媒体会话"
            : session.SourceAppUserModelId;
    }

    private static async Task<MediaControlResult> TryTogglePlayPauseAsync(
        GlobalSystemMediaTransportControlsSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackControls controls)
    {
        var status = session.GetPlaybackInfo().PlaybackStatus;
        if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled)
        {
            return await TryControlAsync(true, session.TryPauseAsync, "当前媒体会话不支持暂停", MediaPlaybackToggleState.Paused);
        }

        if (controls.IsPlayEnabled)
        {
            return await TryControlAsync(true, session.TryPlayAsync, "当前媒体会话不支持播放", MediaPlaybackToggleState.Playing);
        }

        if (controls.IsPlayPauseToggleEnabled)
        {
            var toggledToState = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                ? MediaPlaybackToggleState.Paused
                : MediaPlaybackToggleState.Playing;
            return await TryControlAsync(true, session.TryTogglePlayPauseAsync, "当前媒体会话不支持播放/暂停切换", toggledToState);
        }

        return MediaControlResult.Failure("当前媒体会话不支持播放/暂停");
    }

    private static async Task<MediaControlResult> TryControlAsync(
        bool isEnabled,
        Func<Windows.Foundation.IAsyncOperation<bool>> operation,
        string unsupportedMessage,
        MediaPlaybackToggleState playbackToggleState = MediaPlaybackToggleState.Unknown)
    {
        if (!isEnabled)
        {
            return MediaControlResult.Failure(unsupportedMessage);
        }

        var succeeded = await operation();
        return succeeded
            ? MediaControlResult.Success("系统媒体会话控制成功", playbackToggleState)
            : MediaControlResult.Failure(unsupportedMessage);
    }

    private static async Task<MediaControlResult> TrySeekAsync(GlobalSystemMediaTransportControlsSession session, int deltaSeconds)
    {
        var controls = session.GetPlaybackInfo().Controls;
        if (!controls.IsPlaybackPositionEnabled)
        {
            if (deltaSeconds < 0 && controls.IsRewindEnabled)
            {
                return await TryControlAsync(true, session.TryRewindAsync, "当前媒体会话不支持回退");
            }

            if (deltaSeconds > 0 && controls.IsFastForwardEnabled)
            {
                return await TryControlAsync(true, session.TryFastForwardAsync, "当前媒体会话不支持快进");
            }

            return MediaControlResult.Failure("当前媒体会话不支持按秒快进/回退");
        }

        var timeline = session.GetTimelineProperties();
        var requestedPosition = timeline.Position + TimeSpan.FromSeconds(deltaSeconds);
        var minimum = timeline.MinSeekTime > TimeSpan.Zero ? timeline.MinSeekTime : timeline.StartTime;
        var maximum = timeline.MaxSeekTime > TimeSpan.Zero ? timeline.MaxSeekTime : timeline.EndTime;

        if (maximum > minimum)
        {
            requestedPosition = requestedPosition < minimum ? minimum : requestedPosition;
            requestedPosition = requestedPosition > maximum ? maximum : requestedPosition;
        }
        else if (requestedPosition < TimeSpan.Zero)
        {
            requestedPosition = TimeSpan.Zero;
        }

        var succeeded = await session.TryChangePlaybackPositionAsync(requestedPosition.Ticks);
        return succeeded
            ? MediaControlResult.Success("系统媒体会话按秒跳转成功")
            : MediaControlResult.Failure("当前媒体会话拒绝了快进/回退请求");
    }

    private static MediaControlResult TryExecuteWithLegacyMediaCommand(HotKeyAction action)
    {
        var virtualKey = action switch
        {
            HotKeyAction.MediaPlayPause => VkMediaPlayPause,
            HotKeyAction.MediaPrevious => VkMediaPreviousTrack,
            HotKeyAction.MediaNext => VkMediaNextTrack,
            HotKeyAction.MediaStop => VkMediaStop,
            _ => (ushort)0
        };

        var mediaKeySent = virtualKey != 0 && TrySendMediaKey(virtualKey);

        var appCommand = action switch
        {
            HotKeyAction.MediaPlayPause => AppCommandMediaPlayPause,
            HotKeyAction.MediaPrevious => AppCommandMediaPreviousTrack,
            HotKeyAction.MediaNext => AppCommandMediaNextTrack,
            HotKeyAction.MediaStop => AppCommandMediaStop,
            HotKeyAction.MediaRewind => AppCommandMediaRewind,
            HotKeyAction.MediaFastForward => AppCommandMediaFastForward,
            _ => 0
        };

        var appCommandSent = appCommand != 0 && TrySendAppCommand(appCommand);
        if (mediaKeySent || appCommandSent)
        {
            return MediaControlResult.Success(
                "已发送通用系统媒体键",
                action == HotKeyAction.MediaPlayPause ? MediaPlaybackToggleState.Toggled : MediaPlaybackToggleState.Unknown);
        }

        return appCommand == 0
            ? MediaControlResult.Failure("没有可用的通用媒体命令")
            : MediaControlResult.Failure($"通用媒体命令发送失败（Win32 错误 {Marshal.GetLastWin32Error()}）");
    }

    private static bool TrySendMediaKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey, 0),
            CreateKeyboardInput(virtualKey, KeyEventFKeyUp)
        };

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            Anonymous = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags
                }
            }
        };
    }

    private static bool TrySendAppCommand(int appCommand)
    {
        var lParam = (nint)((appCommand << 16) | FAppCommandKey);
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != 0 &&
            SendMessageTimeout(
                foregroundWindow,
                WmAppCommand,
                0,
                lParam,
                SendMessageTimeoutFlags.SMTO_ABORTIFHUNG,
                100,
                out _) != 0)
        {
            return true;
        }

        return SendMessageTimeout(
            HWND_BROADCAST,
            WmAppCommand,
            0,
            lParam,
            SendMessageTimeoutFlags.SMTO_ABORTIFHUNG,
            100,
            out _) != 0;
    }

    private static readonly nint HWND_BROADCAST = new(0xffff);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION Anonymous;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [Flags]
    private enum SendMessageTimeoutFlags : uint
    {
        SMTO_ABORTIFHUNG = 0x0002
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint hWnd,
        int msg,
        nint wParam,
        nint lParam,
        SendMessageTimeoutFlags fuFlags,
        uint uTimeout,
        out nint lpdwResult);
}

public enum MediaPlaybackToggleState
{
    Unknown,
    Playing,
    Paused,
    Toggled
}

public sealed record MediaControlResult(bool Succeeded, string? Message, MediaPlaybackToggleState PlaybackToggleState = MediaPlaybackToggleState.Unknown)
{
    public static MediaControlResult Success(string? message = null, MediaPlaybackToggleState playbackToggleState = MediaPlaybackToggleState.Unknown)
    {
        return new MediaControlResult(true, message, playbackToggleState);
    }

    public static MediaControlResult Failure(string message)
    {
        return new MediaControlResult(false, message);
    }
}
