using System.Diagnostics;
using System.Text;
using FControl.Models;

namespace FControl.Services;

public sealed class ScriptExecutionService
{
    private readonly AppConfigurationService _configurationService;
    private readonly Dictionary<string, RunningScript> _runningScripts = [];
    private readonly object _gate = new();

    public ScriptExecutionService(AppConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<ScriptRunResult> ExecuteAsync(CustomHotkeyConfig hotkey)
    {
        var config = hotkey.Clone();
        RunningScript? runningToKill = null;
        CancellationTokenSource? ignoredCts = null;

        lock (_gate)
        {
            if (_runningScripts.TryGetValue(config.Id, out var running))
            {
                switch (config.ConcurrencyPolicy)
                {
                    case ConcurrencyPolicy.IgnoreIfRunning:
                        ignoredCts = null;
                        return CreateImmediateResult(config, false, "上一次仍在运行，已忽略本次触发。", timedOut: false);
                    case ConcurrencyPolicy.RestartPrevious:
                        runningToKill = running;
                        break;
                }
            }
        }

        runningToKill?.CancelAndKill();

        var cts = new CancellationTokenSource();
        var runningScript = new RunningScript(cts);
        var shouldTrack = config.ConcurrencyPolicy != ConcurrencyPolicy.AllowConcurrent;
        if (shouldTrack)
        {
            lock (_gate)
            {
                _runningScripts[config.Id] = runningScript;
            }
        }

        try
        {
            return await ExecuteCoreAsync(config, runningScript, cts.Token);
        }
        finally
        {
            ignoredCts?.Dispose();
            if (shouldTrack)
            {
                lock (_gate)
                {
                    if (_runningScripts.TryGetValue(config.Id, out var current) && ReferenceEquals(current, runningScript))
                    {
                        _runningScripts.Remove(config.Id);
                    }
                }
            }

            cts.Dispose();
        }
    }

    public async Task<ScriptRunResult> TestAsync(CustomHotkeyConfig hotkey)
    {
        return await ExecuteCoreAsync(hotkey.Clone(), new RunningScript(new CancellationTokenSource()), CancellationToken.None);
    }

    private async Task<ScriptRunResult> ExecuteCoreAsync(CustomHotkeyConfig config, RunningScript runningScript, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var tempFile = string.Empty;

        try
        {
            var command = BuildCommand(config, out tempFile);
            if (!command.Succeeded)
            {
                return CreateResult(config, startedAt, stopwatch.Elapsed, false, false, null, command.Message, string.Empty, string.Empty);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 3600)));

            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = false,
                CreateNoWindow = config.RunWindowMode == RunWindowMode.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = command.WorkingDirectory
            };

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            runningScript.Process = process;
            AppServices.Log.Info($"脚本启动：{config.Name}，{command.FileName} {command.Arguments}");

            if (!process.Start())
            {
                return CreateResult(config, startedAt, stopwatch.Elapsed, false, false, null, "进程启动失败。", string.Empty, string.Empty);
            }

            if (config.RunWindowMode == RunWindowMode.Minimized)
            {
                _ = TryMinimizeAfterStartAsync(process);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                stopwatch.Stop();
                return CreateResult(config, startedAt, stopwatch.Elapsed, false, true, null, "脚本执行超时，已尝试终止进程。", await SafeReadAsync(outputTask), await SafeReadAsync(errorTask));
            }

