using FControl.Models;
using Windows.Media.Control;

namespace FControl.Services;

public sealed class MediaControlService
{
    public async Task<MediaControlResult> ExecuteAsync(HotKeyAction action, int seekSeconds)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session is null)
            {
                return MediaControlResult.Failure("没有可控制的媒体会话。请先打开支持系统媒体控制的播放器。");
            }

            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;

            return action switch
            {
                HotKeyAction.MediaPlayPause => await TryTogglePlayPauseAsync(session, controls),
                HotKeyAction.MediaPrevious => await TryControlAsync(controls.IsPreviousEnabled, session.TrySkipPreviousAsync, "当前媒体会话不支持上一曲。"),
                HotKeyAction.MediaNext => await TryControlAsync(controls.IsNextEnabled, session.TrySkipNextAsync, "当前媒体会话不支持下一曲。"),
                HotKeyAction.MediaStop => await TryControlAsync(controls.IsStopEnabled, session.TryStopAsync, "当前媒体会话不支持停止。"),
                HotKeyAction.MediaRewind => await TrySeekAsync(session, -Math.Clamp(seekSeconds, 1, 60)),
                HotKeyAction.MediaFastForward => await TrySeekAsync(session, Math.Clamp(seekSeconds, 1, 60)),
                _ => MediaControlResult.Failure($"{action} 不是媒体控制动作。")
            };
        }
        catch (Exception ex)
        {
            return MediaControlResult.Failure(ex.Message);
        }
    }

    private static async Task<MediaControlResult> TryTogglePlayPauseAsync(
        GlobalSystemMediaTransportControlsSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackControls controls)
    {
        if (controls.IsPlayPauseToggleEnabled)
        {
            return await TryControlAsync(true, session.TryTogglePlayPauseAsync, "当前媒体会话不支持播放/暂停切换。");
        }

        var status = session.GetPlaybackInfo().PlaybackStatus;
        if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled)
        {
            return await TryControlAsync(true, session.TryPauseAsync, "当前媒体会话不支持暂停。");
        }

        if (controls.IsPlayEnabled)
        {
            return await TryControlAsync(true, session.TryPlayAsync, "当前媒体会话不支持播放。");
        }

        return MediaControlResult.Failure("当前媒体会话不支持播放/暂停。");
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
            ? MediaControlResult.Success()
            : MediaControlResult.Failure(unsupportedMessage);
    }

    private static async Task<MediaControlResult> TrySeekAsync(GlobalSystemMediaTransportControlsSession session, int deltaSeconds)
    {
        var controls = session.GetPlaybackInfo().Controls;
        if (!controls.IsPlaybackPositionEnabled)
        {
            if (deltaSeconds < 0 && controls.IsRewindEnabled)
            {
                return await TryControlAsync(true, session.TryRewindAsync, "当前媒体会话不支持回退。");
            }

            if (deltaSeconds > 0 && controls.IsFastForwardEnabled)
            {
                return await TryControlAsync(true, session.TryFastForwardAsync, "当前媒体会话不支持快进。");
            }

            return MediaControlResult.Failure("当前媒体会话不支持按秒快进/回退。");
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
            ? MediaControlResult.Success()
            : MediaControlResult.Failure("当前媒体会话拒绝了快进/回退请求。");
    }
}

public sealed record MediaControlResult(bool Succeeded, string? Message)
{
    public static MediaControlResult Success()
    {
        return new MediaControlResult(true, null);
    }

    public static MediaControlResult Failure(string message)
    {
        return new MediaControlResult(false, message);
    }
}
