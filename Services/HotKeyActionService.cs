using System.Diagnostics;
using FControl.Models;

namespace FControl.Services;

public sealed class HotKeyActionService
{
    private readonly AppConfigurationService _configurationService;
    private readonly SystemVolumeService _volumeService = new();
    private readonly MediaControlService _mediaService = new();
    private readonly MonitorBrightnessService _brightnessService = new();
    private readonly ScriptExecutionService _scriptService;
    private readonly SemaphoreSlim _brightnessGate = new(1, 1);
    private readonly object _brightnessStateGate = new();
    private int? _lastBrightnessPercent;
    private long _brightnessRequestVersion;

    public HotKeyActionService(AppConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _scriptService = new ScriptExecutionService(configurationService);
        _ = InitializeBrightnessCacheAsync();
    }

    public event EventHandler<ActionExecutedEventArgs>? ActionExecuted;
    public event EventHandler<ScriptActionExecutedEventArgs>? ScriptActionExecuted;

    public async void Execute(KeyMappingConfig mapping)
    {
        if (mapping.Action is HotKeyAction.BrightnessDown or HotKeyAction.BrightnessUp)
        {
            await ExecuteBrightnessMappingAsync(mapping);
            return;
        }

        try
        {
            AppServices.Log.Info($"执行动作：{mapping.Key} -> {mapping.Action}");
            var result = await ExecuteAsync(mapping);
            AppServices.Log.Info($"动作结果：{mapping.Key} -> {(result.Succeeded ? "成功" : "失败")}，{result.Message}");
            ActionExecuted?.Invoke(this, new ActionExecutedEventArgs(mapping.Clone(), result));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FControl action failed: {ex}");
            AppServices.Log.Error($"动作异常：{mapping.Key} -> {mapping.Action}，{ex.Message}");
            ActionExecuted?.Invoke(this, new ActionExecutedEventArgs(mapping.Clone(), ControlActionResult.Failure(ex.Message)));
        }
    }

    public async void Execute(CustomHotkeyConfig hotkey)
    {
        try
        {
            AppServices.Log.Info($"执行脚本：{hotkey.Hotkey} -> {hotkey.Name}");
            var result = await _scriptService.ExecuteAsync(hotkey);
            _configurationService.AddScriptRunResult(result);
            AppServices.Log.Info($"脚本结果：{hotkey.Hotkey} -> {(result.Succeeded ? "成功" : "失败")}，{result.Message}");
            ScriptActionExecuted?.Invoke(this, new ScriptActionExecutedEventArgs(hotkey.Clone(), result));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FControl script failed: {ex}");
            var result = new ScriptRunResult
            {
                HotkeyId = hotkey.Id,
                HotkeyName = hotkey.Name,
                Hotkey = hotkey.Hotkey,
                ScriptType = hotkey.ScriptType,
                Succeeded = false,
                Message = ex.Message
            };
            _configurationService.AddScriptRunResult(result);
            ScriptActionExecuted?.Invoke(this, new ScriptActionExecutedEventArgs(hotkey.Clone(), result));
        }
    }

    public Task<ScriptRunResult> TestScriptAsync(CustomHotkeyConfig hotkey)
    {
        return _scriptService.TestAsync(hotkey);
    }

    private async Task<ControlActionResult> ExecuteAsync(KeyMappingConfig mapping)
    {
        return mapping.Action switch
        {
            HotKeyAction.Disabled => ControlActionResult.Success("已禁用"),
            HotKeyAction.VolumeDown => ExecuteSystemVolumeKey(HotKeyAction.VolumeDown),
            HotKeyAction.VolumeUp => ExecuteSystemVolumeKey(HotKeyAction.VolumeUp),
            HotKeyAction.MuteToggle => ExecuteSystemVolumeKey(HotKeyAction.MuteToggle),
            HotKeyAction.MediaPlayPause or
                HotKeyAction.MediaPrevious or
                HotKeyAction.MediaNext or
                HotKeyAction.MediaStop or
                HotKeyAction.MediaRewind or
                HotKeyAction.MediaFastForward => await ExecuteMediaAsync(mapping),
            _ => ControlActionResult.Failure($"未支持的动作：{mapping.Action}")
        };
    }

    private async Task InitializeBrightnessCacheAsync()
    {
        try
        {
            var brightnessPercent = await Task.Run(_brightnessService.GetPrimaryBrightnessPercent);
            if (brightnessPercent is null)
            {
                return;
            }

            lock (_brightnessStateGate)
            {
                _lastBrightnessPercent ??= brightnessPercent;
            }
        }
        catch
        {
            // 亮度探测失败时不影响热键；首次实际调节成功后会回填缓存。
        }
    }

