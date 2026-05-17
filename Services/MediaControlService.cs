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
            var gsmtcResult = await TryExecuteWithSystemMediaTransportControlsAsync(action, seekSeconds);
            if (gsmtcResult.Succeeded)
            {
                return gsmtcResult;
            }

            var fallbackResult = TryExecuteWithLegacyMediaCommand(action);
            if (fallbackResult.Succeeded)
            {
                return MediaControlResult.Success(fallbackResult.Message);
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
        var session = manager.GetCurrentSession();
        if (session is null)
        {
            return MediaControlResult.Failure("未发现系统媒体会话");
        }

        var playbackInfo = session.GetPlaybackInfo();
        var controls = playbackInfo.Controls;

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

    private static async Task<MediaControlResult> TryTogglePlayPauseAsync(
        GlobalSystemMediaTransportControlsSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackControls controls)
    {
        if (controls.IsPlayPauseToggleEnabled)
        {
            return await TryControlAsync(true, session.TryTogglePlayPauseAsync, "当前媒体会话不支持播放/暂停切换");
        }

        var status = session.GetPlaybackInfo().PlaybackStatus;
        if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled)
        {
            return await TryControlAsync(true, session.TryPauseAsync, "当前媒体会话不支持暂停");
        }

        if (controls.IsPlayEnabled)
        {
            return await TryControlAsync(true, session.TryPlayAsync, "当前媒体会话不支持播放");
        }

        return MediaControlResult.Failure("当前媒体会话不支持播放/暂停");
    }

    private static async Task<MediaControlResult> TryControlAsync(
        bool isEnabled,
        Func<Windows.Foundation.IAsyncOperation<bool>> operation,
        string unsupportedMessage)
    {
        if (!isEnabled)
        {
            return MediaControlResult.Failure(unsupportedMessage);
        }

        var succeeded = await operation();
        return succeeded
            ? MediaControlResult.Success("系统媒体会话控制成功")
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

        if (virtualKey != 0 && TrySendMediaKey(virtualKey))
        {
            return MediaControlResult.Success("未发现可用系统媒体会话，已发送通用系统媒体键");
        }

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

        if (appCommand == 0)
        {
            return MediaControlResult.Failure("没有可用的通用媒体命令");
        }

        return TrySendAppCommand(appCommand)
            ? MediaControlResult.Success("未发现可用系统媒体会话，已发送 WM_APPCOMMAND 通用媒体命令")
            : MediaControlResult.Failure($"WM_APPCOMMAND 发送失败（Win32 错误 {Marshal.GetLastWin32Error()}）");
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
        public KEYBDINPUT ki;
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

    [Flags]
    private enum SendMessageTimeoutFlags : uint
    {
        SMTO_ABORTIFHUNG = 0x0002
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

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

public sealed record MediaControlResult(bool Succeeded, string? Message)
{
    public static MediaControlResult Success(string? message = null)
    {
        return new MediaControlResult(true, message);
    }

    public static MediaControlResult Failure(string message)
    {
        return new MediaControlResult(false, message);
    }
}
