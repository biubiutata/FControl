using System.Diagnostics;
using FControl.Models;

namespace FControl.Services;

public sealed class HotKeyActionService
{
    private readonly AppConfigurationService _configurationService;
    private readonly SystemVolumeService _volumeService = new();
    private readonly MediaControlService _mediaService = new();
    private readonly MonitorBrightnessService _brightnessService = new();

    public HotKeyActionService(AppConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public event EventHandler<ActionExecutedEventArgs>? ActionExecuted;

    public async void Execute(KeyMappingConfig mapping)
    {
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

    private async Task<ControlActionResult> ExecuteAsync(KeyMappingConfig mapping)
    {
        return mapping.Action switch
        {
            HotKeyAction.Disabled => ControlActionResult.Success("已禁用"),
            HotKeyAction.VolumeDown => ExecuteVolume(-_configurationService.Current.VolumeStepPercent),
            HotKeyAction.VolumeUp => ExecuteVolume(_configurationService.Current.VolumeStepPercent),
            HotKeyAction.MuteToggle => ExecuteMuteToggle(),
            HotKeyAction.BrightnessDown => ExecuteBrightness(-_configurationService.Current.BrightnessStepPercent),
            HotKeyAction.BrightnessUp => ExecuteBrightness(_configurationService.Current.BrightnessStepPercent),
            HotKeyAction.MediaPlayPause or
                HotKeyAction.MediaPrevious or
                HotKeyAction.MediaNext or
                HotKeyAction.MediaStop or
                HotKeyAction.MediaRewind or
                HotKeyAction.MediaFastForward => await ExecuteMediaAsync(mapping),
            _ => ControlActionResult.Failure($"未支持的动作：{mapping.Action}")
        };
    }

    private ControlActionResult ExecuteVolume(int deltaPercent)
    {
        var result = _volumeService.ChangeVolumeByPercent(deltaPercent);
        return result.Succeeded
            ? ControlActionResult.Success(
                result.IsMuted ? $"静音，音量 {result.VolumePercent}%" : $"音量 {result.VolumePercent}%",
                result.VolumePercent,
                result.IsMuted)
            : ControlActionResult.Failure($"音量控制失败：{result.Message}");
    }

    private ControlActionResult ExecuteMuteToggle()
    {
        var result = _volumeService.ToggleMute();
        return result.Succeeded
            ? ControlActionResult.Success(
                result.IsMuted ? "已静音" : $"已取消静音，音量 {result.VolumePercent}%",
                result.VolumePercent,
                result.IsMuted)
            : ControlActionResult.Failure($"静音切换失败：{result.Message}");
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
            ? ControlActionResult.Success(GetMediaSuccessMessage(mapping))
            : ControlActionResult.Failure($"媒体控制失败：{result.Message}");
    }

    private static string GetMediaSuccessMessage(KeyMappingConfig mapping)
    {
        return mapping.Action switch
        {
            HotKeyAction.MediaRewind => $"回退 {mapping.SeekSeconds} 秒",
            HotKeyAction.MediaFastForward => $"快进 {mapping.SeekSeconds} 秒",
            _ => HotKeyActionMetadata.GetDisplayName(mapping.Action)
        };
    }
}

public sealed record ControlActionResult(bool Succeeded, string Message, int? LevelPercent = null, bool? IsMuted = null)
{
    public static ControlActionResult Success(string message, int? levelPercent = null, bool? isMuted = null)
    {
        return new ControlActionResult(true, message, levelPercent, isMuted);
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