    private async Task ExecuteBrightnessMappingAsync(KeyMappingConfig mapping)
    {
        try
        {
            AppServices.Log.Info($"执行动作：{mapping.Key} -> {mapping.Action}");

            var deltaPercent = mapping.Action == HotKeyAction.BrightnessUp
                ? _configurationService.Current.BrightnessStepPercent
                : -_configurationService.Current.BrightnessStepPercent;
            var requestVersion = Interlocked.Increment(ref _brightnessRequestVersion);
            var previewPublished = TryPublishBrightnessPreview(mapping, deltaPercent);

            await _brightnessGate.WaitAsync();
            ControlActionResult result;
            try
            {
                result = await Task.Run(() => ExecuteBrightness(deltaPercent));
            }
            finally
            {
                _brightnessGate.Release();
            }

            var isLatestRequest = requestVersion == System.Threading.Volatile.Read(ref _brightnessRequestVersion);
            if (result.Succeeded && result.LevelPercent is { } levelPercent && isLatestRequest)
            {
                lock (_brightnessStateGate)
                {
                    _lastBrightnessPercent = levelPercent;
                }
            }

            AppServices.Log.Info($"动作结果：{mapping.Key} -> {(result.Succeeded ? "成功" : "失败")}，{result.Message}");
            if (isLatestRequest || !previewPublished)
            {
                ActionExecuted?.Invoke(this, new ActionExecutedEventArgs(mapping.Clone(), result));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FControl action failed: {ex}");
            AppServices.Log.Error($"动作异常：{mapping.Key} -> {mapping.Action}，{ex.Message}");
            ActionExecuted?.Invoke(this, new ActionExecutedEventArgs(mapping.Clone(), ControlActionResult.Failure(ex.Message)));
        }
    }

    private bool TryPublishBrightnessPreview(KeyMappingConfig mapping, int deltaPercent)
    {
        int nextBrightness;
        lock (_brightnessStateGate)
        {
            if (_lastBrightnessPercent is null)
            {
                return false;
            }

            nextBrightness = Math.Clamp(_lastBrightnessPercent.Value + deltaPercent, 0, 100);
            _lastBrightnessPercent = nextBrightness;
        }

        ActionExecuted?.Invoke(
            this,
            new ActionExecutedEventArgs(
                mapping.Clone(),
                ControlActionResult.Success($"亮度 {nextBrightness}%（正在应用）", nextBrightness)));
        return true;
    }

    private ControlActionResult ExecuteSystemVolumeKey(HotKeyAction action)
    {
        var result = action switch
        {
            HotKeyAction.VolumeDown => _volumeService.SendVolumeDownKey(),
            HotKeyAction.VolumeUp => _volumeService.SendVolumeUpKey(),
            HotKeyAction.MuteToggle => _volumeService.SendMuteKey(),
            _ => VolumeControlResult.Failure($"不是音量动作：{action}")
        };

        return result.Succeeded
            ? ControlActionResult.Success(
                result.IsMuted ? $"系统音量键已发送：静音，音量 {result.VolumePercent}%" : $"系统音量键已发送：音量 {result.VolumePercent}%",
                result.VolumePercent,
                result.IsMuted)
            : ControlActionResult.Failure($"系统音量键发送失败：{result.Message}");
    }

    private ControlActionResult ExecuteBrightness(int deltaPercent)
    {
        var result = _brightnessService.ChangeBrightnessByPercent(deltaPercent);
        if (!result.Succeeded)
        {
            return ControlActionResult.Failure($"亮度控制失败：{result.Message}");
        }

        var message = $"亮度 {result.BrightnessPercent}%（{result.ControlledMonitorCount} 台显示器）";
        if (result.UnsupportedMonitorCount > 0)
        {
            message += $"，{result.UnsupportedMonitorCount} 台不支持 DDC/CI";
        }

        return ControlActionResult.Success(message, result.BrightnessPercent);
    }

    private async Task<ControlActionResult> ExecuteMediaAsync(KeyMappingConfig mapping)
    {
        var result = await _mediaService.ExecuteAsync(mapping.Action, mapping.SeekSeconds);
        return result.Succeeded
            ? ControlActionResult.Success(GetMediaSuccessMessage(mapping, result), playbackToggleState: result.PlaybackToggleState)
            : ControlActionResult.Failure($"媒体控制失败：{result.Message}");
    }

    private static string GetMediaSuccessMessage(KeyMappingConfig mapping, MediaControlResult result)
    {
        return mapping.Action switch
        {
            HotKeyAction.MediaRewind => $"回退 {mapping.SeekSeconds} 秒",
            HotKeyAction.MediaFastForward => $"快进 {mapping.SeekSeconds} 秒",
            HotKeyAction.MediaPlayPause when result.PlaybackToggleState == MediaPlaybackToggleState.Playing => "已播放",
            HotKeyAction.MediaPlayPause when result.PlaybackToggleState == MediaPlaybackToggleState.Paused => "已暂停",
            HotKeyAction.MediaPlayPause when result.PlaybackToggleState == MediaPlaybackToggleState.Toggled => "已切换播放/暂停",
            _ => HotKeyActionMetadata.GetDisplayName(mapping.Action)
        };
    }
}

public sealed record ControlActionResult(
    bool Succeeded,
    string Message,
    int? LevelPercent = null,
    bool? IsMuted = null,
    MediaPlaybackToggleState PlaybackToggleState = MediaPlaybackToggleState.Unknown)
{
    public static ControlActionResult Success(
        string message,
        int? levelPercent = null,
        bool? isMuted = null,
        MediaPlaybackToggleState playbackToggleState = MediaPlaybackToggleState.Unknown)
    {
        return new ControlActionResult(true, message, levelPercent, isMuted, playbackToggleState);
    }

    public static ControlActionResult Failure(string message)
    {
        return new ControlActionResult(false, message);
    }
}

public sealed class ActionExecutedEventArgs(KeyMappingConfig mapping, ControlActionResult result) : EventArgs
{
    public KeyMappingConfig Mapping { get; } = mapping;
    public ControlActionResult Result { get; } = result;
}

public sealed class ScriptActionExecutedEventArgs(CustomHotkeyConfig hotkey, ScriptRunResult result) : EventArgs
{
    public CustomHotkeyConfig Hotkey { get; } = hotkey;
    public ScriptRunResult Result { get; } = result;
}