            stopwatch.Stop();
            var output = await SafeReadAsync(outputTask);
            var error = await SafeReadAsync(errorTask);
            var succeeded = process.ExitCode == 0;
            var message = succeeded ? "脚本执行成功。" : $"脚本执行失败，退出码 {process.ExitCode}。";
            return CreateResult(config, startedAt, stopwatch.Elapsed, succeeded, false, process.ExitCode, message, output, error);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CreateResult(config, startedAt, stopwatch.Elapsed, false, false, null, ex.Message, string.Empty, string.Empty);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // ignore cleanup failures
                }
            }
        }
    }

    private ScriptCommand BuildCommand(CustomHotkeyConfig config, out string tempFile)
    {
        tempFile = string.Empty;
        var workingDirectory = ResolveWorkingDirectory(config);
        var content = config.ScriptMode == ScriptMode.File ? config.ScriptPath.Trim() : config.InlineCode;
        if (string.IsNullOrWhiteSpace(content))
        {
            return ScriptCommand.Failure(config.ScriptMode == ScriptMode.File ? "脚本文件路径不能为空。" : "内联代码/命令不能为空。");
        }

        if (config.ScriptMode == ScriptMode.File)
        {
            content = Environment.ExpandEnvironmentVariables(content.Trim('"'));
            if (!File.Exists(content) && config.ScriptType != ScriptType.ExternalProgram)
            {
                return ScriptCommand.Failure($"脚本文件不存在：{content}");
            }
        }

        switch (config.ScriptType)
        {
            case ScriptType.WindowsShell:
                return config.ScriptMode == ScriptMode.File
                    ? ScriptCommand.Success("cmd.exe", $"/c {Quote(content)} {config.Arguments}".Trim(), workingDirectory)
                    : ScriptCommand.Success("cmd.exe", $"/c {content}", workingDirectory);
            case ScriptType.PowerShell:
            {
                var exe = ResolvePowerShell();
                if (config.ScriptMode == ScriptMode.File)
                {
                    return ScriptCommand.Success(exe, $"-NoProfile -ExecutionPolicy RemoteSigned -File {Quote(content)} {config.Arguments}".Trim(), workingDirectory);
                }

                tempFile = WriteTempScript(config, ".ps1", content);
                return ScriptCommand.Success(exe, $"-NoProfile -ExecutionPolicy RemoteSigned -File {Quote(tempFile)} {config.Arguments}".Trim(), workingDirectory);
            }
            case ScriptType.Bash:
            {
                var exe = ResolveConfiguredPath(_configurationService.Current.RuntimePaths.BashPath, "bash");
                if (config.ScriptMode == ScriptMode.File)
                {
                    return ScriptCommand.Success(exe, $"{Quote(content)} {config.Arguments}".Trim(), workingDirectory);
                }

                return ScriptCommand.Success(exe, $"-lc {Quote(content)}", workingDirectory);
            }
            case ScriptType.Python:
            {
                var exe = ResolveConfiguredPath(_configurationService.Current.RuntimePaths.PythonPath, "python");
                if (config.ScriptMode == ScriptMode.File)
                {
                    return ScriptCommand.Success(exe, $"{Quote(content)} {config.Arguments}".Trim(), workingDirectory);
                }

                tempFile = WriteTempScript(config, ".py", content);
                return ScriptCommand.Success(exe, $"{Quote(tempFile)} {config.Arguments}".Trim(), workingDirectory);
            }
            case ScriptType.NodeJs:
            {
                var exe = ResolveConfiguredPath(_configurationService.Current.RuntimePaths.NodePath, "node");
                if (config.ScriptMode == ScriptMode.File)
                {
                    return ScriptCommand.Success(exe, $"{Quote(content)} {config.Arguments}".Trim(), workingDirectory);
                }

                tempFile = WriteTempScript(config, ".js", content);
                return ScriptCommand.Success(exe, $"{Quote(tempFile)} {config.Arguments}".Trim(), workingDirectory);
            }
            case ScriptType.ExternalProgram:
            {
                var exe = config.ScriptMode == ScriptMode.File ? content : content.Trim();
                if (LooksLikePath(exe) && !File.Exists(exe))
                {
                    return ScriptCommand.Failure($"外部程序不存在：{exe}");
                }

                return ScriptCommand.Success(exe, config.Arguments, workingDirectory);
            }
            default:
                return ScriptCommand.Failure("不支持的脚本类型。");
        }
    }

    private string ResolvePowerShell()
    {
        var configured = _configurationService.Current.RuntimePaths.PowerShellPath;
        return ResolveConfiguredPath(configured, "powershell.exe");
    }

    private static string ResolveConfiguredPath(string configuredPath, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return fallback;
        }

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim('"'));
        return expanded;
    }

    private static string ResolveWorkingDirectory(CustomHotkeyConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.WorkingDirectory))
        {
            var expanded = Environment.ExpandEnvironmentVariables(config.WorkingDirectory.Trim('"'));
            if (Directory.Exists(expanded))
            {
                return expanded;
            }
        }

        if (config.ScriptMode == ScriptMode.File && !string.IsNullOrWhiteSpace(config.ScriptPath))
        {
            var scriptPath = Environment.ExpandEnvironmentVariables(config.ScriptPath.Trim('"'));
            var directory = Path.GetDirectoryName(scriptPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return AppContext.BaseDirectory;
    }

    private static string WriteTempScript(CustomHotkeyConfig config, string extension, string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FControl", "scripts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{SanitizeFileName(config.Name)}-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "script" : sanitized;
    }

    private static ScriptRunResult CreateImmediateResult(CustomHotkeyConfig config, bool succeeded, string message, bool timedOut)
    {
        return CreateResult(config, DateTimeOffset.Now, TimeSpan.Zero, succeeded, timedOut, null, message, string.Empty, string.Empty);
    }

    private static ScriptRunResult CreateResult(
        CustomHotkeyConfig config,
        DateTimeOffset startedAt,
        TimeSpan duration,
        bool succeeded,
        bool timedOut,
        int? exitCode,
        string message,
        string output,
        string error)
    {
        return new ScriptRunResult
        {
            HotkeyId = config.Id,
            HotkeyName = config.Name,
            Hotkey = config.Hotkey,
            ScriptType = config.ScriptType,
            StartedAt = startedAt,
            DurationMilliseconds = (int)Math.Clamp(Math.Round(duration.TotalMilliseconds), 0, int.MaxValue),
            Succeeded = succeeded,
            TimedOut = timedOut,
            ExitCode = exitCode,
            Message = message,
            OutputSummary = Summarize(output),
            ErrorSummary = Summarize(error)
        };
    }

    private static string Summarize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = value.Replace("\r", string.Empty).Trim();
        return compact.Length <= 800 ? compact : compact[..800] + "...";
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task TryMinimizeAfterStartAsync(Process process)
    {
        await Task.Delay(300);
        try
        {
            if (process.MainWindowHandle != 0)
            {
                _ = ShowWindow(process.MainWindowHandle, 6);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(':') || value.Contains('\\') || value.Contains('/');
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private sealed class RunningScript(CancellationTokenSource cancellationTokenSource)
    {
        public Process? Process { get; set; }

        public void CancelAndKill()
        {
            try
            {
                cancellationTokenSource.Cancel();
            }
            catch
            {
                // ignore
            }

            if (Process is not null)
            {
                TryKill(Process);
            }
        }
    }

    private sealed record ScriptCommand(bool Succeeded, string FileName, string Arguments, string WorkingDirectory, string Message)
    {
        public static ScriptCommand Success(string fileName, string arguments, string workingDirectory)
        {
            return new ScriptCommand(true, fileName, arguments, workingDirectory, string.Empty);
        }

        public static ScriptCommand Failure(string message)
        {
            return new ScriptCommand(false, string.Empty, string.Empty, string.Empty, message);
        }
    }
}
